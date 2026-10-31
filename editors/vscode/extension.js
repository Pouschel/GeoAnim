// GeoAnim for VS Code: a live preview of the .geo file being edited, its errors in the editor, and the preview following
// the step the cursor is in; likewise for the pages of a book in Markdown (.geo.md, language "geomd"). The work
// is done by "geoanim daemon" (JSON lines over standard input and output); the preview is GeoAnim's own player (or its
// page for Markdown) in a webview, which gets the animations (pages) as messages.
'use strict'

const vscode = require('vscode')
const childProcess = require('child_process')
const fs = require('fs')
const os = require('os')
const path = require('path')

const DEBOUNCE = 250

let output
let diagnostics
let daemon = null
let program = null
let watchedBuilds = []
let nextId = 1
const pending = new Map()
let preview = null
let previewDocument = null
let previewReady = false
let previewPageLoaded = false
let previewStep = null
// the preview of the pages (.geo.md), apart from the one of the scripts: both can be open at once (e.g. in two windows)
let pagePreview = null
let pageDocument = null
let pageReady = false
let pagePageLoaded = false
const lastResults = new Map()
const timers = new Map()
const reported = new Map()
const definitions = new Map()
const references = new Map()
let keywords = null
let colors = null
// a jump from the preview (Ctrl+click): the preview stays at its step
let keepPreviewStep = false
// the objects shown as ghosts in the preview: the one the cursor is on, and for a while the one under the mouse
let cursorName = null
let hoverName = null
let hoverTimer = null
let sentHighlight = ''
const inspections = new Map()

// ------------------------------------------------------------------ finding and starting geoanim

// the builds of geoanim in a folder (GeoAnim/bin/Debug|Release/net10.0), newest first
function buildsIn (folder) {
  const found = []
  for (const configuration of ['Debug', 'Release']) {
    for (const name of ['geoanim.exe', 'geoanim.dll', 'geoanim']) {
      const candidate = path.join(folder, 'GeoAnim', 'bin', configuration, 'net10.0', name)
      if (fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
        found.push(candidate)
        break
      }
    }
  }
  return found.sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs)
}

// the builds in the repository folder itself or in a folder GeoAnim beside it (scripts kept in a repository of their
// own next to the code: C:\Code\GeoBook and C:\Code\GeoAnim)
const buildsAt = folder => {
  const own = buildsIn(folder)
  return own.length > 0 ? own : buildsIn(path.join(folder, 'GeoAnim'))
}

// the geoanim to run: the setting geoanim.path (a program, or the folder of the GeoAnim repository), else the newest
// build in the workspace or above it (or above the file), also in a GeoAnim repository beside them, else geoanim on PATH
function findProgram () {
  watchedBuilds = []
  const folders = (vscode.workspace.workspaceFolders || []).map(f => f.uri.fsPath)
  let setting = vscode.workspace.getConfiguration('geoanim').get('path', '').trim()
  if (setting) {
    setting = setting.replace(/\$\{workspaceFolder\}/g, folders[0] || '')
    const resolved = path.isAbsolute(setting) || !setting.includes(path.sep) ? setting : path.resolve(folders[0] || '', setting)
    if (fs.existsSync(resolved) && fs.statSync(resolved).isDirectory()) {
      const builds = buildsAt(resolved)
      if (builds.length > 0) {
        watchedBuilds = builds
        return builds[0]
      }
      output.appendLine(`Kein Build von geoanim in ${resolved} gefunden (gesucht: GeoAnim\\bin\\Debug|Release\\net10.0).`)
    }
    return resolved
  }
  const starts = [...folders]
  const active = vscode.window.activeTextEditor
  if (active && active.document.uri.scheme === 'file') {
    starts.push(path.dirname(active.document.uri.fsPath))
  }
  for (const start of starts) {
    for (let folder = start; ; folder = path.dirname(folder)) {
      const builds = buildsAt(folder)
      if (builds.length > 0) {
        // both builds are watched: a new build of either runs from then on
        watchedBuilds = builds
        return builds[0]
      }
      if (path.dirname(folder) === folder) {
        break
      }
    }
  }
  return 'geoanim'
}

// a build runs from a copy in the temp folder, so that Visual Studio can still build (Windows locks running programs)
function copyOfBuild (file) {
  const root = path.join(os.tmpdir(), 'geoanim-vscode')
  try {
    for (const old of fs.readdirSync(root)) {
      fs.rmSync(path.join(root, old), { recursive: true, force: true })
    }
  } catch (error) {
    // in use or not there: never mind
  }
  const target = path.join(root, String(Date.now()))
  fs.cpSync(path.dirname(file), target, { recursive: true })
  return path.join(target, path.basename(file))
}

function startDaemon () {
  program = findProgram()
  let command = program
  let args = ['daemon']
  if (path.isAbsolute(program) && fs.existsSync(program)) {
    try {
      command = copyOfBuild(program)
    } catch (error) {
      output.appendLine(`Kopie nicht möglich (${error.message}), starte das Original.`)
    }
  }
  watchBuilds(watchedBuilds.length > 0 ? watchedBuilds : (path.isAbsolute(program) && fs.existsSync(program) ? [program] : []))
  if (command.toLowerCase().endsWith('.dll')) {
    args = [command, 'daemon']
    command = 'dotnet'
  }
  output.appendLine(`Starte ${program}`)
  const started = childProcess.spawn(command, args, { stdio: ['pipe', 'pipe', 'pipe'] })
  let answered = false
  let buffer = ''
  started.stdout.setEncoding('utf8')
  started.stdout.on('data', chunk => {
    buffer += chunk
    let end
    while ((end = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, end).trim()
      buffer = buffer.slice(end + 1)
      if (!line) {
        continue
      }
      let answer
      try {
        answer = JSON.parse(line)
      } catch (error) {
        output.appendLine(line)
        continue
      }
      if (!answered && answer.html === undefined) {
        // geoanim answers again (e.g. after a new build): the previews get their pages now
        if (preview && !previewPageLoaded) {
          setTimeout(loadPreviewPage, 0)
        }
        if (pagePreview && !pagePageLoaded) {
          setTimeout(loadPagePreview, 0)
        }
      }
      answered = true
      const waiting = pending.get(answer.id)
      if (waiting) {
        pending.delete(answer.id)
        waiting.resolve(answer)
      }
    }
  })
  started.stderr.setEncoding('utf8')
  started.stderr.on('data', text => output.append(text))
  started.on('error', error => {
    output.appendLine(`geoanim konnte nicht gestartet werden: ${error.message}`)
    vscode.window.showErrorMessage(`GeoAnim: "${program}" konnte nicht gestartet werden. Bitte die Einstellung "geoanim.path" prüfen.`)
  })
  started.on('exit', code => {
    output.appendLine(`geoanim beendet (${code})`)
    if (!answered && daemon === started) {
      // typically a build from before "geoanim daemon" existed: it prints its usage and ends
      vscode.window.showErrorMessage(
        `GeoAnim: "${program}" läuft nicht als Hintergrunddienst (Exit-Code ${code}). Ist der Build aktuell? ` +
        'Bitte GeoAnim in Visual Studio neu bauen; Details im Ausgabefenster „GeoAnim“.', 'Ausgabe zeigen')
        .then(choice => { if (choice) output.show() })
    }
    if (daemon === started) {
      daemon = null
    }
    for (const waiting of pending.values()) {
      waiting.reject(new Error('geoanim beendet'))
    }
    pending.clear()
  })
  started.stdin.setDefaultEncoding('utf8')
  // writing to a process that could not start: the 'error' handler above reports it
  started.stdin.on('error', () => {})
  daemon = started
}

function stopDaemon () {
  if (daemon) {
    const old = daemon
    daemon = null
    old.stdin.end()
    old.kill()
  }
}

// after a new build: run the new version (the newest build)
let watching = []

function watchBuilds (files) {
  for (const file of watching.filter(f => !files.includes(f))) {
    fs.unwatchFile(file)
  }
  for (const file of files.filter(f => !watching.includes(f))) {
    fs.watchFile(file, { interval: 2000 }, (now, before) => {
      if (now.mtimeMs !== before.mtimeMs && now.size > 0) {
        output.appendLine(`Neuer Build von geoanim gefunden: ${file}`)
        restart()
      }
    })
  }
  watching = [...files]
}

function request (message) {
  if (!daemon) {
    startDaemon()
  }
  const id = nextId++
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject })
    daemon.stdin.write(JSON.stringify({ id, ...message }) + '\n')
  })
}

function restart () {
  // a new build may know other keywords
  keywords = null
  colors = null
  stopDaemon()
  startDaemon()
  if (preview) {
    loadPreviewPage()
  }
  if (pagePreview) {
    loadPagePreview()
  }
  for (const document of vscode.workspace.textDocuments) {
    if (kindOf(document)) {
      compile(document)
    }
  }
}

// ------------------------------------------------------------------ compiling and errors

const isGeo = document => document.languageId === 'geo'

// a page of a book in Markdown (.geo.md)
const isPage = document => document.languageId === 'geomd'

const kindOf = document => isGeo(document) ? 'geo' : isPage(document) ? 'markdown' : null

function schedule (document) {
  const key = document.uri.toString()
  clearTimeout(timers.get(key))
  timers.set(key, setTimeout(() => {
    timers.delete(key)
    compile(document)
  }, DEBOUNCE))
}

async function compile (document) {
  if (!isGeo(document)) {
    return renderMarkdown(document)
  }
  let result
  try {
    result = await request({ type: 'compile', file: document.uri.scheme === 'file' ? document.uri.fsPath : '', text: document.getText() })
  } catch (error) {
    return
  }
  if (document.isClosed) {
    return
  }
  if (result.fatal) {
    output.appendLine(result.fatal)
    return
  }
  lastResults.set(document.uri.toString(), result)
  rememberDefinitions(document, result)
  report(document, result.errors, result.warnings || [])
  if (document === previewDocument) {
    // while writing: the animation shows the end of the step the cursor is in (its end may have moved)
    sendResult(result, true, cursorStep(document))
  }
}

// a Markdown page: its errors in the editor, the page in the preview
async function renderMarkdown (document) {
  let result
  try {
    result = await request({ type: 'markdown', file: document.uri.scheme === 'file' ? document.uri.fsPath : '', text: document.getText() })
  } catch (error) {
    return
  }
  if (document.isClosed) {
    return
  }
  if (result.fatal) {
    output.appendLine(result.fatal)
    return
  }
  lastResults.set(document.uri.toString(), result)
  report(document, result.errors, result.warnings || [])
  if (document === pageDocument) {
    sendPage(result)
  }
}

// errors as red underlines (and in the problems view), also in included files; warnings (e.g. a keyword of two scripts
// of a book) yellow
function report (document, errors, warnings = []) {
  const key = document.uri.toString()
  for (const uri of reported.get(key) || []) {
    diagnostics.delete(uri)
  }
  const byFile = new Map()
  const all = errors.map(error => [error, vscode.DiagnosticSeverity.Error])
    .concat(warnings.map(warning => [warning, vscode.DiagnosticSeverity.Warning]))
  for (const [error, severity] of all) {
    const uri = error.file ? vscode.Uri.file(error.file) : document.uri
    const line = Math.max(0, error.line - 1)
    const column = Math.max(0, error.column - 1)
    const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString())
    let range = new vscode.Range(line, column, line, column + 1)
    if (open && line < open.lineCount) {
      range = open.getWordRangeAtPosition(new vscode.Position(line, column), /[^\s]+/) || new vscode.Range(line, column, line, open.lineAt(line).text.length)
    }
    const diagnostic = new vscode.Diagnostic(range, error.message, severity)
    diagnostic.source = 'GeoAnim'
    const list = byFile.get(uri.toString()) || { uri, items: [] }
    list.items.push(diagnostic)
    byFile.set(uri.toString(), list)
  }
  for (const { uri, items } of byFile.values()) {
    diagnostics.set(uri, items)
  }
  reported.set(key, [...byFile.values()].map(entry => entry.uri))
}

// ------------------------------------------------------------------ tooltips and "go to definition"

// the names the script defines (vars, texts, objects) from geoanim: [{ name, kind, text, file, line, column }]. While
// the script has a mistake, the list may end before it: then the last complete one stays
function rememberDefinitions (document, result) {
  const key = document.uri.toString()
  const fresh = result.definitions || []
  const old = definitions.get(key) || []
  if (result.errors.length === 0 || fresh.length >= old.length) {
    definitions.set(key, fresh)
    references.set(key, result.references || [])
  }
}

const NAME = /[A-Za-z_][A-Za-z0-9_]*'*/

// the definition of the name at the position: the last one before it in this file, else the last one at all
function definitionAt (document, position) {
  const range = document.getWordRangeAtPosition(position, NAME)
  if (!range) {
    return null
  }
  const name = document.getText(range)
  const all = (definitions.get(document.uri.toString()) || []).filter(d => d.name === name)
  if (all.length === 0) {
    return null
  }
  const here = all.filter(d => isThisFile(document, d.file) && d.line - 1 <= position.line)
  return { definition: (here.length > 0 ? here : all)[(here.length > 0 ? here : all).length - 1], range }
}

const isThisFile = (document, file) => !file || path.resolve(file).toLowerCase() === document.uri.fsPath.toLowerCase()

async function hover (document, position) {
  const found = definitionAt(document, position)
  if (!found) {
    return null
  }
  const { definition, range } = found
  const text = new vscode.MarkdownString()
  text.appendCodeblock(definition.text, 'geo')
  const kind = { var: 'Var', set: 'Text', object: 'Objekt' }[definition.kind] || definition.kind
  text.appendMarkdown(`${kind}, ${isThisFile(document, definition.file) ? '' : path.basename(definition.file) + ', '}Zeile ${definition.line}`)
  // an object in the script shown in the preview: its values there now, and its ghost in the picture
  if (definition.kind === 'object' && preview && previewReady && document === previewDocument) {
    showHoverGhost(definition.name)
    const now = await inspect(definition.name)
    if (now) {
      const when = now.step > 0 ? `Schritt ${now.step}` : 'Startbild'
      text.appendMarkdown(`\n\n---\n\n**Vorschau, ${when}:** ` + (now.info === null ? 'hier noch nicht definiert' : now.info))
    }
  }
  return new vscode.Hover(text, range)
}

// asks the preview for an object's values at its current time (null if it does not answer quickly)
function inspect (name) {
  const id = nextId++
  return new Promise(resolve => {
    const timer = setTimeout(() => {
      inspections.delete(id)
      resolve(null)
    }, 500)
    inspections.set(id, result => {
      clearTimeout(timer)
      resolve(result)
    })
    preview.webview.postMessage({ type: 'inspect', id, name })
  })
}

// ghosts: the object under the mouse for a few seconds (a tooltip has no end that an extension sees), the one the
// cursor is on as long as it stays there
function showHoverGhost (name) {
  hoverName = name
  clearTimeout(hoverTimer)
  hoverTimer = setTimeout(() => {
    hoverName = null
    sendHighlight()
  }, 4000)
  sendHighlight()
}

function sendHighlight () {
  const names = [...new Set([cursorName, hoverName].filter(name => name))]
  const key = names.join(' ')
  if (!preview || !previewReady || key === sentHighlight) {
    return
  }
  sentHighlight = key
  preview.webview.postMessage({ type: 'highlight', names })
}

function definitionLocation (document, position) {
  const found = definitionAt(document, position)
  if (!found) {
    return null
  }
  const { definition } = found
  const uri = definition.file ? vscode.Uri.file(definition.file) : document.uri
  return new vscode.Location(uri, new vscode.Position(definition.line - 1, Math.max(0, definition.column - 1)))
}

// ------------------------------------------------------------------ renaming, references, highlights (in the file itself)

const rangeOf = (reference) => new vscode.Range(reference.line - 1, reference.column - 1, reference.line - 1, reference.column - 1 + reference.name.length)

// the place of a name at the position (not a word in a description: only where geoanim found a name)
function referenceAt (document, position, list) {
  return (list || references.get(document.uri.toString()) || [])
    .find(r => isThisFile(document, r.file) && rangeOf(r).contains(position)) || null
}

const referencesInFile = (document, name, list) =>
  (list || references.get(document.uri.toString()) || []).filter(r => r.name === name && isThisFile(document, r.file))

// a name defined in an included file is used in other files too: renaming across files is not there yet
function checkRenamable (document, reference) {
  const definition = (definitions.get(document.uri.toString()) || []).find(d => d.name === reference.name)
  if (definition && !isThisFile(document, definition.file)) {
    throw new Error(`${reference.name} ist in ${path.basename(definition.file)} definiert; Umbenennen über Dateien hinweg gibt es noch nicht.`)
  }
}

function prepareRename (document, position) {
  const reference = referenceAt(document, position)
  if (!reference) {
    throw new Error('Hier steht kein Name eines Objekts, einer Var, eines Textes oder einer Zahl.')
  }
  checkRenamable(document, reference)
  return rangeOf(reference)
}

async function provideRenameEdits (document, position, newName) {
  // the places as they are now (the text may have changed since the last compile)
  const result = await request({ type: 'compile', file: document.uri.scheme === 'file' ? document.uri.fsPath : '', text: document.getText() })
  rememberDefinitions(document, result)
  const reference = referenceAt(document, position, result.references)
  if (!reference) {
    throw new Error('Hier steht kein Name eines Objekts, einer Var, eines Textes oder einer Zahl.')
  }
  checkRenamable(document, reference)
  if (!/^[A-Za-z_][A-Za-z0-9_]*'*$/.test(newName)) {
    throw new Error(`„${newName}“ ist kein Name: Buchstaben, Ziffern und _, dahinter beliebig viele ' (A', P_1).`)
  }
  if (keywords === null) {
    try {
      keywords = new Set((await request({ type: 'keywords' })).keywords || [])
    } catch (error) {
      keywords = new Set()
    }
  }
  if (keywords.has(newName)) {
    throw new Error(`„${newName}“ ist ein Schlüsselwort und kann kein Name sein.`)
  }
  if (newName !== reference.name && (result.definitions || []).some(d => d.name === newName)) {
    throw new Error(`Der Name ${newName} ist schon vergeben.`)
  }
  const edit = new vscode.WorkspaceEdit()
  for (const place of referencesInFile(document, reference.name, result.references)) {
    edit.replace(document.uri, rangeOf(place), newName)
  }
  return edit
}

function provideReferences (document, position, context) {
  const reference = referenceAt(document, position)
  if (!reference) {
    return []
  }
  const definition = (definitions.get(document.uri.toString()) || []).find(d => d.name === reference.name)
  return (references.get(document.uri.toString()) || [])
    .filter(r => r.name === reference.name)
    .filter(r => context.includeDeclaration || !definition || r.file !== definition.file || r.line !== definition.line || r.column !== definition.column)
    .map(r => new vscode.Location(r.file ? vscode.Uri.file(r.file) : document.uri, rangeOf(r)))
}

function provideDocumentHighlights (document, position) {
  const reference = referenceAt(document, position)
  return reference ? referencesInFile(document, reference.name).map(r => new vscode.DocumentHighlight(rangeOf(r))) : []
}

// ------------------------------------------------------------------ preview of the scripts

async function loadPreviewPage () {
  previewReady = false
  try {
    const answer = await request({ type: 'player' })
    if (preview && answer.html) {
      preview.webview.html = answer.html
      previewPageLoaded = true
    }
  } catch (error) {
    // geoanim did not start: the error message says so
  }
}

function sendResult (result, keepPosition, step) {
  if (!preview || !previewReady) {
    return
  }
  if (result.animation) {
    preview.webview.postMessage({ type: 'animation', animation: result.animation, keepPosition, step })
    if (step !== undefined) {
      previewStep = step
    }
  }
  const error = result.errors[0]
  preview.webview.postMessage({
    type: 'error',
    error: error ? `${error.file && previewDocument && error.file !== previewDocument.uri.fsPath ? path.basename(error.file) + ', l' : 'L'}ine ${error.line}, column ${error.column}: ${error.message}` : null
  })
}

function showInPreview (document) {
  previewDocument = document
  previewStep = null
  preview.title = 'GeoAnim: ' + path.basename(document.uri.fsPath)
  const result = lastResults.get(document.uri.toString())
  if (result) {
    sendResult(result, false)
  }
  compile(document)
}

// the preview for the active editor: the player for a script, the page for a page
function openPreview () {
  const editor = vscode.window.activeTextEditor
  if (editor && isPage(editor.document)) {
    openPagePreview(editor.document)
    return
  }
  if (!editor || !isGeo(editor.document)) {
    vscode.window.showInformationMessage('GeoAnim: Bitte zuerst eine .geo- oder .geo.md-Datei öffnen.')
    return
  }
  if (preview) {
    preview.reveal(vscode.ViewColumn.Beside, true)
    showInPreview(editor.document)
    return
  }
  preview = vscode.window.createWebviewPanel('geoanimPreview', 'GeoAnim', { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
    { enableScripts: true, retainContextWhenHidden: true })
  preview.onDidDispose(() => {
    preview = null
    previewDocument = null
    previewPageLoaded = false
  })
  preview.webview.onDidReceiveMessage(message => {
    if (message && message.type === 'open') {
      openLink(message.href, previewDocument)
    }
    if (message && message.type === 'step' && typeof message.index === 'number') {
      revealStep(message.index)
    }
    if (message && message.type === 'reveal' && Array.isArray(message.names)) {
      revealDefinition(message.names)
    }
    if (message && message.type === 'inspected' && inspections.has(message.id)) {
      const answer = inspections.get(message.id)
      inspections.delete(message.id)
      answer(message.result)
    }
    if (message && message.type === 'ready') {
      previewReady = true
      sentHighlight = null
      if (previewDocument) {
        const result = lastResults.get(previewDocument.uri.toString())
        if (result) {
          sendResult(result, false)
        } else {
          compile(previewDocument)
        }
      }
    }
  })
  previewDocument = editor.document
  preview.title = 'GeoAnim: ' + path.basename(editor.document.uri.fsPath)
  loadPreviewPage()
  compile(editor.document)
}

// ------------------------------------------------------------------ preview of the pages

async function loadPagePreview () {
  pageReady = false
  try {
    const answer = await request({ type: 'page' })
    if (pagePreview && answer.html) {
      pagePreview.webview.html = answer.html
      pagePageLoaded = true
    }
  } catch (error) {
    // geoanim did not start: the error message says so
  }
}

// the page, at the place the editor shows (unless keepPosition: while writing it stays where it is)
function sendPage (result, keepPosition) {
  if (!pagePreview || !pageReady) {
    return
  }
  const error = result.errors[0]
  pagePreview.webview.postMessage({
    type: 'page',
    html: result.html,
    figures: result.figures || {},
    error: error ? `Line ${error.line}, column ${error.column}: ${error.message}` : null,
    line: keepPosition ? undefined : topLine(pageDocument)
  })
}

function showPage (document) {
  pageDocument = document
  pagePreview.title = 'GeoAnim: ' + path.basename(document.uri.fsPath)
  const result = lastResults.get(document.uri.toString())
  if (result) {
    sendPage(result)
  }
  compile(document)
}

function openPagePreview (document) {
  if (pagePreview) {
    pagePreview.reveal(vscode.ViewColumn.Beside, true)
    showPage(document)
    return
  }
  pagePreview = vscode.window.createWebviewPanel('geoanimPagePreview', 'GeoAnim', { viewColumn: vscode.ViewColumn.Beside, preserveFocus: true },
    { enableScripts: true, retainContextWhenHidden: true })
  pagePreview.onDidDispose(() => {
    pagePreview = null
    pageDocument = null
    pagePageLoaded = false
  })
  pagePreview.webview.onDidReceiveMessage(message => {
    if (message && message.type === 'open') {
      openLink(message.href, pageDocument)
    }
    if (message && message.type === 'reveal' && typeof message.line === 'number') {
      revealInPage(message)
    }
    if (message && message.type === 'ready') {
      pageReady = true
      if (pageDocument) {
        const result = lastResults.get(pageDocument.uri.toString())
        if (result) {
          sendPage(result)
        } else {
          compile(pageDocument)
        }
      }
    }
  })
  pageDocument = document
  pagePreview.title = 'GeoAnim: ' + path.basename(document.uri.fsPath)
  loadPagePreview()
  compile(document)
}

// a link in a preview of the document: a site in the browser, another animation (Prop1.html) as its script (Prop1.geo),
// a page of the book (a .md file) or a figure of a page ("Punkte.geo#3..4") as its script at the step
async function openLink (href, from) {
  if (/^[a-z][a-z0-9+.-]*:/i.test(href)) {
    vscode.env.openExternal(vscode.Uri.parse(href))
    return
  }
  if (!from || from.uri.scheme !== 'file') {
    return
  }
  const target = path.resolve(path.dirname(from.uri.fsPath), decodeURIComponent(href.split(/[?#]/)[0]))
  // a link to a step ("Strecken.geo#2", as the keyword links of a book): the cursor goes to its "step" line
  const step = /#(\d+)(?:(?:\.\.|-)[^#]*)?$/.exec(href)
  if (/\.md$/i.test(target)) {
    if (fs.existsSync(target)) {
      const page = await vscode.workspace.openTextDocument(target)
      const editor = await openBeside(page, from)
      // a reference to a mark on that page ("strecken.geo.md#def-strecke"): the cursor goes to the mark
      const mark = /#([\p{L}\p{N}_-]+)$/u.exec(href)
      if (mark && !revealMark(editor, mark[1])) {
        revealKeyword(editor, from, mark[1])
      }
    } else {
      vscode.window.showInformationMessage(`GeoAnim: Die Seite ${href} gibt es nicht.`)
    }
    return
  }
  const script = target.replace(/\.html?$/i, '.geo')
  if (!fs.existsSync(script)) {
    vscode.window.showInformationMessage(`GeoAnim: Zu ${href} gibt es kein Skript (gesucht: ${script}).`)
    return
  }
  const document = await vscode.workspace.openTextDocument(script)
  const editor = await openBeside(document, from)
  if (step && Number(step[1]) > 0) {
    for (let i = 0, count = 0; i < document.lineCount; i++) {
      if (/^\s*(?:text)?step\b/.test(document.lineAt(i).text) && ++count === Number(step[1])) {
        const line = document.lineAt(i)
        const position = new vscode.Position(i, line.firstNonWhitespaceCharacterIndex)
        editor.selection = new vscode.Selection(position, position)
        editor.revealRange(line.range, vscode.TextEditorRevealType.InCenterIfOutsideViewport)
        break
      }
    }
  }
  // the preview follows the newly opened script (onDidChangeActiveTextEditor)
}

// a document in the editor group of the one shown so far (from) – never in a preview's group, so that the previews stay
// visible
function openBeside (document, from) {
  const previewColumns = [preview, pagePreview].filter(p => p).map(p => p.viewColumn)
  const shown = (vscode.window.visibleTextEditors || []).find(editor => editor.document === from)
  let column = shown ? shown.viewColumn : undefined
  if (!column || previewColumns.includes(column)) {
    column = [vscode.ViewColumn.One, vscode.ViewColumn.Two, vscode.ViewColumn.Three].find(c => !previewColumns.includes(c))
  }
  return vscode.window.showTextDocument(document, { viewColumn: column, preview: false })
}

// the first line (from 1) the editor of a Markdown page shows: the preview shows the same place
function topLine (document) {
  const editor = document && (vscode.window.visibleTextEditors || []).find(e => e.document === document)
  return editor && editor.visibleRanges.length > 0 ? editor.visibleRanges[0].start.line + 1 : undefined
}

function followScrolling (editor) {
  // not while the editor goes to a place clicked in the preview (the preview stays where it is)
  if (Date.now() < keepPagePosition) {
    return
  }
  if (pagePreview && pageReady && editor.document === pageDocument) {
    const line = topLine(editor.document)
    if (line !== undefined) {
      pagePreview.webview.postMessage({ type: 'line', line })
    }
  }
}

// the step the cursor is in: the number of "step" lines up to it, minus one (-1: the starting picture); undefined if the
// document is not in an editor
function cursorStep (document) {
  const active = vscode.window.activeTextEditor
  const editor = active && active.document === document ? active : (vscode.window.visibleTextEditors || []).find(e => e.document === document)
  if (!editor) {
    return undefined
  }
  const line = editor.selection.active.line
  let index = -1
  for (let i = 0; i <= line && i < document.lineCount; i++) {
    if (/^\s*(?:text)?step\b/.test(document.lineAt(i).text)) {
      index++
    }
  }
  return index
}

// the viewer went to a step in the preview: the cursor goes to its "step" line (to the first line for the starting
// picture), unless it is in that step already; the focus stays in the preview
function revealStep (index) {
  previewStep = index
  const document = previewDocument
  const editor = document && (vscode.window.visibleTextEditors || []).find(e => e.document === document)
  if (!editor || cursorStep(document) === index) {
    return
  }
  // a step from an included or used file has no line here: the cursor stays
  let target = index < 0 ? 0 : null
  for (let i = 0, count = -1; target === null && i < document.lineCount; i++) {
    if (/^\s*(?:text)?step\b/.test(document.lineAt(i).text)) {
      count++
      if (count === index) {
        target = i
      }
    }
  }
  if (target === null) {
    return
  }
  const line = document.lineAt(target)
  const position = new vscode.Position(target, line.firstNonWhitespaceCharacterIndex)
  editor.selection = new vscode.Selection(position, position)
  editor.revealRange(line.range, vscode.TextEditorRevealType.InCenterIfOutsideViewport)
}

function followCursor (editor) {
  if (!preview || !previewReady || editor.document !== previewDocument) {
    return
  }
  const index = cursorStep(editor.document)
  if (keepPreviewStep) {
    keepPreviewStep = false
  } else if (index !== undefined && index !== previewStep) {
    previewStep = index
    preview.webview.postMessage({ type: 'step', index })
  }
  // the object whose name the cursor is on: its ghost in the preview
  const found = editor.selection.isEmpty ? definitionAt(editor.document, editor.selection.active) : null
  cursorName = found && found.definition.kind === 'object' ? found.definition.name : null
  sendHighlight()
}

// ------------------------------------------------------------------ from the preview to the script (Ctrl+click)

// an object clicked in the preview: the cursor goes to the name in its definition (in this file), the preview stays at
// its step, and the tooltip shows its values
async function revealDefinition (names) {
  const document = previewDocument
  if (!document) {
    return
  }
  const all = definitions.get(document.uri.toString()) || []
  const definition = all.find(d => names.includes(d.name) && isThisFile(document, d.file)) || all.find(d => names.includes(d.name))
  if (!definition) {
    vscode.window.setStatusBarMessage(`GeoAnim: ${names[0]} ist nicht in dieser Datei definiert.`, 4000)
    return
  }
  const target = definition.file && !isThisFile(document, definition.file) ? await vscode.workspace.openTextDocument(definition.file) : document
  const shown = (vscode.window.visibleTextEditors || []).find(e => e.document === target)
  const previewColumn = preview ? preview.viewColumn : undefined
  let column = shown ? shown.viewColumn : undefined
  if (!column || column === previewColumn) {
    column = previewColumn === vscode.ViewColumn.One ? vscode.ViewColumn.Two : vscode.ViewColumn.One
  }
  keepPreviewStep = target === document
  const editor = await vscode.window.showTextDocument(target, { viewColumn: column, preserveFocus: false })
  const start = new vscode.Position(definition.line - 1, Math.max(0, definition.column - 1))
  editor.selection = new vscode.Selection(start, start)
  editor.revealRange(new vscode.Range(start, start), vscode.TextEditorRevealType.InCenterIfOutsideViewport)
  vscode.commands.executeCommand('editor.action.showHover')
}

// ------------------------------------------------------------------ outline, folding, colour swatches

const STEP_LINE = /^\s*(?:(text)?step)\b\s*(.*)$/

// the kind of a definition from its line ("point A = …" → point; "var …", "set …")
function definedKind (definition) {
  if (definition.kind !== 'object') {
    return definition.kind
  }
  const match = /^\s*(?:(?:input|output)\s+)?([a-z]+)\b/.exec(definition.text || '')
  return match ? match[1] : 'object'
}

const SYMBOL_KINDS = {
  point: vscode.SymbolKind.Constant,
  segment: vscode.SymbolKind.Interface,
  line: vscode.SymbolKind.Interface,
  ray: vscode.SymbolKind.Interface,
  circle: vscode.SymbolKind.Struct,
  arc: vscode.SymbolKind.Struct,
  sector: vscode.SymbolKind.Struct,
  polygon: vscode.SymbolKind.Class,
  angle: vscode.SymbolKind.Enum,
  text: vscode.SymbolKind.String,
  set: vscode.SymbolKind.String,
  number: vscode.SymbolKind.Number,
  var: vscode.SymbolKind.Variable
}

// the outline: the starting picture and the steps, each with the names defined in it
async function provideDocumentSymbols (document) {
  // before the first compilation the names are not known yet
  if (!definitions.has(document.uri.toString())) {
    await compile(document)
  }
  const sections = []
  let current = { name: 'Startbild', detail: '', line: 0, children: [] }
  sections.push(current)
  for (let i = 0; i < document.lineCount; i++) {
    const match = STEP_LINE.exec(document.lineAt(i).text)
    if (match) {
      current.end = i - 1
      current = { name: `${sections.length} ${match[2].trim() || '(ohne Beschreibung)'}`, detail: match[1] ? 'textstep' : 'step', line: i, children: [] }
      sections.push(current)
    }
  }
  current.end = document.lineCount - 1
  const seen = new Set()
  for (const definition of definitions.get(document.uri.toString()) || []) {
    if (!isThisFile(document, definition.file) || seen.has(definition.name + ':' + definition.line)) {
      continue
    }
    seen.add(definition.name + ':' + definition.line)
    const line = definition.line - 1
    if (line < 0 || line >= document.lineCount) {
      continue
    }
    const section = sections.filter(s => s.line <= line).pop()
    const kind = definedKind(definition)
    const nameRange = new vscode.Range(line, Math.max(0, definition.column - 1), line, Math.max(0, definition.column - 1) + definition.name.length)
    section.children.push(new vscode.DocumentSymbol(definition.name, kind, SYMBOL_KINDS[kind] || vscode.SymbolKind.Object, document.lineAt(line).range, nameRange))
  }
  return sections
    .filter(s => s.line > 0 || s.children.length > 0 || sections.length === 1)
    .map(s => {
      const range = new vscode.Range(s.line, 0, Math.max(s.line, s.end), document.lineAt(Math.max(s.line, s.end)).text.length)
      const symbol = new vscode.DocumentSymbol(s.name, s.detail, vscode.SymbolKind.Namespace, range, document.lineAt(s.line).range)
      symbol.children = s.children
      return symbol
    })
}

// folding: each step (up to the next one) and each together block
function provideFoldingRanges (document) {
  const ranges = []
  let stepStart = -1
  const together = []
  for (let i = 0; i < document.lineCount; i++) {
    const text = document.lineAt(i).text
    if (STEP_LINE.test(text)) {
      if (stepStart >= 0 && i - 1 > stepStart) {
        ranges.push(new vscode.FoldingRange(stepStart, lastContentLine(document, stepStart, i - 1)))
      }
      stepStart = i
    } else if (/^\s*together\b/.test(text)) {
      together.push(i)
    } else if (/^\s*end\b/.test(text) && together.length > 0) {
      ranges.push(new vscode.FoldingRange(together.pop(), i))
    }
  }
  if (stepStart >= 0 && document.lineCount - 1 > stepStart) {
    ranges.push(new vscode.FoldingRange(stepStart, lastContentLine(document, stepStart, document.lineCount - 1)))
  }
  return ranges
}

// a step folds up to its last line that is not empty
function lastContentLine (document, start, end) {
  while (end > start && document.lineAt(end).text.trim().length === 0) {
    end--
  }
  return end
}

// ------------------------------------------------------------------ files changed elsewhere

// a script or page changed (saved here, or written by another VS Code window): the other open scripts and pages anew,
// as they may include it, use it or show it as a figure (the daemon compiles a changed file again)
function refreshOthers (changed) {
  for (const document of vscode.workspace.textDocuments) {
    if (kindOf(document) && document.uri.fsPath !== changed) {
      schedule(document)
    }
  }
}

// watchers for the books of the open scripts and pages (the folder with index.yaml, else the file's folder): also
// outside the workspace, and also for changes by other programs
const fileWatchers = new Map()
let refreshTimer = null

function bookFolderOf (file) {
  for (let folder = path.dirname(file); ; folder = path.dirname(folder)) {
    if (fs.existsSync(path.join(folder, 'index.yaml'))) {
      return folder
    }
    if (path.dirname(folder) === folder) {
      return path.dirname(file)
    }
  }
}

function watchFolderOf (document) {
  if (!kindOf(document) || document.uri.scheme !== 'file') {
    return
  }
  const folder = bookFolderOf(document.uri.fsPath)
  if (fileWatchers.has(folder)) {
    return
  }
  const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(vscode.Uri.file(folder), '**/*.{geo,md}'))
  const changed = uri => {
    // a file open here and unchanged in the editor follows by itself; the others are compiled again, once for a burst
    clearTimeout(refreshTimer)
    refreshTimer = setTimeout(() => refreshOthers(uri.fsPath), 300)
  }
  watcher.onDidChange(changed)
  watcher.onDidCreate(changed)
  watcher.onDidDelete(changed)
  fileWatchers.set(folder, watcher)
}

// ------------------------------------------------------------------ pages (.geo.md): editor and preview together

let keepPagePosition = 0
let selectionTimer = null

// markup that the preview does not show as text: emphasis, code marks; formulas are no text there at all
const plainText = text => text.replace(/\\([*_`$§])/g, '$1').replace(/[*_`]/g, '')

// the line where the paragraph of a line begins (after an empty line, a heading or a container line)
function paragraphStart (document, line) {
  let start = line
  while (start > 0) {
    const before = document.lineAt(start - 1).text
    if (/^\s*$/.test(before) || /^\s*(#{1,6}\s|:{3,}|\$\$\s*$|```|~~~|---\s*$)/.test(before)) {
      break
    }
    start--
  }
  return start
}

// the cursor of a page's editor: its block marked in the preview, a selected text (on one line, without formulas)
// highlighted there – as the how-manyth time it is in its paragraph
function sendSelection (editor) {
  clearTimeout(selectionTimer)
  selectionTimer = setTimeout(() => {
    if (!pagePreview || !pageReady || editor.document !== pageDocument) {
      return
    }
    const selected = editor.selection
    const line = selected.start.line
    let text = null
    let occurrence = 0
    if (!selected.isEmpty && selected.isSingleLine) {
      const raw = editor.document.getText(selected)
      const wanted = plainText(raw).trim()
      if (wanted && !raw.includes('$') && wanted.length <= 200) {
        text = wanted
        const start = paragraphStart(editor.document, line)
        const before = plainText(editor.document.getText(new vscode.Range(start, 0, line, selected.start.character)).replace(/\$[^$]*\$/g, ' '))
        occurrence = before.split(wanted).length - 1
      }
    }
    pagePreview.webview.postMessage({ type: 'select', line: line + 1, text, occurrence })
  }, 80)
}

// a word (or formula) double-clicked in the preview: the cursor goes to it in the editor, in the paragraph of its line
// – the how-manyth time it is there, not counting formulas
async function revealInPage (message) {
  const document = pageDocument
  if (!document) {
    return
  }
  const shown = (vscode.window.visibleTextEditors || []).find(e => e.document === document)
  keepPagePosition = Date.now() + 800
  const editor = await vscode.window.showTextDocument(document, { viewColumn: shown ? shown.viewColumn : vscode.ViewColumn.One, preserveFocus: false })
  const first = Math.max(0, message.line - 1)
  let last = first
  while (last + 1 < document.lineCount && !/^\s*$/.test(document.lineAt(last + 1).text)) {
    last++
  }
  let range = null
  if (message.formula) {
    const wanted = message.formula.trim()
    for (let i = first; i <= last && !range; i++) {
      const text = document.lineAt(i).text
      const pattern = /\$\$?([^$]+)\$\$?/g
      for (let found = pattern.exec(text); found; found = pattern.exec(text)) {
        if (found[1].trim() === wanted) {
          range = new vscode.Range(i, found.index, i, found.index + found[0].length)
          break
        }
      }
    }
  } else if (message.word) {
    const pattern = new RegExp('(?<![\\p{L}\\p{N}_-])' + message.word + '(?![\\p{L}\\p{N}_-])', 'gu')
    let count = 0
    let firstFound = null
    for (let i = first; i <= last && !range; i++) {
      // formulas do not count (the preview shows them as pictures); emphasis marks stay where they are
      const text = document.lineAt(i).text.replace(/\$[^$]*\$/g, m => ' '.repeat(m.length))
      for (let found = pattern.exec(text); found; found = pattern.exec(text)) {
        const here = new vscode.Range(i, found.index, i, found.index + found[0].length)
        firstFound = firstFound || here
        if (count++ === (message.occurrence || 0)) {
          range = here
          break
        }
      }
    }
    range = range || firstFound
  }
  range = range || new vscode.Range(first, 0, first, 0)
  editor.selection = new vscode.Selection(range.start, range.end)
  editor.revealRange(range, vscode.TextEditorRevealType.InCenterIfOutsideViewport)
}

// ------------------------------------------------------------------ pages (.geo.md): marks, references, steps

// "§name" at the position (not "\§name"): the mark's name and the range of the reference
function markReferenceAt (document, position) {
  const range = document.getWordRangeAtPosition(position, /§§?\*?[\p{L}\p{N}_](?:[\p{L}\p{N}_-]*[\p{L}\p{N}_])?/u)
  if (!range) {
    return null
  }
  if (range.start.character > 0 && document.lineAt(range.start.line).text[range.start.character - 1] === '\\') {
    return null
  }
  return { name: document.getText(range).replace(/^§§?\*?/, ''), range }
}

// the marks of the book, from the last answer for the page: [{ name, kind, label, text, file, line }]
const marksOf = document => (lastResults.get(document.uri.toString()) || {}).marks || []

function markAt (document, position) {
  const reference = markReferenceAt(document, position)
  return reference ? { reference, mark: marksOf(document).find(m => m.name === reference.name) } : null
}

function markLocation (document, mark) {
  return new vscode.Location(mark.file ? vscode.Uri.file(mark.file) : document.uri, new vscode.Position(Math.max(0, mark.line - 1), 0))
}

// the cursor on the line with the mark ("{#name}")
function revealMark (editor, name) {
  // a name has only letters, digits, "_" and "-": nothing to escape
  const pattern = new RegExp('\\{[^}]*#' + name + '(?![\\p{L}\\p{N}_-])', 'u')
  for (let i = 0; i < editor.document.lineCount; i++) {
    const match = pattern.exec(editor.document.lineAt(i).text)
    if (match) {
      const position = new vscode.Position(i, match.index)
      editor.selection = new vscode.Selection(position, position)
      editor.revealRange(new vscode.Range(position, position), vscode.TextEditorRevealType.InCenterIfOutsideViewport)
      return true
    }
  }
  return false
}

// the cursor on the term of a keyword ("kw-strahl": **Strahl**{kw=…}), from what the daemon said about the book of the
// page the link was in
function revealKeyword (editor, from, anchor) {
  const result = from ? lastResults.get(from.uri.toString()) : null
  const keyword = ((result && result.keywords) || []).find(k => k.anchor === anchor && k.file && k.file.toLowerCase() === editor.document.uri.fsPath.toLowerCase())
  if (keyword) {
    const position = new vscode.Position(Math.max(0, keyword.line - 1), Math.max(0, keyword.column - 1))
    editor.selection = new vscode.Selection(position, position)
    editor.revealRange(new vscode.Range(position, position), vscode.TextEditorRevealType.InCenterIfOutsideViewport)
  }
}

// the page's own script: seite.geo for seite.geo.md
const ownScript = document => document.uri.scheme === 'file' && /\.geo\.md$/i.test(document.uri.fsPath) ? document.uri.fsPath.slice(0, -3) : null

// completion: after "§" the marks of the book; in "![](#" or "![](datei.geo#" (also after "..") the steps of the script
async function providePageCompletions (document, position) {
  const before = document.lineAt(position.line).text.slice(0, position.character)
  const reference = /(\\?)§§?\*?([\p{L}\p{N}_-]*)$/u.exec(before)
  if (reference && !reference[1]) {
    const range = new vscode.Range(position.translate(0, -reference[2].length), position)
    return marksOf(document).map(mark => {
      const item = new vscode.CompletionItem({ label: mark.name, description: mark.label }, vscode.CompletionItemKind.Reference)
      item.detail = mark.label + (mark.file && mark.file !== document.uri.fsPath ? ' · ' + path.basename(mark.file) : '')
      item.documentation = mark.text || undefined
      item.filterText = mark.name
      item.range = range
      return item
    })
  }
  const figure = /!\[[^\]]*\]\(([^()\s#]*)#(?:[\p{L}\p{N}_-]+\.\.)?([\p{L}\p{N}_-]*)$/u.exec(before)
  if (!figure) {
    return []
  }
  const script = figure[1] ? (document.uri.scheme === 'file' ? path.resolve(path.dirname(document.uri.fsPath), decodeURIComponent(figure[1])) : null) : ownScript(document)
  if (!script) {
    return []
  }
  let answer
  try {
    answer = await request({ type: 'steps', file: script })
  } catch (error) {
    return []
  }
  const range = new vscode.Range(position.translate(0, -figure[2].length), position)
  return (answer.steps || []).map(step => {
    const name = step.mark || step.name || String(step.number)
    const item = new vscode.CompletionItem({ label: name, description: 'Schritt ' + step.number }, step.mark ? vscode.CompletionItemKind.Constant : vscode.CompletionItemKind.Value)
    item.detail = `Schritt ${step.number}: ${step.caption}` + (step.mark ? ' (Marke)' : '')
    item.sortText = String(step.number).padStart(4, '0')
    item.filterText = name + ' ' + step.number
    item.range = range
    return item
  })
}

// the tooltip of a reference: what it refers to
function providePageHover (document, position) {
  const found = markAt(document, position)
  if (!found) {
    return null
  }
  const { reference, mark } = found
  if (!mark) {
    return new vscode.Hover(`Unbekannte Marke \`${reference.name}\``, reference.range)
  }
  const text = new vscode.MarkdownString()
  text.appendMarkdown(`**${mark.label}**`)
  if (mark.text) {
    text.appendText(' · ' + mark.text)
  }
  if (mark.file && mark.file !== document.uri.fsPath) {
    text.appendMarkdown(`\n\n${path.basename(mark.file)}, Zeile ${mark.line}`)
  }
  return new vscode.Hover(text, reference.range)
}

// F12 / Ctrl+click: on a reference its mark; on a link or figure ("[…](seite.geo.md#marke)", "![…](datei.geo#3..4)") the
// page at the mark or the script at the step; on a keyword of the book (as the page links it) the place of its term
async function providePageDefinition (document, position) {
  const found = markAt(document, position)
  if (found) {
    return found.mark ? markLocation(document, found.mark) : null
  }
  const link = linkAt(document, position)
  if (link) {
    return linkLocation(document, link)
  }
  return keywordLocation(document, position)
}

// the link or figure under the cursor: { image, href }
function linkAt (document, position) {
  const text = document.lineAt(position.line).text
  const pattern = /(!?)\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*<?([^()\s>]*)>?(?:\s+"[^"]*")?\s*\)/g
  for (let match; (match = pattern.exec(text));) {
    if (position.character >= match.index && position.character <= match.index + match[0].length) {
      return { image: match[1] === '!', href: match[2] }
    }
  }
  return null
}

// the text of a file: as in its editor (unsaved changes too), else as saved
function linesOf (file) {
  const open = vscode.workspace.textDocuments.find(d => d.uri.scheme === 'file' && d.uri.fsPath.toLowerCase() === file.toLowerCase())
  if (open) {
    return open.getText().split(/\r?\n/)
  }
  try {
    return fs.readFileSync(file, 'utf8').split(/\r?\n/)
  } catch (error) {
    return null
  }
}

async function linkLocation (document, { image, href }) {
  if (/^[a-z][a-z0-9+.-]*:/i.test(href) || document.uri.scheme !== 'file') {
    return null
  }
  const hash = href.indexOf('#')
  const file = decodeURIComponent(hash < 0 ? href : href.slice(0, hash))
  const anchor = hash < 0 ? '' : decodeURIComponent(href.slice(hash + 1))
  // a figure of the page's own script ("![](#3)"), else the file beside the page
  const target = file ? path.resolve(path.dirname(document.uri.fsPath), file) : image ? ownScript(document) : document.uri.fsPath
  if (!target) {
    return null
  }
  const script = target.replace(/\.html?$/i, '.geo')
  if (/\.geo$/i.test(script)) {
    return fs.existsSync(script) ? new vscode.Location(vscode.Uri.file(script), await stepPosition(script, anchor)) : null
  }
  if (!fs.existsSync(target)) {
    return null
  }
  return new vscode.Location(vscode.Uri.file(target), anchor ? markPosition(document, target, anchor) : new vscode.Position(0, 0))
}

// the place of a mark on a page ("{#def-strecke}"), else of a keyword's term ("kw-strecke"), else the start
function markPosition (from, file, anchor) {
  const lines = linesOf(file) || []
  const pattern = new RegExp('\\{[^}]*#' + anchor.replace(/[^\p{L}\p{N}_-]/gu, '') + '(?![\\p{L}\\p{N}_-])', 'u')
  const line = lines.findIndex(l => pattern.test(l))
  if (line >= 0) {
    return new vscode.Position(line, lines[line].search(pattern))
  }
  const result = lastResults.get(from.uri.toString())
  const keyword = ((result && result.keywords) || []).find(k => k.anchor === anchor && k.file && k.file.toLowerCase() === file.toLowerCase())
  return keyword ? new vscode.Position(Math.max(0, keyword.line - 1), Math.max(0, keyword.column - 1)) : new vscode.Position(0, 0)
}

// the "step" line of a step of a script: by its number, mark or name ("3", "gleich", "3..4": the first), else the start
async function stepPosition (script, anchor) {
  const first = anchor.split('..')[0]
  let number = /^\d+$/.test(first) ? Number(first) : 0
  if (first && !number) {
    try {
      const steps = (await request({ type: 'steps', file: script })).steps || []
      const step = steps.find(s => s.mark === first) || steps.find(s => s.name === first)
      number = step ? step.number : 0
    } catch (error) {
    }
  }
  const lines = linesOf(script) || []
  for (let i = 0, count = 0; number > 0 && i < lines.length; i++) {
    if (/^\s*(?:text)?step\b/.test(lines[i]) && ++count === number) {
      return new vscode.Position(i, lines[i].search(/\S/))
    }
  }
  return new vscode.Position(0, 0)
}

// a keyword of the book under the cursor (one of its forms, as the page links it): the place of its term
function keywordLocation (document, position) {
  const result = lastResults.get(document.uri.toString())
  const keywords = ((result && result.keywords) || []).filter(k => k.file && k.forms)
  const text = document.lineAt(position.line).text
  let best = null
  for (const keyword of keywords) {
    for (const form of keyword.forms) {
      const escaped = form.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/ /g, '\\s+')
      const pattern = new RegExp('(?<![\\p{L}\\p{N}])' + escaped + '(?![\\p{L}\\p{N}])', 'gu')
      for (let match; (match = pattern.exec(text));) {
        if (position.character >= match.index && position.character <= match.index + match[0].length
          && (!best || match[0].length > best.length)) {
          best = { keyword, length: match[0].length }
        }
      }
    }
  }
  if (!best) {
    return null
  }
  const { keyword } = best
  return new vscode.Location(vscode.Uri.file(keyword.file), new vscode.Position(Math.max(0, keyword.line - 1), Math.max(0, keyword.column - 1)))
}

// ------------------------------------------------------------------ pages (.geo.md): outline and folding

// the parts of a page: the YAML block at the start, code blocks (``` or ~~~) and headings ("# …" to "###### …", not in
// those two)
function pageParts (document) {
  const parts = { front: null, fences: [], headings: [] }
  let i = 0
  if (document.lineCount > 0 && /^---\s*$/.test(document.lineAt(0).text)) {
    for (let k = 1; k < document.lineCount; k++) {
      if (/^(---|\.\.\.)\s*$/.test(document.lineAt(k).text)) {
        parts.front = [0, k]
        i = k + 1
        break
      }
    }
  }
  let fence = null
  for (; i < document.lineCount; i++) {
    const text = document.lineAt(i).text
    const mark = /^\s{0,3}(`{3,}|~{3,})/.exec(text)
    if (fence) {
      if (mark && mark[1][0] === fence.mark[0] && mark[1].length >= fence.mark.length && text.trim() === mark[1]) {
        parts.fences.push([fence.line, i])
        fence = null
      }
      continue
    }
    if (mark) {
      fence = { mark: mark[1], line: i }
      continue
    }
    const heading = /^\s{0,3}(#{1,6})\s+(.*?)(?:\s+#+)?\s*$/.exec(text)
    if (heading) {
      parts.headings.push({ line: i, level: heading[1].length, text: heading[2] || '…' })
    }
  }
  return parts
}

// the last line of each heading's section: before the next heading of the same or a higher level
function sectionEnds (document, headings) {
  return headings.map((heading, index) => {
    const next = headings.slice(index + 1).find(h => h.level <= heading.level)
    return lastContentLine(document, heading.line, next ? next.line - 1 : document.lineCount - 1)
  })
}

// the outline of a page: its headings, nested by level
function providePageSymbols (document) {
  const { headings } = pageParts(document)
  const ends = sectionEnds(document, headings)
  const roots = []
  const open = []
  headings.forEach((heading, index) => {
    const line = document.lineAt(heading.line)
    const symbol = new vscode.DocumentSymbol(heading.text, '', vscode.SymbolKind.String,
      new vscode.Range(heading.line, 0, ends[index], document.lineAt(ends[index]).text.length), line.range)
    while (open.length > 0 && open[open.length - 1].level >= heading.level) {
      open.pop()
    }
    ;(open.length > 0 ? open[open.length - 1].symbol.children : roots).push(symbol)
    open.push({ level: heading.level, symbol })
  })
  return roots
}

// folding: each heading's section, the YAML block at the start, code blocks
function providePageFoldingRanges (document) {
  const { front, fences, headings } = pageParts(document)
  const ends = sectionEnds(document, headings)
  const ranges = headings.map((heading, index) => new vscode.FoldingRange(heading.line, ends[index]))
  if (front) {
    ranges.push(new vscode.FoldingRange(front[0], front[1], vscode.FoldingRangeKind.Region))
  }
  for (const [start, end] of fences) {
    ranges.push(new vscode.FoldingRange(start, end))
  }
  // statements ("::: axiom" … ":::")
  for (const block of statementBlocks(document)) {
    ranges.push(new vscode.FoldingRange(block.start, block.end))
  }
  return ranges.filter(range => range.end > range.start)
}

// colour swatches: after color, fill and background (names and #rgb / #rrggbb), with the colour picker
async function provideDocumentColors (document) {
  if (colors === null) {
    try {
      colors = (await request({ type: 'keywords' })).colors || {}
    } catch (error) {
      colors = {}
    }
  }
  const found = []
  const pattern = /\b(?:color|fill|background)\s+(#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b|[A-Za-z]+\b)/g
  for (let i = 0; i < document.lineCount; i++) {
    const text = document.lineAt(i).text.replace(/#(?![0-9a-fA-F]{3}\b|[0-9a-fA-F]{6}\b).*$/, '')
    for (const match of text.matchAll(pattern)) {
      const value = match[1]
      const hex = value.startsWith('#') ? value : colors[value.toLowerCase()]
      if (!hex) {
        continue
      }
      const full = hex.length === 4 ? '#' + [...hex.slice(1)].map(c => c + c).join('') : hex
      const n = parseInt(full.slice(1), 16)
      const start = match.index + match[0].length - value.length
      found.push(new vscode.ColorInformation(new vscode.Range(i, start, i, start + value.length),
        new vscode.Color(((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255, 1)))
    }
  }
  return found
}

// a colour picked: written as #rrggbb (a name stays a name if it is exactly that colour)
function provideColorPresentations (color) {
  const part = v => Math.round(v * 255).toString(16).padStart(2, '0')
  const hex = '#' + part(color.red) + part(color.green) + part(color.blue)
  const name = Object.keys(colors || {}).find(n => colors[n] === hex)
  return [new vscode.ColorPresentation(name || hex)]
}

// ------------------------------------------------------------------ together blocks (Ctrl+.)

// selected lines: put them into a together block; the cursor in a together block: dissolve it
function provideCodeActions (document, range) {
  const actions = []
  if (!range.isEmpty) {
    const action = new vscode.CodeAction('In together … end einschließen', vscode.CodeActionKind.RefactorRewrite)
    action.command = { command: 'geoanim.surroundTogether', title: action.title, arguments: [document.uri, range.start.line, lastLineOf(range)] }
    actions.push(action)
  }
  const block = togetherAround(document, range.start.line)
  if (block) {
    const action = new vscode.CodeAction('together-Block auflösen', vscode.CodeActionKind.RefactorRewrite)
    action.edit = dissolveEdit(document, block)
    actions.push(action)
  }
  return actions
}

// a selection that ends at the start of a line (whole lines selected) ends on the line before
const lastLineOf = range => range.end.character === 0 && range.end.line > range.start.line ? range.end.line - 1 : range.end.line

// one step of indentation as the editor has it
function indentUnit (editor) {
  const { insertSpaces, tabSize } = editor.options
  return insertSpaces === false ? '\t' : ' '.repeat(typeof tabSize === 'number' ? tabSize : 2)
}

// the lines first … last indented one step more between "together" and "end" (with the first line's indentation)
async function surroundTogether (uri, first, last) {
  const editor = vscode.window.activeTextEditor
  if (!editor || editor.document.uri.toString() !== uri.toString()) {
    return
  }
  const document = editor.document
  const lines = []
  for (let i = first; i <= last; i++) {
    lines.push(document.lineAt(i).text)
  }
  const indent = /^\s*/.exec(lines.find(line => line.trim().length > 0) || '')[0]
  const unit = indentUnit(editor)
  const text = indent + 'together\n' + lines.map(line => line.trim().length > 0 ? unit + line : line).join('\n') + '\n' + indent + 'end'
  const range = new vscode.Range(first, 0, last, document.lineAt(last).text.length)
  await editor.edit(builder => builder.replace(range, text))
}

// the together block around a line: the lines of "together" and "end", or null
function togetherAround (document, line) {
  let start = -1
  for (let i = line; i >= 0; i--) {
    const text = document.lineAt(i).text
    if (/^\s*together\b/.test(text)) {
      start = i
      break
    }
    if (/^\s*end\b/.test(text) && i < line) {
      return null
    }
  }
  if (start < 0) {
    return null
  }
  for (let i = start + 1; i < document.lineCount; i++) {
    if (/^\s*end\b/.test(document.lineAt(i).text)) {
      return i >= line ? { start, end: i } : null
    }
    if (/^\s*together\b/.test(document.lineAt(i).text)) {
      return null
    }
  }
  return null
}

// without "together" and "end"; the lines between one step less indented (as far as they are indented more than the block)
function dissolveEdit (document, block) {
  const edit = new vscode.WorkspaceEdit()
  const outer = /^\s*/.exec(document.lineAt(block.start).text)[0]
  const inner = []
  for (let i = block.start + 1; i < block.end; i++) {
    inner.push(document.lineAt(i).text)
  }
  // the common indentation of the lines inside, beyond the block's own
  const extra = Math.min(...inner.filter(line => line.trim().length > 0).map(line => /^\s*/.exec(line)[0].length), Infinity)
  const cut = Number.isFinite(extra) ? Math.max(0, extra - outer.length) : 0
  const text = inner.map(line => line.trim().length > 0 ? outer + line.slice(outer.length + cut) : line)
  const range = new vscode.Range(block.start, 0, block.end, document.lineAt(block.end).text.length)
  edit.replace(document.uri, range, text.join('\n'))
  return edit
}

// ------------------------------------------------------------------ statement blocks in pages (Ctrl+.)

// the kinds of statements as the lightbulb offers them (as in REFERENZ-MARKDOWN.md)
const STATEMENT_KINDS = [
  ['definition', 'Definition'],
  ['axiom', 'Axiom'],
  ['theorem', 'Satz'],
  ['lemma', 'Hilfssatz'],
  ['corollary', 'Folgerung'],
  ['proof', 'Beweis'],
  ['example', 'Beispiel'],
  ['remark', 'Bemerkung'],
  ['box', 'Kasten']
]

const statementName = kind => {
  const known = STATEMENT_KINDS.find(([name]) => name === kind || (kind === 'def' && name === 'definition'))
  return known ? known[1] : kind
}

// the blocks "::: kind …" … ":::" of a page (not in code blocks): { start, end, colons, kind }; a closing line closes the
// innermost open block with at most as many colons
function statementBlocks (document) {
  const blocks = []
  const open = []
  let fence = null
  for (let i = 0; i < document.lineCount; i++) {
    const text = document.lineAt(i).text
    const mark = /^\s{0,3}(`{3,}|~{3,})/.exec(text)
    if (fence) {
      if (mark && mark[1][0] === fence[0] && mark[1].length >= fence.length && text.trim() === mark[1]) {
        fence = null
      }
      continue
    }
    if (mark) {
      fence = mark[1]
      continue
    }
    const opening = /^\s{0,3}(:{3,})\s*([A-Za-z]+)/.exec(text)
    if (opening) {
      open.push({ start: i, colons: opening[1].length, kind: opening[2].toLowerCase() })
      continue
    }
    const closing = /^\s{0,3}(:{3,})\s*$/.exec(text)
    if (closing) {
      for (let k = open.length - 1; k >= 0; k--) {
        if (open[k].colons <= closing[1].length) {
          blocks.push({ ...open[k], end: i })
          open.splice(k)
          break
        }
      }
    }
  }
  return blocks
}

// selected lines: put them into a block of each kind; the cursor in a block: dissolve it (the innermost)
function providePageCodeActions (document, range) {
  const actions = []
  const blocks = statementBlocks(document)
  if (!range.isEmpty) {
    const first = range.start.line
    const last = lastLineOf(range)
    for (const [kind, name] of STATEMENT_KINDS) {
      const action = new vscode.CodeAction(`In ${name} einschließen`, vscode.CodeActionKind.RefactorRewrite)
      action.command = { command: 'geoanim.surroundStatement', title: action.title, arguments: [document.uri, first, last, kind] }
      actions.push(action)
    }
  }
  const line = range.start.line
  const around = blocks.filter(block => block.start <= line && line <= block.end).sort((a, b) => (a.end - a.start) - (b.end - b.start))[0]
  if (around) {
    const action = new vscode.CodeAction(`${statementName(around.kind)}-Block auflösen`, vscode.CodeActionKind.RefactorRewrite)
    action.edit = new vscode.WorkspaceEdit()
    // the closing line first: the lines stay where they are for the opening one
    action.edit.delete(document.uri, lineWithBreak(document, around.end))
    action.edit.delete(document.uri, lineWithBreak(document, around.start))
    actions.push(action)
  }
  return actions
}

// a whole line with its line break (the last line: with the break before it)
function lineWithBreak (document, line) {
  return line + 1 < document.lineCount
    ? new vscode.Range(line, 0, line + 1, 0)
    : new vscode.Range(line - 1, document.lineAt(line - 1).text.length, line, document.lineAt(line).text.length)
}

// the lines first … last between "::: kind" and ":::" (more colons if there are blocks inside), with an empty line
// before and after where the text goes on; the cursor after the kind, for a mark and a title ("{#name} Titel")
async function surroundStatement (uri, first, last, kind) {
  const editor = vscode.window.activeTextEditor
  if (!editor || editor.document.uri.toString() !== uri.toString()) {
    return
  }
  const document = editor.document
  // without empty lines at the start and end of the selection
  while (first < last && document.lineAt(first).isEmptyOrWhitespace) {
    first++
  }
  while (last > first && document.lineAt(last).isEmptyOrWhitespace) {
    last--
  }
  const inner = statementBlocks(document).filter(block => block.start >= first && block.end <= last)
  const colons = ':'.repeat(Math.max(3, ...inner.map(block => block.colons + 1)))
  const lines = []
  for (let i = first; i <= last; i++) {
    lines.push(document.lineAt(i).text)
  }
  const before = first > 0 && !document.lineAt(first - 1).isEmptyOrWhitespace ? '\n' : ''
  const after = last + 1 < document.lineCount && !document.lineAt(last + 1).isEmptyOrWhitespace ? '\n' : ''
  const opening = `${colons} ${kind}`
  const text = before + opening + '\n' + lines.join('\n') + '\n' + colons + after
  const range = new vscode.Range(first, 0, last, document.lineAt(last).text.length)
  const done = await editor.edit(builder => builder.replace(range, text))
  if (done) {
    const line = first + (before ? 1 : 0)
    const position = new vscode.Position(line, opening.length)
    editor.selection = new vscode.Selection(position, position)
  }
}

// ------------------------------------------------------------------ activation

function activate (context) {
  output = vscode.window.createOutputChannel('GeoAnim')
  diagnostics = vscode.languages.createDiagnosticCollection('geoanim')
  context.subscriptions.push(output, diagnostics,
    vscode.commands.registerCommand('geoanim.preview', openPreview),
    vscode.commands.registerCommand('geoanim.restart', restart),
    vscode.commands.registerCommand('geoanim.surroundTogether', surroundTogether),
    vscode.commands.registerCommand('geoanim.surroundStatement', surroundStatement),
    vscode.languages.registerDocumentSymbolProvider('geo', { provideDocumentSymbols }, { label: 'GeoAnim' }),
    vscode.languages.registerFoldingRangeProvider('geo', { provideFoldingRanges }),
    vscode.languages.registerDocumentSymbolProvider('geomd', { provideDocumentSymbols: providePageSymbols }, { label: 'GeoAnim' }),
    vscode.languages.registerFoldingRangeProvider('geomd', { provideFoldingRanges: providePageFoldingRanges }),
    vscode.languages.registerCompletionItemProvider('geomd', { provideCompletionItems: providePageCompletions }, '§', '#', '.'),
    vscode.languages.registerHoverProvider('geomd', { provideHover: providePageHover }),
    vscode.languages.registerDefinitionProvider('geomd', { provideDefinition: providePageDefinition }),
    vscode.languages.registerColorProvider('geo', { provideDocumentColors, provideColorPresentations }),
    vscode.languages.registerCodeActionsProvider('geo', { provideCodeActions }, { providedCodeActionKinds: [vscode.CodeActionKind.RefactorRewrite] }),
    vscode.languages.registerCodeActionsProvider('geomd', { provideCodeActions: providePageCodeActions }, { providedCodeActionKinds: [vscode.CodeActionKind.RefactorRewrite] }),
    vscode.languages.registerHoverProvider('geo', { provideHover: hover }),
    vscode.languages.registerDefinitionProvider('geo', { provideDefinition: definitionLocation }),
    vscode.languages.registerRenameProvider('geo', { prepareRename, provideRenameEdits }),
    vscode.languages.registerReferenceProvider('geo', { provideReferences }),
    vscode.languages.registerDocumentHighlightProvider('geo', { provideDocumentHighlights }),
    vscode.workspace.onDidOpenTextDocument(document => { if (kindOf(document)) compile(document) }),
    vscode.workspace.onDidChangeTextDocument(event => { if (kindOf(event.document)) schedule(event.document) }),
    // a saved file may be included or used by others, or be a figure of a page
    vscode.workspace.onDidSaveTextDocument(saved => {
      if (isGeo(saved)) {
        refreshOthers(saved.uri.fsPath)
      }
    }),
    // a script or page written by another program (another VS Code window): the pages and scripts open here again
    { dispose: () => { for (const watcher of fileWatchers.values()) watcher.dispose() } },
    vscode.workspace.onDidOpenTextDocument(document => watchFolderOf(document)),
    vscode.workspace.onDidCloseTextDocument(document => {
      const key = document.uri.toString()
      for (const uri of reported.get(key) || []) {
        diagnostics.delete(uri)
      }
      reported.delete(key)
      lastResults.delete(key)
      definitions.delete(key)
      references.delete(key)
    }),
    vscode.window.onDidChangeActiveTextEditor(editor => {
      // each preview follows the active editor of its kind
      if (editor && isGeo(editor.document) && preview && editor.document !== previewDocument) {
        showInPreview(editor.document)
      }
      if (editor && isPage(editor.document) && pagePreview && editor.document !== pageDocument) {
        showPage(editor.document)
      }
    }),
    vscode.window.onDidChangeTextEditorSelection(event => {
      followCursor(event.textEditor)
      if (isPage(event.textEditor.document)) {
        sendSelection(event.textEditor)
      }
    }),
    vscode.window.onDidChangeTextEditorVisibleRanges(event => followScrolling(event.textEditor)),
    vscode.workspace.onDidChangeConfiguration(event => { if (event.affectsConfiguration('geoanim.path')) restart() }),
    { dispose: () => { stopDaemon(); watchBuilds([]) } })
  for (const document of vscode.workspace.textDocuments) {
    if (kindOf(document)) {
      watchFolderOf(document)
      compile(document)
    }
  }
}

function deactivate () {
  stopDaemon()
}

module.exports = { activate, deactivate }
