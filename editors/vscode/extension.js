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

// the geoanim to run: the setting geoanim.path, else the newest build in the workspace (or above the file), else
// geoanim on PATH
function findProgram () {
  watchedBuilds = []
  const folders = (vscode.workspace.workspaceFolders || []).map(f => f.uri.fsPath)
  let setting = vscode.workspace.getConfiguration('geoanim').get('path', '').trim()
  if (setting) {
    setting = setting.replace(/\$\{workspaceFolder\}/g, folders[0] || '')
    return path.isAbsolute(setting) || !setting.includes(path.sep) ? setting : path.resolve(folders[0] || '', setting)
  }
  const starts = [...folders]
  const active = vscode.window.activeTextEditor
  if (active && active.document.uri.scheme === 'file') {
    starts.push(path.dirname(active.document.uri.fsPath))
  }
  for (const start of starts) {
    for (let folder = start; ; folder = path.dirname(folder)) {
      const builds = buildsIn(folder)
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
  report(document, result.errors)
  if (document === previewDocument) {
    // while writing: the animation shows the end of the step the cursor is in (its end may have moved)
    sendResult(result, true, cursorStep(document))
  }
}

// errors as red underlines (and in the problems view), also in included files
function report (document, errors) {
  const key = document.uri.toString()
  for (const uri of reported.get(key) || []) {
    diagnostics.delete(uri)
  }
  const byFile = new Map()
  for (const error of errors) {
    const uri = error.file ? vscode.Uri.file(error.file) : document.uri
    const line = Math.max(0, error.line - 1)
    const column = Math.max(0, error.column - 1)
    const open = vscode.workspace.textDocuments.find(d => d.uri.toString() === uri.toString())
    let range = new vscode.Range(line, column, line, column + 1)
    if (open && line < open.lineCount) {
      range = open.getWordRangeAtPosition(new vscode.Position(line, column), /[^\s]+/) || new vscode.Range(line, column, line, open.lineAt(line).text.length)
    }
    const diagnostic = new vscode.Diagnostic(range, error.message, vscode.DiagnosticSeverity.Error)
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
    if (message && message.type === 'ready') {
      previewReady = true
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
  await vscode.window.showTextDocument(document, { viewColumn: column, preview: false })
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

function followCursor (editor) {
  if (!preview || !previewReady || editor.document !== previewDocument) {
    return
  }
  const index = cursorStep(editor.document)
  if (index !== undefined && index !== previewStep) {
    previewStep = index
    preview.webview.postMessage({ type: 'step', index })
  }
}

// ------------------------------------------------------------------ activation

function activate (context) {
  output = vscode.window.createOutputChannel('GeoAnim')
  diagnostics = vscode.languages.createDiagnosticCollection('geoanim')
  context.subscriptions.push(output, diagnostics,
    vscode.commands.registerCommand('geoanim.preview', openPreview),
    vscode.commands.registerCommand('geoanim.restart', restart),
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
