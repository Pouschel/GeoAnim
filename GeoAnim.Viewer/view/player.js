// The GeoAnim player: draws an animation (the JSON of GeoAnim.Core's AnimationData) with SVG and plays it.
// The animation comes from window.GEO_ANIMATION (an exported file) or, in "geoanim serve", from animation.json, which
// is fetched again and again so that a changed script shows at once.
(function () {
  'use strict'

  const SVG = 'http://www.w3.org/2000/svg'
  // sizes in the animation (line widths, font sizes) are points: pixels of a player 1280 pixels wide
  const REFERENCE_WIDTH = 1280
  const SPEEDS = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4]
  const POLL_INTERVAL = 400

  const $ = id => document.getElementById(id)
  const scene = $('scene')
  const stage = $('stage')

  let animation = null
  let items = []
  let decor = null
  let time = 0
  let playing = false
  let stopTime = null
  let speed = 1
  // off: the animation runs through; on (S): it stops at the end of every step
  let stopAtSteps = false
  let previousTimestamp = null
  // the mapping from mathematical coordinates to the pixels of the picture
  let scale = 1
  let unit = 1
  let liveVersion = null

  // ------------------------------------------------------------------ values over time

  const EASINGS = {
    l: u => u,
    i: u => u * u * u,
    o: u => 1 - Math.pow(1 - u, 3),
    io: u => u < 0.5 ? 4 * u * u * u : 1 - Math.pow(-2 * u + 2, 3) / 2,
    s: u => u < 1 ? 0 : 1
  }

  function parseColor (hex) {
    const n = parseInt(hex.slice(1), 16)
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
  }

  function lerp (a, b, u) {
    if (typeof a === 'number') {
      return a + (b - a) * u
    }
    if (Array.isArray(a)) {
      return a.length === b.length ? a.map((v, i) => v + (b[i] - v) * u) : (u < 1 ? a : b)
    }
    if (typeof a === 'string' && a[0] === '#' && b[0] === '#') {
      const ca = parseColor(a)
      const cb = parseColor(b)
      return 'rgb(' + ca.map((v, i) => Math.round(v + (cb[i] - v) * u)).join(',') + ')'
    }
    return u < 1 ? a : b
  }

  // the value of a track at time t; undefined before its first key
  function valueAt (track, t) {
    if (!track || track.length === 0 || t < track[0][0]) {
      return undefined
    }
    let low = 0
    let high = track.length - 1
    while (low < high) {
      const middle = (low + high + 1) >> 1
      if (track[middle][0] <= t) {
        low = middle
      } else {
        high = middle - 1
      }
    }
    const key = track[low]
    const next = track[low + 1]
    if (!next) {
      return key[1]
    }
    const span = next[0] - key[0]
    const easing = EASINGS[next[2] || 'l'] || EASINGS.l
    const u = span <= 0 ? 1 : (t - key[0]) / span
    return lerp(key[1], next[1], easing(u))
  }

  // ------------------------------------------------------------------ drawing

  const toX = x => (x - animation.view.xmin) * scale
  const toY = y => (animation.view.ymax - y) * scale

  function create (tag, attributes, parent) {
    const element = document.createElementNS(SVG, tag)
    for (const name in attributes) {
      element.setAttribute(name, attributes[name])
    }
    if (parent) {
      parent.appendChild(element)
    }
    return element
  }

  function arcPath (cx, cy, r, start, sweep) {
    if (!(r > 0) || !(sweep > 0.01)) {
      return ''
    }
    const X = toX(cx)
    const Y = toY(cy)
    const R = r * scale
    if (sweep >= 359.99) {
      // a full circle: two half circles
      return `M ${X + R} ${Y} A ${R} ${R} 0 1 0 ${X - R} ${Y} A ${R} ${R} 0 1 0 ${X + R} ${Y}`
    }
    const a0 = start * Math.PI / 180
    const a1 = (start + sweep) * Math.PI / 180
    const x0 = X + R * Math.cos(a0)
    const y0 = Y - R * Math.sin(a0)
    const x1 = X + R * Math.cos(a1)
    const y1 = Y - R * Math.sin(a1)
    // counterclockwise in mathematical coordinates is sweep-flag 0 on the screen
    return `M ${x0} ${y0} A ${R} ${R} 0 ${sweep > 180 ? 1 : 0} 0 ${x1} ${y1}`
  }

  const RENDERERS = {
    dot (element, v) {
      element.setAttribute('cx', toX(v.x))
      element.setAttribute('cy', toY(v.y))
      element.setAttribute('r', Math.max(0, v.r) * unit)
      element.setAttribute('fill', v.color)
    },
    line (element, v) {
      // a line of length 0 would show as a dot (round ends)
      element.style.visibility = Math.abs(v.x2 - v.x1) + Math.abs(v.y2 - v.y1) < 1e-9 ? 'hidden' : ''
      element.setAttribute('x1', toX(v.x1))
      element.setAttribute('y1', toY(v.y1))
      element.setAttribute('x2', toX(v.x2))
      element.setAttribute('y2', toY(v.y2))
      element.setAttribute('stroke', v.color)
      element.setAttribute('stroke-width', v.width * unit)
    },
    arc (element, v) {
      element.setAttribute('d', arcPath(v.cx, v.cy, v.r, v.start, v.sweep))
      element.setAttribute('stroke', v.color)
      element.setAttribute('stroke-width', v.width * unit)
    },
    polygon (element, v) {
      const points = []
      for (let i = 0; i + 1 < v.points.length; i += 2) {
        points.push(toX(v.points[i]) + ',' + toY(v.points[i + 1]))
      }
      element.setAttribute('points', points.join(' '))
      element.setAttribute('stroke', v.color)
      element.setAttribute('stroke-width', v.width * unit)
      element.setAttribute('fill', v.fill)
      element.setAttribute('fill-opacity', v.fillOpacity)
    },
    // a group with the pieces of the text side by side: text elements and formulas (nested svg elements)
    text (element, v) {
      const size = v.size * unit
      const key = v.text + '\n' + size
      if (element.layoutKey !== key) {
        element.layoutWidth = layoutText(element, v.text, size)
        element.layoutKey = key
      }
      let x = toX(v.x) + (v.dx || 0) * unit
      let y = toY(v.y) - (v.dy || 0) * unit
      if (v.nx !== undefined) {
        // the label of a line: its box moved along the normal (nx, ny) until it is gap points away from the line
        const width = element.layoutWidth
        const height = 0.7 * size
        const reach = Math.abs(v.nx) * width / 2 + Math.abs(v.ny) * height / 2 + v.gap * unit
        x += v.nx * reach - width / 2
        y += -v.ny * reach + height / 2
      } else if (v.anchor === 'middle') {
        x -= element.layoutWidth / 2
      } else if (v.anchor === 'end') {
        x -= element.layoutWidth
      }
      element.setAttribute('transform', `translate(${x} ${y})`)
      element.setAttribute('fill', v.color)
      element.style.color = v.color
    }
  }

  const TAGS = { dot: 'circle', line: 'line', arc: 'path', polygon: 'polygon', text: 'g' }

  // ------------------------------------------------------------------ texts with formulas

  // "Kreis um $A$" → [{ text: 'Kreis um ', math: false }, { text: 'A', math: true }]; "\$" is a dollar sign
  function splitMath (text) {
    const runs = []
    let current = ''
    let inFormula = false
    for (let i = 0; i < text.length; i++) {
      const c = text[i]
      if (c === '\\' && text[i + 1] === '$') {
        current += '$'
        i++
      } else if (c === '$') {
        if (current) {
          runs.push({ text: inFormula ? current.trim() : current, math: inFormula })
        }
        current = ''
        inFormula = !inFormula
      } else {
        current += c
      }
    }
    if (current) {
      runs.push({ text: current, math: inFormula })
    }
    return runs
  }

  const formulaOf = source => animation.math && animation.math[source]

  // a formula as svg element; box: viewBox in thousandths of an em, baseline at y = 0
  function formulaElement (formula) {
    const element = document.createElementNS(SVG, 'svg')
    element.setAttribute('viewBox', formula.box.join(' '))
    element.setAttribute('overflow', 'visible')
    element.innerHTML = formula.svg
    return element
  }

  // lays out a text in the picture; returns its width in pixels
  function layoutText (group, text, size) {
    group.textContent = ''
    let x = 0
    for (const run of splitMath(text)) {
      const formula = run.math ? formulaOf(run.text) : null
      if (formula) {
        const element = formulaElement(formula)
        const [, top, width, height] = formula.box
        element.setAttribute('x', x)
        element.setAttribute('y', top / 1000 * size)
        element.setAttribute('width', width / 1000 * size)
        element.setAttribute('height', height / 1000 * size)
        group.appendChild(element)
        x += width / 1000 * size
      } else {
        const element = create('text', { x, y: 0, 'font-size': size, 'dominant-baseline': 'alphabetic' }, group)
        element.style.whiteSpace = 'pre'
        element.textContent = run.text
        x += element.getComputedTextLength()
      }
    }
    return x
  }

  // a text with formulas in an HTML element (title, step description)
  function setRichText (element, text) {
    if (element.richText === text) {
      return
    }
    element.richText = text
    element.textContent = ''
    for (const run of splitMath(text)) {
      const formula = run.math ? formulaOf(run.text) : null
      if (formula) {
        const [, top, width, height] = formula.box
        const svg = formulaElement(formula)
        svg.setAttribute('width', (width / 1000) + 'em')
        svg.setAttribute('height', (height / 1000) + 'em')
        svg.style.verticalAlign = (-(top + height) / 1000) + 'em'
        element.appendChild(svg)
      } else {
        element.appendChild(document.createTextNode(run.text))
      }
    }
  }

  const plainText = text => splitMath(text).map(run => run.text).join('')

  function buildScene () {
    scene.textContent = ''
    create('rect', { id: 'background', x: 0, y: 0, width: '100%', height: '100%', fill: animation.background }, scene)
    decor = create('g', { id: 'decor' }, scene)
    const sorted = animation.primitives.slice().sort((a, b) => a.layer - b.layer || a.id - b.id)
    items = sorted.map(primitive => {
      const element = create(TAGS[primitive.type], {}, scene)
      if (primitive.type === 'line' || primitive.type === 'arc') {
        element.setAttribute('fill', 'none')
        element.setAttribute('stroke-linecap', 'round')
      }
      if (primitive.type === 'polygon') {
        element.setAttribute('stroke-linejoin', 'round')
      }
      return { primitive, element, shown: true }
    })
  }

  function render () {
    for (const item of items) {
      const tracks = item.primitive.tracks
      const values = {}
      let exists = true
      for (const name in tracks) {
        const value = valueAt(tracks[name], time)
        if (value === undefined) {
          exists = false
          break
        }
        values[name] = value
      }
      const opacity = exists ? values.opacity : 0
      if (!exists || !(opacity > 0.001)) {
        if (item.shown) {
          item.element.style.display = 'none'
          item.shown = false
        }
        continue
      }
      if (!item.shown) {
        item.element.style.display = ''
        item.shown = true
      }
      item.element.setAttribute('opacity', opacity)
      RENDERERS[item.primitive.type](item.element, values)
    }
    updateControls()
  }

  // grid and axes, drawn again when the size changes
  function drawDecor () {
    decor.textContent = ''
    const view = animation.view
    if (animation.grid) {
      const step = animation.grid
      const grid = create('g', { stroke: '#e3e6eb', 'stroke-width': Math.max(1, unit) }, decor)
      for (let x = Math.ceil(view.xmin / step) * step; x <= view.xmax + 1e-9; x += step) {
        create('line', { x1: toX(x), y1: 0, x2: toX(x), y2: toY(view.ymin) }, grid)
      }
      for (let y = Math.ceil(view.ymin / step) * step; y <= view.ymax + 1e-9; y += step) {
        create('line', { x1: 0, y1: toY(y), x2: toX(view.xmax), y2: toY(y) }, grid)
      }
    }
    if (animation.axes) {
      const color = '#7a808c'
      const axes = create('g', { stroke: color, 'stroke-width': 1.5 * unit, fill: color }, decor)
      const labels = create('g', { fill: color, 'font-size': 15 * unit }, decor)
      const arrow = 9 * unit
      // labels on every k-th unit, at least 36 points apart
      let k = 1
      while (k * scale < 36 * unit) {
        k = k === 1 ? 2 : k === 2 ? 5 : k * 2
      }
      if (view.ymin <= 0 && view.ymax >= 0) {
        const y0 = toY(0)
        const xEnd = toX(view.xmax)
        create('line', { x1: 0, y1: y0, x2: xEnd, y2: y0 }, axes)
        create('polygon', { points: `${xEnd},${y0} ${xEnd - arrow},${y0 - arrow / 2} ${xEnd - arrow},${y0 + arrow / 2}`, stroke: 'none' }, axes)
        for (let x = Math.ceil(view.xmin / k) * k; x < view.xmax; x += k) {
          // no number at 0 and none cut off at the edges
          if (Math.abs(x) < 1e-9 || toX(x) < 12 * unit || toX(x) > toX(view.xmax) - 24 * unit) {
            continue
          }
          create('line', { x1: toX(x), y1: y0 - 4 * unit, x2: toX(x), y2: y0 + 4 * unit }, axes)
          create('text', { x: toX(x), y: y0 + 20 * unit, 'text-anchor': 'middle' }, labels).textContent = formatNumber(x)
        }
      }
      if (view.xmin <= 0 && view.xmax >= 0) {
        const x0 = toX(0)
        create('line', { x1: x0, y1: toY(view.ymin), x2: x0, y2: 0 }, axes)
        create('polygon', { points: `${x0},0 ${x0 - arrow / 2},${arrow} ${x0 + arrow / 2},${arrow}`, stroke: 'none' }, axes)
        for (let y = Math.ceil(view.ymin / k) * k; y < view.ymax; y += k) {
          if (Math.abs(y) < 1e-9 || toY(y) < 24 * unit || toY(y) > toY(view.ymin) - 12 * unit) {
            continue
          }
          create('line', { x1: x0 - 4 * unit, y1: toY(y), x2: x0 + 4 * unit, y2: toY(y) }, axes)
          create('text', { x: x0 - 8 * unit, y: toY(y) + 5 * unit, 'text-anchor': 'end' }, labels).textContent = formatNumber(y)
        }
      }
    }
  }

  const formatNumber = n => String(Math.round(n * 1000) / 1000).replace('-', '−')

  // the description below the picture keeps the height of the tallest one (formulas, several lines), so that the
  // picture does not change its size while the animation plays
  function reserveCaptionSpace () {
    if (!animation) {
      return
    }
    const box = $('caption')
    const current = box.richText || ''
    box.style.minHeight = ''
    let height = box.offsetHeight
    for (const step of steps()) {
      setRichText(box, step.caption)
      height = Math.max(height, box.offsetHeight)
    }
    setRichText(box, current)
    box.style.minHeight = height + 'px'
  }

  // the picture gets the shape of the view, as large as the window allows
  function layout () {
    if (!animation) {
      return
    }
    const view = animation.view
    const ratio = (view.xmax - view.xmin) / (view.ymax - view.ymin)
    const width = Math.max(1, Math.min(stage.clientWidth, stage.clientHeight * ratio))
    const height = width / ratio
    scene.setAttribute('width', width)
    scene.setAttribute('height', height)
    scene.setAttribute('viewBox', `0 0 ${width} ${height}`)
    scale = width / (view.xmax - view.xmin)
    unit = width / REFERENCE_WIDTH
    drawDecor()
    render()
  }

  // ------------------------------------------------------------------ steps and playing

  const steps = () => animation.steps
  const EPSILON = 0.5

  // the step shown at the current time: the one running, or the one just finished
  function currentStepIndex () {
    const list = steps()
    for (let i = 0; i < list.length; i++) {
      if (time > list[i].start + EPSILON && time <= list[i].end + EPSILON) {
        return i
      }
    }
    return -1
  }

  function setTime (t) {
    time = Math.max(0, Math.min(animation.duration, t))
    render()
  }

  function pause () {
    playing = false
    stopTime = null
    updateControls()
  }

  function play () {
    if (time >= animation.duration - EPSILON) {
      time = 0
    }
    const next = steps().find(step => step.end > time + EPSILON)
    stopTime = stopAtSteps && next ? next.end : null
    playing = true
    previousTimestamp = null
    updateControls()
  }

  function togglePlay () {
    if (playing) {
      pause()
    } else {
      play()
    }
  }

  function nextStep () {
    pause()
    const next = steps().find(step => step.end > time + EPSILON)
    setTime(next ? next.end : animation.duration)
  }

  function previousStep () {
    pause()
    const before = steps().filter(step => step.end < time - EPSILON)
    setTime(before.length ? before[before.length - 1].end : 0)
  }

  function replayStep () {
    const list = steps()
    let index = currentStepIndex()
    if (index < 0) {
      index = list.findIndex(step => step.end > time + EPSILON)
    }
    if (index < 0) {
      return
    }
    setTime(list[index].start)
    playing = true
    stopTime = list[index].end
    previousTimestamp = null
    updateControls()
  }

  function tick (timestamp) {
    if (playing && animation) {
      const elapsed = previousTimestamp === null ? 0 : timestamp - previousTimestamp
      let t = time + elapsed * speed
      if (stopTime !== null && t >= stopTime) {
        t = stopTime
        playing = false
        stopTime = null
      }
      if (t >= animation.duration) {
        t = animation.duration
        playing = false
      }
      setTime(t)
    }
    previousTimestamp = timestamp
    requestAnimationFrame(tick)
  }

  function updateControls () {
    if (!animation) {
      return
    }
    const list = steps()
    const index = currentStepIndex()
    setRichText($('caption'), index >= 0 ? list[index].caption : '')
    $('counter').textContent = (index + 1) + ' / ' + list.length
    const timeline = $('timeline')
    if (Number(timeline.value) !== Math.round(time)) {
      timeline.value = String(Math.round(time))
    }
    $('play').classList.toggle('playing', playing)
  }

  function drawMarks () {
    const marks = $('marks')
    marks.textContent = ''
    if (!(animation.duration > 0)) {
      return
    }
    for (const step of steps()) {
      const mark = document.createElement('span')
      mark.style.left = (100 * step.end / animation.duration) + '%'
      marks.appendChild(mark)
    }
  }

  // ------------------------------------------------------------------ loading

  function setAnimation (data, keepPosition) {
    // after a change of the script: stay at the same step
    let stepIndex = -1
    let atStepEnd = false
    if (keepPosition && animation) {
      stepIndex = currentStepIndex()
      atStepEnd = stepIndex >= 0 && Math.abs(time - steps()[stepIndex].end) < EPSILON
    }
    animation = data
    document.title = data.title ? plainText(data.title) : 'GeoAnim'
    setRichText($('title'), data.title || '')
    $('header').hidden = !data.title
    $('message').textContent = ''
    buildScene()
    $('timeline').max = String(Math.max(1, Math.round(data.duration)))
    drawMarks()
    document.querySelectorAll('#controls button, #timeline').forEach(control => { control.disabled = false })
    if (keepPosition && stepIndex >= 0 && stepIndex < data.steps.length) {
      time = atStepEnd ? data.steps[stepIndex].end : Math.min(time, data.duration)
    } else if (keepPosition) {
      time = Math.min(time, data.duration)
    } else {
      time = 0
    }
    if (!keepPosition) {
      pause()
    }
    reserveCaptionSpace()
    layout()
  }

  function showError (error) {
    const box = $('error')
    if (error) {
      box.textContent = error
      box.hidden = false
    } else {
      box.hidden = true
    }
  }

  async function poll () {
    try {
      const response = await fetch('animation.json?since=' + (liveVersion === null ? '' : liveVersion), { cache: 'no-store' })
      const live = await response.json()
      if (live.version !== liveVersion) {
        const first = liveVersion === null
        liveVersion = live.version
        if (live.animation) {
          setAnimation(live.animation, !first)
        }
        showError(live.error ? live.error : null)
        if (!live.animation && first) {
          $('message').textContent = ''
        }
      }
    } catch (error) {
      showError('No connection to "geoanim serve".')
      liveVersion = null
    }
    setTimeout(poll, POLL_INTERVAL)
  }

  // ------------------------------------------------------------------ controls

  // buttons keep no focus, so that the keys always work
  document.querySelectorAll('#controls button').forEach(button => button.addEventListener('mousedown', event => event.preventDefault()))
  $('first').addEventListener('click', () => { pause(); setTime(0) })
  $('previous').addEventListener('click', previousStep)
  $('play').addEventListener('click', togglePlay)
  $('next').addEventListener('click', nextStep)
  $('last').addEventListener('click', () => { pause(); setTime(animation.duration) })
  $('replay').addEventListener('click', replayStep)
  $('timeline').addEventListener('input', event => {
    // read the position before pause() updates the slider
    const position = Number(event.target.value)
    pause()
    setTime(position)
  })
  $('stops').addEventListener('click', toggleStops)
  scene.addEventListener('click', () => { if (animation) togglePlay() })

  // ------------------------------------------------------------------ speed

  const formatSpeed = s => ({ 0.25: '¼', 0.5: '½', 0.75: '¾', 1.25: '1¼', 1.5: '1½' }[s] || String(s)) + '×'

  function setSpeed (s) {
    speed = s
    $('speed').textContent = formatSpeed(s)
    $('speed-menu').querySelectorAll('button').forEach(item => {
      item.setAttribute('aria-checked', String(Number(item.dataset.speed) === s))
    })
  }

  // one step slower (-1) or faster (+1)
  function changeSpeed (direction) {
    const index = SPEEDS.indexOf(speed) + direction
    setSpeed(SPEEDS[Math.max(0, Math.min(SPEEDS.length - 1, index))])
  }

  function openSpeedMenu (open) {
    $('speed-menu').hidden = !open
    $('speed').setAttribute('aria-expanded', String(open))
  }

  for (const s of SPEEDS.slice().reverse()) {
    const item = document.createElement('button')
    item.type = 'button'
    item.setAttribute('role', 'menuitemradio')
    item.dataset.speed = String(s)
    item.textContent = formatSpeed(s)
    item.addEventListener('mousedown', event => event.preventDefault())
    item.addEventListener('click', () => { setSpeed(s); openSpeedMenu(false) })
    $('speed-menu').appendChild(item)
  }
  setSpeed(1)
  $('speed').addEventListener('click', () => openSpeedMenu($('speed-menu').hidden))
  $('speed').addEventListener('wheel', event => {
    event.preventDefault()
    changeSpeed(event.deltaY < 0 ? 1 : -1)
  }, { passive: false })
  document.addEventListener('mousedown', event => {
    if (!$('speed-box').contains(event.target)) {
      openSpeedMenu(false)
    }
  })

  function toggleStops () {
    stopAtSteps = !stopAtSteps
    $('stops').setAttribute('aria-pressed', String(stopAtSteps))
    if (playing) {
      play()
    }
  }

  document.addEventListener('keydown', event => {
    if (!animation || event.ctrlKey || event.metaKey || event.altKey) {
      return
    }
    const actions = {
      ' ': togglePlay,
      ArrowRight: nextStep,
      ArrowLeft: previousStep,
      Home: () => { pause(); setTime(0) },
      End: () => { pause(); setTime(animation.duration) },
      r: replayStep,
      R: replayStep,
      s: toggleStops,
      S: toggleStops,
      '-': () => changeSpeed(-1),
      '+': () => changeSpeed(1),
      Escape: () => openSpeedMenu(false)
    }
    if (actions[event.key]) {
      event.preventDefault()
      actions[event.key]()
    }
  })

  window.addEventListener('resize', () => {
    reserveCaptionSpace()
    layout()
  })

  async function start () {
    // the fonts first: the labels are measured with them
    try {
      await document.fonts.ready
    } catch (error) {
    }
    if (window.GEO_ANIMATION) {
      setAnimation(window.GEO_ANIMATION, false)
    } else {
      poll()
    }
    requestAnimationFrame(tick)
  }

  start()
})()
