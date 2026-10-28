// A page of a book in Markdown, in an editor's preview (VS Code): the editor sends the page made by "geoanim daemon"
// ({ type: "page", html, figures, error }) and the line at the top of the editor ({ type: "line", line }); links go
// back to the editor ({ type: "open", href }), which opens scripts and pages itself.
// The figures are players in frames (window.GEO_FIGURE_PLAYER with the figure's animation and part): a frame stays as
// long as its figure does not change, so that writing does not restart the animations.
'use strict'

;(function () {
  const page = document.getElementById('page')
  const error = document.getElementById('error')
  const host = typeof acquireVsCodeApi === 'function' ? acquireVsCodeApi() : null
  let shownHtml = null
  let figures = {}
  let nextFrame = 1

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

  // the blocks of the page that changed are replaced, the others stay (with their frames); a block is compared as
  // written (before frames were added to it), without its line (which moves when lines are added above it)
  const blockKey = element => element.outerHTML.replace(/ data-line="\d+"/g, '')

  function show (html) {
    if (html === shownHtml) {
      return
    }
    // the same place stays in view while writing
    const top = window.scrollY
    const fresh = document.createElement('div')
    fresh.innerHTML = html
    const before = Array.from(page.children)
    const after = Array.from(fresh.children)
    for (const element of after) {
      element.geoKey = blockKey(element)
    }
    let start = 0
    while (start < before.length && start < after.length && before[start].geoKey === after[start].geoKey) {
      start++
    }
    let end = 0
    while (end < before.length - start && end < after.length - start &&
      before[before.length - 1 - end].geoKey === after[after.length - 1 - end].geoKey) {
      end++
    }
    // the lines of the blocks that stay
    before.forEach((element, index) => {
      const replacement = index < start ? after[index] : index >= before.length - end ? after[index - before.length + after.length] : null
      if (replacement) {
        copyLines(replacement, element)
      }
    })
    const next = before[before.length - end] || null
    for (const element of before.slice(start, before.length - end)) {
      element.remove()
    }
    for (const element of after.slice(start, after.length - end)) {
      page.insertBefore(element, next)
    }
    shownHtml = html
    startFigures()
    window.scrollTo({ top })
  }

  function copyLines (from, to) {
    const source = [from, ...from.querySelectorAll('[data-line]')]
    const target = [to, ...to.querySelectorAll('[data-line]')]
    source.forEach((element, index) => {
      if (target[index] && element.dataset.line) {
        target[index].dataset.line = element.dataset.line
      }
    })
  }

  // ------------------------------------------------------------------ figures

  // a player in a frame for every figure that has none yet
  function startFigures () {
    for (const item of page.querySelectorAll('.geo-item[data-figure]')) {
      if (item.querySelector('iframe')) {
        continue
      }
      const data = figures[item.dataset.figure]
      if (!data || !window.GEO_FIGURE_PLAYER) {
        continue
      }
      const frame = document.createElement('iframe')
      const id = 'figure' + nextFrame++
      frame.dataset.id = id
      frame.title = 'Abbildung'
      const ratio = parseFloat(getComputedStyle(item).getPropertyValue('--ratio')) || 1.6
      if (data.still) {
        frame.style.aspectRatio = String(ratio)
      } else {
        // until the player tells its height: picture, description and controls
        frame.style.height = Math.round(item.clientWidth / ratio + 110) + 'px'
      }
      const script = 'window.GEO_ANIMATION = ' + JSON.stringify(data.animation) + ';\n' +
        'window.GEO_FIGURE = ' + JSON.stringify({ id, from: data.from, to: data.to, still: data.still }) + ';'
      // the JSON in a script element: "<" written as <, so that it cannot end the element
      frame.srcdoc = window.GEO_FIGURE_PLAYER.replace('/*GEO_FIGURE*/', () => script.replace(/[<]/g, '\\u003c'))
      item.appendChild(frame)
    }
  }

  const frameOf = id => page.querySelector(`iframe[data-id="${id}"]`)

  window.addEventListener('message', event => {
    const message = event.data
    if (!message) {
      return
    }
    if (message.type === 'geoanim-figure-height') {
      const frame = frameOf(message.id)
      if (frame) {
        frame.style.height = message.height + 'px'
      }
    } else if (message.type === 'geoanim-figure-play') {
      // one figure at a time
      for (const frame of page.querySelectorAll('iframe[data-id]')) {
        if (frame.dataset.id !== message.id && frame.contentWindow) {
          frame.contentWindow.postMessage({ type: 'geoanim-pause' }, '*')
        }
      }
    } else if (message.type === 'page') {
      figures = message.figures || {}
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
