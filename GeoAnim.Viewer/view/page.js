// A page of a book in Markdown, in an editor's preview (VS Code): the editor sends the page made by "geoanim daemon"
// ({ type: "page", html, figures, error }), the line at the top of the editor ({ type: "line", line }) and where its
// cursor is ({ type: "select", line, text, occurrence }: the block of the line is marked, a selected text in it
// highlighted); links go back to the editor ({ type: "open", href }), which opens scripts and pages itself, and so does
// a double-click or Ctrl+click on the text ({ type: "reveal", line, word, occurrence } or { type: "reveal", line,
// formula }: the editor puts the cursor there).
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
    // the cursor's place again, in the new blocks
    if (selection) {
      markSelection(selection, false)
    }
  }

  // ------------------------------------------------------------------ the editor's cursor and selection

  let selection = null
  let markedBlock = null

  // the text of an element (without formulas and figures) with the text nodes it comes from
  function textOf (element) {
    const nodes = []
    let text = ''
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, {
      acceptNode: node => node.parentElement.closest('svg, iframe, .figure-label') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT
    })
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      nodes.push({ node, start: text.length })
      text += node.nodeValue
    }
    return { text, nodes }
  }

  // the place in the text nodes of a position in the text
  function pointAt (nodes, position) {
    for (let i = nodes.length - 1; i >= 0; i--) {
      if (nodes[i].start <= position) {
        return { node: nodes[i].node, offset: Math.min(position - nodes[i].start, nodes[i].node.nodeValue.length) }
      }
    }
    return null
  }

  const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

  // the occurrence-th place of the text in the element (spaces match any white space), as a range; else the first
  function findText (element, wanted, occurrence) {
    const { text, nodes } = textOf(element)
    const pattern = new RegExp(escape(wanted).replace(/\s+/g, '\\s+'), 'g')
    let match = null
    let first = null
    for (let count = 0, found = pattern.exec(text); found; found = pattern.exec(text), count++) {
      first = first || found
      if (count === occurrence) {
        match = found
        break
      }
    }
    match = match || first
    if (!match) {
      return null
    }
    const start = pointAt(nodes, match.index)
    const end = pointAt(nodes, match.index + match[0].length)
    const range = document.createRange()
    range.setStart(start.node, start.offset)
    range.setEnd(end.node, end.offset)
    return range
  }

  // the block of the cursor's line marked, the selected text highlighted; scrolled into view if it is not
  function markSelection (wanted, scroll) {
    selection = wanted
    if (markedBlock) {
      markedBlock.classList.remove('geo-cursor')
    }
    if (window.CSS && CSS.highlights) {
      CSS.highlights.delete('geo-selection')
    }
    markedBlock = elementOf(wanted.line)
    if (!markedBlock) {
      return
    }
    markedBlock.classList.add('geo-cursor')
    let target = markedBlock
    if (wanted.text) {
      const range = findText(markedBlock, wanted.text, wanted.occurrence || 0)
      if (range && window.CSS && CSS.highlights && typeof Highlight === 'function') {
        CSS.highlights.set('geo-selection', new Highlight(range))
        target = range
      }
    }
    if (scroll) {
      const box = target.getBoundingClientRect()
      if (box.bottom < 0 || box.top > window.innerHeight) {
        window.scrollBy({ top: box.top - window.innerHeight / 3 })
      }
    }
  }

  // a double-click or Ctrl+click on the text: the editor goes to that word (or formula)
  function revealFrom (event) {
    if (!host || event.target.closest('a[href], iframe, button')) {
      return false
    }
    const block = event.target.closest('[data-line]')
    if (!block) {
      return false
    }
    const line = Number(block.dataset.line)
    const formula = event.target.closest('svg.formula')
    if (formula) {
      host.postMessage({ type: 'reveal', line, formula: formula.getAttribute('aria-label') })
      return true
    }
    const caret = document.caretRangeFromPoint ? document.caretRangeFromPoint(event.clientX, event.clientY) : null
    if (!caret || caret.startContainer.nodeType !== Node.TEXT_NODE) {
      host.postMessage({ type: 'reveal', line })
      return true
    }
    // the word at the click, and how many times it is in the block before
    const { text, nodes } = textOf(block)
    const entry = nodes.find(n => n.node === caret.startContainer)
    if (!entry) {
      host.postMessage({ type: 'reveal', line })
      return true
    }
    const position = entry.start + caret.startOffset
    const isWordChar = c => c !== undefined && /[\p{L}\p{N}_-]/u.test(c)
    let start = position
    let end = position
    while (isWordChar(text[start - 1])) {
      start--
    }
    while (isWordChar(text[end])) {
      end++
    }
    const word = text.slice(start, end)
    if (!word) {
      host.postMessage({ type: 'reveal', line })
      return true
    }
    const before = text.slice(0, start).match(new RegExp('(?<![\\p{L}\\p{N}_-])' + escape(word) + '(?![\\p{L}\\p{N}_-])', 'gu'))
    host.postMessage({ type: 'reveal', line, word, occurrence: before ? before.length : 0 })
    return true
  }

  document.addEventListener('dblclick', event => {
    if (revealFrom(event)) {
      window.getSelection().removeAllRanges()
    }
  })
  document.addEventListener('click', event => {
    if ((event.ctrlKey || event.metaKey) && revealFrom(event)) {
      event.preventDefault()
    }
  }, true)

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

  // how figures show: 'book' as in the printed book (stills; steps from … to as a row of stills with their numbers and
  // descriptions; a whole animation as its last picture) or 'web' as on a web page (players)
  let mode = 'book'
  try {
    mode = (host && host.getState() && host.getState().mode) || 'book'
  } catch (ignored) {
  }

  // the book: the pictures as GeoAnim makes them for print, in proportion to the text as in print (the text of the
  // preview stands for the book's text of "print: text"); a row of stills with their steps' numbers and descriptions;
  // narrower if the column is too narrow
  function printPictures (item, data) {
    const pixelsPerPoint = parseFloat(getComputedStyle(page).fontSize) / (data.textSize || 11)
    const row = document.createElement('div')
    row.className = 'geo-print'
    for (const picture of data.pictures) {
      const cell = document.createElement('div')
      cell.className = 'geo-print-picture'
      const image = document.createElement('img')
      image.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(picture.svg)
      image.width = Math.round(picture.width * pixelsPerPoint)
      image.alt = 'Abbildung'
      cell.style.width = image.width + 'px'
      cell.appendChild(image)
      if (picture.caption) {
        const caption = document.createElement('div')
        caption.className = 'geo-print-caption'
        caption.innerHTML = picture.caption
        cell.appendChild(caption)
      }
      row.appendChild(cell)
    }
    item.appendChild(row)
  }

  // a frame with the player for every figure that has none yet (in the book: the printed pictures)
  function startFigures () {
    for (const item of page.querySelectorAll('.geo-item[data-figure]')) {
      if (item.querySelector('iframe, .geo-print')) {
        continue
      }
      const data = figures[item.dataset.figure]
      if (mode === 'book' && data && data.pictures && data.pictures.length > 0) {
        printPictures(item, data)
        continue
      }
      if (!data || !window.GEO_FIGURE_PLAYER) {
        continue
      }
      const ratio = parseFloat(getComputedStyle(item).getPropertyValue('--ratio')) || 1.6
      const steps = data.animation.steps
      // in the book cut to what the pictures show: the times of the stills (a row of stills: all of them alike)
      const endOf = index => index >= 0 && steps[index] ? steps[index].end : 0
      const crop = (from, to) => mode === 'book' && data.crop ? Array.from({ length: to - from + 1 }, (_, k) => endOf(from + k)) : undefined
      if (mode === 'web' || data.still) {
        const still = data.still ? crop(data.to, data.to) : undefined
        item.appendChild(frame(data.animation, { from: data.from, to: data.to, still: data.still, crop: still }, ratio, item.clientWidth))
        continue
      }
      // the book: the stills of the steps side by side (at most three in a row), each with its number and description;
      // a whole animation: its last picture
      const count = steps.length
      const from = typeof data.from === 'number' ? data.from : count - 1
      const to = typeof data.to === 'number' ? data.to : count - 1
      if (from === to) {
        item.appendChild(frame(data.animation, { from: to, to, still: true, crop: crop(to, to) }, ratio, item.clientWidth))
        continue
      }
      const row = document.createElement('div')
      row.className = 'geo-steps'
      const columns = Math.min(to - from + 1, to - from + 1 === 4 ? 2 : 3)
      row.style.setProperty('--columns', String(columns))
      item.appendChild(row)
      const cell = (item.clientWidth - 10 * (columns - 1)) / columns
      for (let step = from; step <= to; step++) {
        row.appendChild(frame(data.animation, { from: step, to: step, still: true, caption: true, crop: crop(from, to) }, ratio, cell))
      }
    }
  }

  function frame (animation, part, ratio, width) {
    const element = document.createElement('iframe')
    const id = 'figure' + nextFrame++
    element.dataset.id = id
    element.title = 'Abbildung'
    // the width it has before it is cut to its content (a row of stills: the width of a column)
    element.baseWidth = width
    if (part.caption) {
      element.style.width = Math.floor(width) + 'px'
    }
    if (part.still && !part.caption) {
      element.style.aspectRatio = String(ratio)
    } else {
      // until the player tells its height: picture, description (and controls)
      element.style.height = Math.round(width / ratio + (part.caption ? 40 : 110)) + 'px'
    }
    const script = 'window.GEO_ANIMATION = ' + JSON.stringify(animation) + ';\n' +
      'window.GEO_FIGURE = ' + JSON.stringify(Object.assign({ id }, part)) + ';'
    // the JSON in a script element: "<" written as \u003c, so that it cannot end the element
    element.srcdoc = window.GEO_FIGURE_PLAYER.replace('/*GEO_FIGURE*/', () => script.replace(/[<]/g, '\\u003c'))
    return element
  }

  // the switch between book and web: all figures anew
  const modeButton = document.getElementById('mode')
  function showMode () {
    modeButton.textContent = mode === 'book' ? 'Buch' : 'Web'
    modeButton.title = mode === 'book'
      ? 'Abbildungen wie im Druck (Größe und Schrift im Verhältnis zum Text wie im Buch); klicken: wie im Web (Player)'
      : 'Abbildungen wie im Web (Player); klicken: wie im Druck'
  }
  showMode()
  modeButton.addEventListener('click', () => {
    mode = mode === 'book' ? 'web' : 'book'
    try {
      if (host) {
        host.setState({ mode })
      }
    } catch (ignored) {
    }
    showMode()
    for (const old of page.querySelectorAll('.geo-item[data-figure] > *')) {
      old.remove()
    }
    startFigures()
  })

  const frameOf = id => page.querySelector(`iframe[data-id="${id}"]`)

  window.addEventListener('message', event => {
    const message = event.data
    if (!message) {
      return
    }
    if (message.type === 'geoanim-figure-crop') {
      // cut to its content: as much narrower as its picture, in its new shape
      const frame = frameOf(message.id)
      if (frame && frame.baseWidth) {
        frame.style.width = Math.max(40, Math.round(frame.baseWidth * message.fraction)) + 'px'
        if (frame.style.aspectRatio) {
          frame.style.aspectRatio = String(message.ratio)
        }
      }
    } else if (message.type === 'geoanim-figure-height') {
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
    } else if (message.type === 'select') {
      markSelection({ line: message.line, text: message.text, occurrence: message.occurrence }, true)
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
