// A page of a book in Markdown, in an editor's preview (VS Code): the editor sends the page made by "geoanim daemon"
// ({ type: "page", html, error }) and the line at the top of the editor ({ type: "line", line }); links go back to the
// editor ({ type: "open", href }), which opens scripts and pages itself.
'use strict'

;(function () {
  const page = document.getElementById('page')
  const error = document.getElementById('error')
  const host = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null
  let shownHtml = null

  // the element of the line (data-line): the last one that starts at it or before it
  function elementOf (line) {
    let found = null
    for (const element of page.querySelectorAll('[data-line]')) {
      if (Number(element.dataset.line) <= line) {
        found = element
      } else {
        break
      }
    }
    return found
  }

  function scrollToLine (line) {
    const element = elementOf(line)
    window.scrollTo({ top: element ? element.getBoundingClientRect().top + window.scrollY - 12 : 0 })
  }

  function show (html) {
    if (html === shownHtml) {
      return
    }
    // the same place stays in view while writing
    const top = window.scrollY
    page.innerHTML = html
    shownHtml = html
    window.scrollTo({ top })
  }

  window.addEventListener('message', event => {
    const message = event.data
    if (!message) {
      return
    }
    if (message.type === 'page') {
      if (message.html !== null && message.html !== undefined) {
        show(message.html)
      }
      error.textContent = message.error || ''
      error.hidden = !message.error
      if (typeof message.line === 'number') {
        scrollToLine(message.line)
      }
    } else if (message.type === 'line') {
      scrollToLine(message.line)
    }
  })

  // links: the editor opens them (a script or a page beside this one, or a site in the browser)
  document.addEventListener('click', event => {
    const link = event.target.closest('a[href]')
    if (!link || !host) {
      return
    }
    const href = link.getAttribute('href')
    if (href.startsWith('#')) {
      return
    }
    event.preventDefault()
    host.postMessage({ type: 'open', href })
  })

  if (host) {
    host.postMessage({ type: 'ready' })
  }
})()
