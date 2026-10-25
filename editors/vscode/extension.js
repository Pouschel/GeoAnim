// GeoAnim for VS Code: a live preview of the .geo file being edited, its errors in the editor, and the preview following
// the step the cursor is in. The work is done by "geoanim daemon" (JSON lines over standard input and output); the
// preview is GeoAnim's own player in a webview, which gets the animations as messages.
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
const lastResults = new Map()
const timers = new Map()
const reported = new Map()
const definitions = new Map()
const references = new Map()
let keywords = null
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
      if (!answered && preview && !previewPageLoaded && answer.html === undefined) {
        // geoanim answers again (e.g. after a new build): the preview gets its page now
        setTimeout(loadPreviewPage, 0)
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
  stopDaemon()
  startDaemon()
  if (preview) {
    loadPreviewPage()
  }
  for (const document of vscode.workspace.textDocuments) {
    if (isGeo(document)) {
      compile(document)
    }
  }
}

// ------------------------------------------------------------------ compiling and errors

const isGeo = document => document.languageId === 'geo'

function schedule (document) {
  const key = document.uri.toString()
  clearTimeout(timers.get(key))
  timers.set(key, setTimeout(() => {
    timers.delete(key)
    compile(document)
  }, DEBOUNCE))
}

async function compile (document) {
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

// ------------------------------------------------------------------ preview

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

function openPreview () {
  const editor = vscode.window.activeTextEditor
  if (!editor || !isGeo(editor.document)) {
    vscode.window.showInformationMessage('GeoAnim: Bitte zuerst eine .geo-Datei öffnen.')
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
      openLink(message.href)
    }
    if (message && message.type === 'step' && typeof message.index === 'number') {
      revealStep(message.index)
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

// a link in the preview: a site in the browser, another animation (Prop1.html) as its script (Prop1.geo)
async function openLink (href) {
  if (/^[a-z][a-z0-9+.-]*:/i.test(href)) {
    vscode.env.openExternal(vscode.Uri.parse(href))
    return
  }
  if (!previewDocument || previewDocument.uri.scheme !== 'file') {
    return
  }
  const target = path.resolve(path.dirname(previewDocument.uri.fsPath), decodeURIComponent(href.split(/[?#]/)[0]))
  // a link to a step ("Strecken.geo#2", as the keyword links of a book): the cursor goes to its "step" line
  const step = /#(\d+)$/.exec(href)
  const script = target.replace(/\.html?$/i, '.geo')
  if (!fs.existsSync(script)) {
    vscode.window.showInformationMessage(`GeoAnim: Zu ${href} gibt es kein Skript (gesucht: ${script}).`)
    return
  }
  const document = await vscode.workspace.openTextDocument(script)
  // in the editor group of the script shown so far – never in the preview's group, so that the preview stays visible
  const previewColumn = preview ? preview.viewColumn : undefined
  const shown = (vscode.window.visibleTextEditors || []).find(editor => editor.document === previewDocument)
  let column = shown ? shown.viewColumn : undefined
  if (!column || column === previewColumn) {
    column = previewColumn === vscode.ViewColumn.One ? vscode.ViewColumn.Two : vscode.ViewColumn.One
  }
  const editor = await vscode.window.showTextDocument(document, { viewColumn: column, preview: false })
  if (step && Number(step[1]) > 0) {
    for (let i = 0, count = 0; i < document.lineCount; i++) {
      if (/^\s*step\b/.test(document.lineAt(i).text) && ++count === Number(step[1])) {
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
    if (/^\s*step\b/.test(document.lineAt(i).text)) {
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
    if (/^\s*step\b/.test(document.lineAt(i).text)) {
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
  if (index !== undefined && index !== previewStep) {
    previewStep = index
    preview.webview.postMessage({ type: 'step', index })
  }
  // the object whose name the cursor is on: its ghost in the preview
  const found = editor.selection.isEmpty ? definitionAt(editor.document, editor.selection.active) : null
  cursorName = found && found.definition.kind === 'object' ? found.definition.name : null
  sendHighlight()
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

// the lines first … last indented one step more between "together" and "end" (with the first line's indentation); the
// cursor after "together", with a choice of durations
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
  const snippet = new vscode.SnippetString()
  snippet.appendText(indent + 'together')
  snippet.appendChoice(['', ' duration 500', ' duration 1000'])
  snippet.appendText('\n' + lines.map(line => line.trim().length > 0 ? unit + line : line).join('\n') + '\n' + indent + 'end')
  const range = new vscode.Range(first, 0, last, document.lineAt(last).text.length)
  await editor.insertSnippet(snippet, range, { keepWhitespace: true })
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

// ------------------------------------------------------------------ activation

function activate (context) {
  output = vscode.window.createOutputChannel('GeoAnim')
  diagnostics = vscode.languages.createDiagnosticCollection('geoanim')
  context.subscriptions.push(output, diagnostics,
    vscode.commands.registerCommand('geoanim.preview', openPreview),
    vscode.commands.registerCommand('geoanim.restart', restart),
    vscode.commands.registerCommand('geoanim.surroundTogether', surroundTogether),
    vscode.languages.registerCodeActionsProvider('geo', { provideCodeActions }, { providedCodeActionKinds: [vscode.CodeActionKind.RefactorRewrite] }),
    vscode.languages.registerHoverProvider('geo', { provideHover: hover }),
    vscode.languages.registerDefinitionProvider('geo', { provideDefinition: definitionLocation }),
    vscode.languages.registerRenameProvider('geo', { prepareRename, provideRenameEdits }),
    vscode.languages.registerReferenceProvider('geo', { provideReferences }),
    vscode.languages.registerDocumentHighlightProvider('geo', { provideDocumentHighlights }),
    vscode.workspace.onDidOpenTextDocument(document => { if (isGeo(document)) compile(document) }),
    vscode.workspace.onDidChangeTextDocument(event => { if (isGeo(event.document)) schedule(event.document) }),
    // a saved file may be included or used by others
    vscode.workspace.onDidSaveTextDocument(saved => {
      if (isGeo(saved)) {
        for (const document of vscode.workspace.textDocuments) {
          if (isGeo(document) && document !== saved) {
            schedule(document)
          }
        }
      }
    }),
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
      if (editor && isGeo(editor.document) && preview && editor.document !== previewDocument) {
        showInPreview(editor.document)
      }
    }),
    vscode.window.onDidChangeTextEditorSelection(event => followCursor(event.textEditor)),
    vscode.workspace.onDidChangeConfiguration(event => { if (event.affectsConfiguration('geoanim.path')) restart() }),
    { dispose: () => { stopDaemon(); watchBuilds([]) } })
  for (const document of vscode.workspace.textDocuments) {
    if (isGeo(document)) {
      compile(document)
    }
  }
}

function deactivate () {
  stopDaemon()
}

module.exports = { activate, deactivate }
