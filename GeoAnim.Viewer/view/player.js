// The GeoAnim player: draws an animation (the JSON of GeoAnim.Core's AnimationData) with SVG and plays it.
// The animation comes from window.GEO_ANIMATION (an exported file), in "geoanim serve" from animation.json, which is
// fetched again and again so that a changed script shows at once, or, in an editor's preview (window.GEO_EMBEDDED), as
// messages: { type: 'animation', animation, keepPosition, step }, { type: 'error', error }, { type: 'step', index }.
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
  // in the VS Code preview: every object over time ("inspect", also the hidden ones) by name, and the names whose
  // ghosts are shown (the object the cursor or the mouse is on in the editor)
  let inspected = {}
  let ghostLayer = null
  let highlighted = []
  let time = 0
  let playing = false
  let stopTime = null
  let speed = 1
  // off: the animation runs through; on (S): it stops at the end of every step
  // stop after each step: on by default (S or the button switches it off)
  let stopAtSteps = true
  let previousTimestamp = null
  // the mapping from mathematical coordinates to the pixels of the picture
  let scale = 1
  let unit = 1
  let liveVersion = null
  // the editor around an embedded preview (VS Code's webview API), if any
  let host = null

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
    // a group: the line and its arrowheads ("arrow": "end", "start" or "both")
    line (element, v) {
      if (!element.line) {
        element.line = create('line', { fill: 'none', 'stroke-linecap': 'round' }, element)
      }
      let x1 = toX(v.x1)
      let y1 = toY(v.y1)
      let x2 = toX(v.x2)
      let y2 = toY(v.y2)
      const length = Math.hypot(x2 - x1, y2 - y1)
      // a line of length 0 would show as a dot (round ends)
      element.style.visibility = length < 1e-6 ? 'hidden' : ''
      const atEnd = v.arrow === 'end' || v.arrow === 'both'
      const atStart = v.arrow === 'start' || v.arrow === 'both'
      // the head grows with the line width; on a short line (while it is drawn) it shrinks with it
      const head = Math.min((4 * v.width + 6) * unit, length / (atEnd && atStart ? 2 : 1))
      const dx = length > 0 ? (x2 - x1) / length : 0
      const dy = length > 0 ? (y2 - y1) / length : 0
      // the line ends inside the head, so that its round end does not stick out of the tip
      arrowHead(element, 'endHead', atEnd, x2, y2, dx, dy, head, v.color)
      arrowHead(element, 'startHead', atStart, x1, y1, -dx, -dy, head, v.color)
      if (atEnd) {
        x2 -= dx * head * 0.8
        y2 -= dy * head * 0.8
      }
      if (atStart) {
        x1 += dx * head * 0.8
        y1 += dy * head * 0.8
      }
      const line = element.line
      line.setAttribute('x1', x1)
      line.setAttribute('y1', y1)
      line.setAttribute('x2', x2)
      line.setAttribute('y2', y2)
      line.setAttribute('stroke', v.color)
      line.setAttribute('stroke-width', v.width * unit)
    },
    arc (element, v) {
      element.setAttribute('d', arcPath(v.cx, v.cy, v.r, v.start, v.sweep))
      element.setAttribute('stroke', v.color)
      element.setAttribute('stroke-width', v.width * unit)
    },
    // an arc or the border of a sector ("sector": with the two radii), with arrowheads and a label (an arc's outside its
    // middle, a sector's inside on its bisector)
    part (element, v) {
      if (!element.parts) {
        element.parts = {
          line: create('path', { fill: 'none', 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, element),
          label: create('g', {}, element)
        }
      }
      const parts = element.parts
      const cx = toX(v.cx)
      const cy = toY(v.cy)
      const r = v.r * scale
      const sweep = Math.max(0, Math.min(v.sweep, 360))
      const atEnd = (v.arrow === 'end' || v.arrow === 'both') && sweep > 0.01
      const atStart = (v.arrow === 'start' || v.arrow === 'both') && sweep > 0.01
      // the heads grow with the line width; on a short arc they shrink with it; the arc ends inside them
      const head = Math.min((4 * v.width + 6) * unit, r * sweep * Math.PI / 180 / (atEnd && atStart ? 2 : 1))
      const cut = r > 0 ? head * 0.8 / r * 180 / Math.PI : 0
      const from = v.start + (atStart ? cut : 0)
      const to = v.start + sweep - (atEnd ? cut : 0)
      const point = degrees => [cx + r * Math.cos(degrees * Math.PI / 180), cy - r * Math.sin(degrees * Math.PI / 180)]
      let d = ''
      if (r > 0 && sweep > 0.01) {
        const arc = sweep >= 359.99 && !v.sector ? arcPath(v.cx, v.cy, v.r, 0, 360) : arcPath(v.cx, v.cy, v.r, from, Math.max(0, to - from))
        d = v.sector ? `M ${cx} ${cy} L ${arc.slice(1)} Z` : arc
      }
      parts.line.setAttribute('d', d)
      parts.line.setAttribute('stroke', v.color)
      parts.line.setAttribute('stroke-width', v.width * unit)
      // the tangent counterclockwise on the screen: (−sin, −cos)
      const endAngle = (v.start + sweep) * Math.PI / 180
      const startAngle = v.start * Math.PI / 180
      const [ex, ey] = point(v.start + sweep)
      const [sx, sy] = point(v.start)
      arrowHead(element, 'endHead', atEnd, ex, ey, -Math.sin(endAngle), -Math.cos(endAngle), head, v.color)
      arrowHead(element, 'startHead', atStart, sx, sy, Math.sin(startAngle), Math.cos(startAngle), head, v.color)
      if (v.text && sweep > 0.01) {
        const size = textSize(v.size)
        const text = withNumbers(v.text)
        const key = text + '\n' + size
        if (parts.label.layoutKey !== key) {
          const layout = layoutText(parts.label, text, size, 'middle')
          parts.label.layoutWidth = layout.width
          parts.label.layoutLines = layout.lines
          parts.label.layoutKey = key
        }
        const width = parts.label.layoutWidth
        const height = 0.7 * size + (parts.label.layoutLines - 1) * LINE_HEIGHT * size
        const bisector = (v.start + sweep / 2) * Math.PI / 180
        const bx = Math.cos(bisector)
        const by = Math.sin(bisector)
        // how far the label's box reaches along the bisector
        const along = Math.abs(bx) * width / 2 + Math.abs(by) * height / 2
        const distance = v.sector ? Math.max(0.3 * r, Math.min(0.6 * r, r - along - 6 * unit)) : r + 8 * unit + along
        const x = cx + bx * distance + (v.sx || 0) * scale - width / 2
        const y = cy - by * distance - (v.sy || 0) * scale - height / 2 + 0.7 * size
        parts.label.setAttribute('transform', `translate(${x} ${y})`)
        parts.label.setAttribute('fill', v.color)
        parts.label.style.color = v.color
        parts.label.setAttribute('opacity', v.labelOpacity === undefined ? 1 : v.labelOpacity)
        parts.label.style.display = ''
        applyHalo(parts.label, v.halo ? animation.background : null, size)
      } else {
        parts.label.style.display = 'none'
      }
    },
    // a filled circle: a sector from the centre while it is drawn, then the whole disc
    disc (element, v) {
      const cx = toX(v.cx)
      const cy = toY(v.cy)
      const r = v.r * scale
      let d = ''
      if (r > 0 && v.sweep >= 359.99) {
        d = `M ${cx + r} ${cy} A ${r} ${r} 0 1 0 ${cx - r} ${cy} A ${r} ${r} 0 1 0 ${cx + r} ${cy} Z`
      } else if (r > 0 && v.sweep > 0.01) {
        const arc = arcPath(v.cx, v.cy, v.r, v.start, v.sweep)
        d = `M ${cx} ${cy} L ${arc.slice(1)} Z`
      }
      element.setAttribute('d', d)
      element.setAttribute('stroke', 'none')
      element.setAttribute('fill', v.fill)
      element.setAttribute('fill-opacity', v.fillOpacity)
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
    angle (element, v) {
      drawAngle(element, v)
    },
    // a group with the pieces of the text side by side: text elements and formulas (nested svg elements)
    text (element, v) {
      const size = textSize(v.size)
      // along a direction: a line's label (moved out along its normal) or a point's with "labeldir" (dir 1)
      const along = v.nx !== undefined && v.dir !== 0
      const align = along ? 'middle' : v.anchor || 'start'
      const text = withNumbers(v.text)
      const key = text + '\n' + size + '\n' + align
      if (element.layoutKey !== key) {
        const layout = layoutText(element, text, size, align)
        element.layoutWidth = layout.width
        element.layoutLines = layout.lines
        element.layoutTop = layout.top
        element.layoutBottom = layout.bottom
        element.layoutKey = key
      }
      let x
      let y
      if (v.below !== undefined) {
        // below another text: under its bottom with the space between the lines of a text (plus the gap), at the same
        // place across ("shift" from there)
        const above = boxOf(itemsById[v.below])
        x = above.x + (v.sx || 0) * scale
        y = above.bottom + LINE_GAP * size + (v.belowGap || 0) * scale - element.layoutTop - (v.sy || 0) * scale
      } else {
        // "shift": from the default place, in units of the coordinates
        x = toX(v.x) + (v.dx || 0) * unit + (v.sx || 0) * scale
        y = toY(v.y) - (v.dy || 0) * unit - (v.sy || 0) * scale
      }
      // where it stands (before the alignment) and how far down it reaches: for a text below this one
      element.box = { x, bottom: y + element.layoutBottom }
      if (along) {
        // the label of a line or a point: its box moved along the normal (nx, ny) until it is gap points away from the line
        const width = element.layoutWidth
        const height = 0.7 * size + (element.layoutLines - 1) * LINE_HEIGHT * size
        const reach = Math.abs(v.nx) * width / 2 + Math.abs(v.ny) * height / 2 + v.gap * unit
        x += v.nx * reach - width / 2
        y += -v.ny * reach - height / 2 + 0.7 * size
      } else if (v.anchor === 'middle') {
        x -= element.layoutWidth / 2
      } else if (v.anchor === 'end') {
        x -= element.layoutWidth
      }
      element.setAttribute('transform', `translate(${x} ${y})`)
      element.setAttribute('fill', v.color)
      element.style.color = v.color
      applyHalo(element, v.halo ? animation.background : null, size)
    }
  }

  // "halo": a copy of the laid-out text behind it with a rim in the colour of what lies below (lines stop short of the
  // letters); made again when the layout changes
  function applyHalo (group, color, size) {
    if (!color) {
      if (group.halo) {
        group.halo.remove()
        group.halo = null
      }
      return
    }
    if (!group.halo || !group.halo.parentNode || group.haloKey !== group.layoutKey) {
      if (group.halo) {
        group.halo.remove()
      }
      const halo = document.createElementNS(SVG, 'g')
      halo.setAttribute('class', 'halo')
      for (const child of [...group.childNodes]) {
        halo.appendChild(child.cloneNode(true))
      }
      group.insertBefore(halo, group.firstChild)
      group.halo = halo
      group.haloKey = group.layoutKey
    }
    group.halo.style.setProperty('--halo', color)
    group.halo.style.setProperty('--halo-width', (0.22 * size) + 'px')
  }

  // a colour as [r, g, b] ("#rrggbb" or "rgb(r,g,b)", as tracks give it)
  function rgbOf (color) {
    if (typeof color === 'string' && color.startsWith('rgb')) {
      return color.slice(color.indexOf('(') + 1, color.indexOf(')')).split(',').map(Number)
    }
    return parseColor(color)
  }

  // the colour of a fill with the opacity over the background
  function fillOver (fill, opacity) {
    const [r, g, b] = rgbOf(animation.background)
    const [fr, fg, fb] = rgbOf(fill)
    const mix = (x, y) => Math.round(x + (y - x) * opacity)
    return `rgb(${mix(r, fr)},${mix(g, fg)},${mix(b, fb)})`
  }

  // the place of a text (where it stands across, its bottom) for a text below it: as drawn last; a text not drawn yet
  // is laid out once for it (hidden texts cannot be measured: then estimated from its lines)
  function boxOf (item) {
    if (!item) {
      return { x: 0, bottom: 0 }
    }
    if (item.element.box) {
      return item.element.box
    }
    const values = {}
    for (const name in item.primitive.tracks) {
      values[name] = valueAt(item.primitive.tracks[name], Math.max(time, item.primitive.tracks[name][0][0]))
    }
    const size = textSize(values.size)
    const lines = (values.text || '').split('\n').length
    const bottom = 0.3 * size + (lines - 1) * LINE_HEIGHT * size
    if (values.below !== undefined) {
      const above = boxOf(itemsById[values.below])
      const top = above.bottom + LINE_GAP * size + (values.belowGap || 0) * scale + 0.75 * size - (values.sy || 0) * scale
      return { x: above.x + (values.sx || 0) * scale, bottom: top + bottom }
    }
    const y = toY(values.y) - (values.dy || 0) * unit - (values.sy || 0) * scale
    return { x: toX(values.x) + (values.dx || 0) * unit + (values.sx || 0) * scale, bottom: y + bottom }
  }

  // texts in the picture grow with it, but stay readable in a small one: the smallest one never below MIN_TEXT_SIZE
  // pixels, a larger one never below a floor that grows with its size (more gently than the size itself), so that a
  // larger text always stays the larger one without crowding the small picture. Texts given smaller than
  // MIN_TEXT_SIZE keep their own size as the floor.
  const MIN_TEXT_SIZE = 12
  let smallestText = MIN_TEXT_SIZE
  const textSize = points => Math.max(points * unit,
    points < MIN_TEXT_SIZE ? points : MIN_TEXT_SIZE * Math.sqrt(points / smallestText))

  // the smallest size of a text or label in the picture (at least MIN_TEXT_SIZE)
  function findSmallestText () {
    let smallest = Infinity
    for (const primitive of animation.primitives) {
      if ((primitive.type === 'text' || primitive.type === 'angle') && primitive.tracks.size) {
        for (const key of primitive.tracks.size) {
          if (key[1] >= MIN_TEXT_SIZE) {
            smallest = Math.min(smallest, key[1])
          }
        }
      }
    }
    smallestText = Number.isFinite(smallest) ? smallest : MIN_TEXT_SIZE
  }

  // an arrowhead with its tip at (x, y), pointing in the direction (dx, dy); hidden when not wanted
  function arrowHead (element, name, wanted, x, y, dx, dy, length, color) {
    if (!wanted) {
      if (element[name]) {
        element[name].setAttribute('points', '')
      }
      return
    }
    if (!element[name]) {
      element[name] = create('polygon', { stroke: 'none' }, element)
    }
    const half = 0.42 * length
    const bx = x - dx * length
    const by = y - dy * length
    element[name].setAttribute('points', `${x},${y} ${bx - dy * half},${by + dx * half} ${bx + dy * half},${by - dx * half}`)
    element[name].setAttribute('fill', color)
  }

  const TAGS = { dot: 'circle', line: 'g', arc: 'path', disc: 'path', part: 'g', polygon: 'polygon', text: 'g', angle: 'g' }

  // ------------------------------------------------------------------ angles

  // a point at angle a (degrees, counterclockwise) and distance r (pixels) from (cx, cy)
  const polar = (cx, cy, a, r) => [cx + r * Math.cos(a * Math.PI / 180), cy - r * Math.sin(a * Math.PI / 180)]

  // an angle: a filled sector and its arc, the label on the bisector, a dot for a right angle (as in German schools).
  // Without a radius the sector is sized so that the label fits between the arms (at least "base" points).
  function drawAngle (element, v) {
    if (!element.parts) {
      element.parts = {
        fill: create('path', { stroke: 'none' }, element),
        arc: create('path', { fill: 'none', 'stroke-linecap': 'round' }, element),
        dot: create('circle', {}, element),
        label: create('g', {}, element)
      }
    }
    const parts = element.parts
    const reveal = v.reveal === undefined ? 1 : Math.max(0, Math.min(1, v.reveal))
    const cx = toX(v.x)
    const cy = toY(v.y)
    const size = textSize(v.size)
    let width = 0
    let height = 0
    if (v.text) {
      const text = withNumbers(v.text)
      const key = text + '\n' + size
      if (parts.label.layoutKey !== key) {
        const layout = layoutText(parts.label, text, size, 'middle')
        parts.label.layoutWidth = layout.width
        parts.label.layoutLines = layout.lines
        parts.label.layoutKey = key
      }
      width = parts.label.layoutWidth
      height = 0.7 * size + (parts.label.layoutLines - 1) * LINE_HEIGHT * size
    } else if (parts.label.firstChild) {
      parts.label.textContent = ''
      parts.label.layoutKey = null
    }
    // the label's box: its extent along the bisector and across it
    const bisector = (v.start + v.sweep / 2) * Math.PI / 180
    const bx = Math.cos(bisector)
    const by = Math.sin(bisector)
    const along = Math.abs(bx) * width / 2 + Math.abs(by) * height / 2
    const across = Math.abs(by) * width / 2 + Math.abs(bx) * height / 2
    const pad = 6 * unit
    const half = Math.min(v.sweep, 359.9) / 2 * Math.PI / 180
    // far enough out on the bisector that the box keeps off both arms, and off the vertex's own label
    let distance = v.text ? (half < Math.PI / 2 ? Math.max((across + pad) / Math.sin(half), along + pad) : along + pad) : 0
    if (v.text) {
      distance = Math.max(distance, 1.25 * size + along)
    }
    distance = Math.min(distance, 8 * v.base * unit)
    let radius
    if (v.radius > 0) {
      radius = v.radius * scale
      distance = Math.max(radius * 0.55, Math.min(distance, radius - along - pad))
    } else {
      radius = Math.max(v.base * unit, v.text ? distance + along + pad : 0)
    }
    const sweep = v.sweep * reveal
    if (sweep < 0.01) {
      parts.fill.setAttribute('d', '')
      parts.arc.setAttribute('d', '')
    } else if (sweep >= 359.99) {
      const circle = `M ${cx + radius} ${cy} A ${radius} ${radius} 0 1 0 ${cx - radius} ${cy} A ${radius} ${radius} 0 1 0 ${cx + radius} ${cy}`
      parts.fill.setAttribute('d', circle)
      parts.arc.setAttribute('d', circle)
    } else {
      const [x0, y0] = polar(cx, cy, v.start, radius)
      const [x1, y1] = polar(cx, cy, v.start + sweep, radius)
      const arc = `A ${radius} ${radius} 0 ${sweep > 180 ? 1 : 0} 0 ${x1} ${y1}`
      parts.fill.setAttribute('d', `M ${cx} ${cy} L ${x0} ${y0} ${arc} Z`)
      parts.arc.setAttribute('d', `M ${x0} ${y0} ${arc}`)
    }
    // arrowheads on the arc: at its end (counterclockwise), at its start (clockwise) or both; the arc ends inside them
    const atEnd = (v.arrow === 'end' || v.arrow === 'both') && sweep >= 0.01
    const atStart = (v.arrow === 'start' || v.arrow === 'both') && sweep >= 0.01
    const head = Math.min((4 * v.width + 6) * unit, radius * Math.min(sweep, 359.99) * Math.PI / 180 / (atEnd && atStart ? 2 : 1))
    const cut = radius > 0 ? head / radius * 180 / Math.PI : 0
    const arcHead = (name, wanted, tipAngle, fromAngle) => {
      const [tx, ty] = polar(cx, cy, tipAngle, radius)
      const [fx, fy] = polar(cx, cy, fromAngle, radius)
      const length = Math.hypot(tx - fx, ty - fy) || 1
      arrowHead(element, name, wanted, tx, ty, (tx - fx) / length, (ty - fy) / length, head, v.color)
    }
    arcHead('endHead', atEnd, v.start + sweep, v.start + sweep - cut)
    arcHead('startHead', atStart, v.start, v.start + cut)
    if ((atEnd || atStart) && sweep < 359.99) {
      const a0 = v.start + (atStart ? 0.8 * cut : 0)
      const a1 = v.start + sweep - (atEnd ? 0.8 * cut : 0)
      const [x0, y0] = polar(cx, cy, a0, radius)
      const [x1, y1] = polar(cx, cy, a1, radius)
      parts.arc.setAttribute('d', a1 > a0 ? `M ${x0} ${y0} A ${radius} ${radius} 0 ${a1 - a0 > 180 ? 1 : 0} 0 ${x1} ${y1}` : '')
    }
    parts.fill.setAttribute('fill', v.fill)
    parts.fill.setAttribute('fill-opacity', v.fillOpacity)
    parts.arc.setAttribute('stroke', v.color)
    parts.arc.setAttribute('stroke-width', v.width * unit)
    // a right angle: the arc with a dot – unless it has a label, which stands there instead (while the label is hidden
    // with "hide label", the dot shows; the two fade into each other)
    const right = Math.abs(v.sweep - 90) < 0.05
    const labelShown = v.text ? (v.labelOpacity === undefined ? 1 : v.labelOpacity) : 0
    const [dx, dy] = polar(cx, cy, v.start + v.sweep / 2, radius * 0.45)
    parts.dot.setAttribute('cx', dx)
    parts.dot.setAttribute('cy', dy)
    parts.dot.setAttribute('r', right && labelShown < 1 ? Math.max(1.5, 1.4 * v.width) * unit : 0)
    parts.dot.setAttribute('fill', v.color)
    parts.dot.setAttribute('opacity', reveal * (1 - labelShown))
    if (v.text) {
      // "offset x y": moved from its place on the bisector, in units of the coordinates
      const x = cx + bx * distance - width / 2 + (v.ox || 0) * scale
      const y = cy - by * distance - height / 2 + 0.7 * size - (v.oy || 0) * scale
      parts.label.setAttribute('transform', `translate(${x} ${y})`)
      parts.label.setAttribute('fill', v.color)
      parts.label.style.color = v.color
      parts.label.setAttribute('opacity', reveal * (v.labelOpacity === undefined ? 1 : v.labelOpacity))
      applyHalo(parts.label, v.halo ? fillOver(v.fill, v.fillOpacity) : null, size)
    }
  }

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

  // distance of the lines of a text in the picture, in font sizes
  const LINE_HEIGHT = 1.3
  // the space between two lines of letters (a line reaches 0.75 above its baseline and 0.3 below)
  const LINE_GAP = LINE_HEIGHT - 1.05

  // lays out a text in the picture: its lines (a new paragraph: a new line) aligned to the left, centre or right, with
  // bold, italic, code and links (underlined) from Markdown; the first baseline at y = 0. Returns width and lines.
  function layoutText (group, text, size, align) {
    group.textContent = ''
    const lines = [[]]
    flattenInline(parseMarkdown(text), {}, lines)
    let widest = 0
    const widths = []
    // how far the text reaches above its first baseline and below it (formulas can reach further than letters)
    let topOfFirst = -0.75 * size
    let bottom = 0.3 * size
    const rows = lines.map((pieces, index) => {
      const row = create('g', {}, group)
      const baseline = index * LINE_HEIGHT * size
      bottom = Math.max(bottom, baseline + 0.3 * size)
      let x = 0
      for (const piece of pieces) {
        const formula = piece.math !== undefined ? formulaOf(piece.math) : null
        if (formula) {
          const element = formulaElement(formula)
          const [, top, width, height] = formula.box
          if (index === 0) {
            topOfFirst = Math.min(topOfFirst, top / 1000 * size)
          }
          bottom = Math.max(bottom, baseline + (top + height) / 1000 * size)
          element.setAttribute('x', x)
          element.setAttribute('y', top / 1000 * size)
          element.setAttribute('width', width / 1000 * size)
          element.setAttribute('height', height / 1000 * size)
          row.appendChild(element)
          x += width / 1000 * size
        } else {
          const element = create('text', { x, y: 0, 'font-size': size, 'dominant-baseline': 'alphabetic' }, row)
          element.style.whiteSpace = 'pre'
          if (piece.bold) {
            element.setAttribute('font-weight', 'bold')
          }
          if (piece.italic) {
            element.setAttribute('font-style', 'italic')
          }
          if (piece.code) {
            element.setAttribute('font-family', "Consolas, 'Cascadia Mono', monospace")
          }
          if (piece.link) {
            element.setAttribute('text-decoration', 'underline')
          }
          element.textContent = piece.text !== undefined ? piece.text : piece.math
          x += element.getComputedTextLength()
        }
      }
      widths[index] = x
      widest = Math.max(widest, x)
      return row
    })
    rows.forEach((row, index) => {
      const shift = align === 'middle' ? (widest - widths[index]) / 2 : align === 'end' ? widest - widths[index] : 0
      row.setAttribute('transform', `translate(${shift} ${index * LINE_HEIGHT * size})`)
    })
    return { width: widest, lines: lines.length, top: topOfFirst, bottom }
  }

  // Markdown pieces → lines of pieces with their style
  function flattenInline (nodes, style, lines) {
    for (const node of nodes) {
      if (node.text !== undefined) {
        node.text.split('\n').forEach((part, index) => {
          if (index > 0) {
            lines.push([])
          }
          if (part) {
            lines[lines.length - 1].push({ text: part, ...style })
          }
        })
      } else if (node.math !== undefined) {
        lines[lines.length - 1].push({ math: node.math, ...style })
      } else {
        const inner = { ...style }
        inner[{ strong: 'bold', em: 'italic', code: 'code', link: 'link' }[node.type]] = true
        flattenInline(node.children, inner, lines)
      }
    }
  }

  // a text with formulas in an HTML element (title, step description)
  function setRichText (element, text) {
    if (element.richText === text) {
      return
    }
    element.richText = text
    element.textContent = ''
    renderInline(element, parseMarkdown(text))
  }

  // ------------------------------------------------------------------ Markdown in titles and descriptions

  // a little Markdown within a paragraph: **bold**, *italic* or _italic_, `code`, [text](address), <address>;
  // a backslash keeps a mark ("\*"). Formulas ($…$) are whole pieces, so marks inside them do not count.
  function parseMarkdown (text) {
    const items = []
    for (const run of splitMath(text)) {
      if (run.math) {
        items.push({ math: run.text })
      } else {
        items.push(...run.text)
      }
    }
    return parseInline(items, 0, null).nodes
  }

  const isSpace = c => c === undefined || c === ' '
  const isWordChar = c => typeof c === 'string' && /[\p{L}\p{N}]/u.test(c)
  const startsWith = (items, i, mark) => [...mark].every((c, k) => items[i + k] === c)

  // the pieces from position i up to the closing mark (null: it does not close)
  function parseInline (items, i, closer) {
    const nodes = []
    let text = ''
    const flush = () => {
      if (text) {
        nodes.push({ text })
        text = ''
      }
    }
    while (i < items.length) {
      const c = items[i]
      const doubleStar = c === '*' && items[i + 1] === '*'
      if (closer && startsWith(items, i, closer) && !isSpace(items[i - 1]) && !(closer === '*' && doubleStar && items[i + 2] !== undefined) &&
          !(closer === '_' && isWordChar(items[i + closer.length]))) {
        flush()
        return { nodes, end: i + closer.length }
      }
      if (typeof c !== 'string') {
        flush()
        nodes.push(c)
        i++
        continue
      }
      if (c === '\\' && typeof items[i + 1] === 'string' && '*_`[]<>{}\\'.includes(items[i + 1])) {
        text += items[i + 1]
        i += 2
        continue
      }
      // before a letter: "no keyword link here" (exportdir leaves "\\Seite" alone); the backslash is not shown
      if (c === '\\' && typeof items[i + 1] === 'string' && /\p{L}/u.test(items[i + 1])) {
        i++
        continue
      }
      let found = null
      if (doubleStar) {
        found = wrapped(items, i, '**', 'strong')
      } else if (c === '*') {
        found = wrapped(items, i, '*', 'em')
      } else if (c === '_' && !isWordChar(items[i - 1])) {
        found = wrapped(items, i, '_', 'em')
      } else if (c === '`') {
        found = code(items, i)
      } else if (c === '[') {
        found = link(items, i)
      } else if (c === '<') {
        found = autolink(items, i)
      }
      if (found) {
        flush()
        nodes.push(found.node)
        i = found.end
      } else {
        text += c
        i++
      }
    }
    if (closer) {
      return null
    }
    flush()
    return { nodes, end: i }
  }

  function wrapped (items, i, mark, type) {
    if (isSpace(items[i + mark.length])) {
      return null
    }
    const inner = parseInline(items, i + mark.length, mark)
    return inner && inner.nodes.length ? { node: { type, children: inner.nodes }, end: inner.end } : null
  }

  function code (items, i) {
    let text = ''
    for (let k = i + 1; k < items.length; k++) {
      if (items[k] === '`') {
        return text ? { node: { type: 'code', children: [{ text }] }, end: k + 1 } : null
      }
      text += typeof items[k] === 'string' ? items[k] : '$' + items[k].math + '$'
    }
    return null
  }

  // [text](address)
  function link (items, i) {
    const label = parseInline(items, i + 1, ']')
    if (!label || items[label.end] !== '(') {
      return null
    }
    let href = ''
    for (let k = label.end + 1; k < items.length; k++) {
      if (items[k] === ')') {
        return href ? { node: { type: 'link', href, children: label.nodes }, end: k + 1 } : null
      }
      if (typeof items[k] !== 'string' || items[k] === ' ') {
        return null
      }
      href += items[k]
    }
    return null
  }

  // <Prop1.html>, <https://…>
  function autolink (items, i) {
    let href = ''
    for (let k = i + 1; k < items.length; k++) {
      if (items[k] === '>') {
        return /[.:/]/.test(href) ? { node: { type: 'link', href, children: [{ text: href }] }, end: k + 1 } : null
      }
      if (typeof items[k] !== 'string' || items[k] === ' ' || items[k] === '<') {
        return null
      }
      href += items[k]
    }
    return null
  }

  const TAG = { strong: 'strong', em: 'em', code: 'code', link: 'a' }

  function renderInline (parent, nodes) {
    for (const node of nodes) {
      if (node.text !== undefined) {
        // a new paragraph ("|" alone, "\n"): a new line
        node.text.split('\n').forEach((part, index) => {
          if (index > 0) {
            parent.appendChild(document.createElement('br'))
          }
          parent.appendChild(document.createTextNode(part))
        })
      } else if (node.math !== undefined) {
        const formula = formulaOf(node.math)
        if (!formula) {
          parent.appendChild(document.createTextNode(node.math))
          continue
        }
        const [, top, width, height] = formula.box
        const svg = formulaElement(formula)
        svg.setAttribute('width', (width / 1000) + 'em')
        svg.setAttribute('height', (height / 1000) + 'em')
        svg.style.verticalAlign = (-(top + height) / 1000) + 'em'
        parent.appendChild(svg)
      } else {
        const element = document.createElement(TAG[node.type])
        if (node.type === 'link') {
          prepareLink(element, node.href)
        }
        renderInline(element, node.children)
        parent.appendChild(element)
      }
    }
  }

  // exported: other animations open in the same window, other sites in a new one; "geoanim serve": a new window;
  // an editor's preview: the editor opens the script (Prop1.html → Prop1.geo) or the site
  function prepareLink (element, href) {
    element.href = href
    const external = /^[a-z][a-z0-9+.-]*:/i.test(href)
    if (window.GEO_EMBEDDED) {
      element.addEventListener('click', event => {
        event.preventDefault()
        if (host) {
          host.postMessage({ type: 'open', href })
        }
      })
    } else if (external || !window.GEO_ANIMATION) {
      element.target = '_blank'
      element.rel = 'noopener'
    }
  }

  const plainOf = nodes => nodes.map(n => n.text !== undefined ? n.text.replace(/\n/g, ' ') : n.math !== undefined ? n.math : plainOf(n.children)).join('')
  const plainText = text => plainOf(parseMarkdown(text))

  // ------------------------------------------------------------------ numbers in texts

  // "{l}" or "{l:2}" (decimal places): the value of the number l at the current time, with a decimal comma
  const PLACEHOLDER = /(?<!\\)\{([A-Za-z_][A-Za-z0-9_]*'*)(?::(\d))?\}/g
  let numbers = []
  let numberValues = {}
  // the items by the id of their primitive (a text below another one finds it)
  let itemsById = {}

  function updateNumbers () {
    numberValues = {}
    for (const primitive of numbers) {
      const tracks = primitive.tracks
      const name = valueAt(tracks.name, Infinity)
      const valid = valueAt(tracks.valid, time)
      const value = valueAt(tracks.value, time)
      numberValues[name] = { value: valid > 0.5 && value !== undefined ? value : undefined, digits: valueAt(tracks.digits, Infinity) }
    }
  }

  function withNumbers (text) {
    if (!text || text.indexOf('{') < 0) {
      return text
    }
    return text.replace(PLACEHOLDER, (match, name, digits) => {
      const number = numberValues[name]
      if (!number || number.value === undefined) {
        return '–'
      }
      const places = digits !== undefined ? Number(digits) : number.digits
      let shown = number.value.toFixed(places)
      if (/^-?[0.]+$/.test(shown)) {
        shown = shown.replace('-', '')
      }
      return shown.replace('.', ',').replace('-', '−')
    })
  }

  function buildScene () {
    scene.textContent = ''
    create('rect', { id: 'background', x: 0, y: 0, width: '100%', height: '100%', fill: animation.background }, scene)
    decor = create('g', { id: 'decor' }, scene)
    // numbers are not drawn: the placeholders in the texts show them
    numbers = animation.primitives.filter(primitive => primitive.type === 'number')
    const sorted = animation.primitives.filter(primitive => primitive.type !== 'number').sort((a, b) => a.layer - b.layer || a.id - b.id)
    items = sorted.map(primitive => {
      const element = create(TAGS[primitive.type], { 'data-id': primitive.id }, scene)
      if (primitive.type === 'arc') {
        element.setAttribute('fill', 'none')
        element.setAttribute('stroke-linecap', 'round')
      }
      if (primitive.type === 'polygon') {
        element.setAttribute('stroke-linejoin', 'round')
      }
      return { primitive, element, shown: true }
    })
    itemsById = {}
    for (const item of items) {
      itemsById[item.primitive.id] = item
    }
    findSmallestText()
    // the ghosts above everything; drawn only while highlighted
    ghostLayer = create('g', { id: 'ghosts', 'pointer-events': 'none' }, scene)
    inspected = {}
    for (const entry of animation.inspect || []) {
      for (const name of entry.names) {
        inspected[name] = entry
      }
    }
  }

  // ------------------------------------------------------------------ ghosts (VS Code preview)

  const GHOST_COLOR = '#ff6d00'

  // the highlighted objects as dashed orange outlines at the current time, whether they are shown or not
  function renderGhosts () {
    if (!ghostLayer) {
      return
    }
    const wanted = new Set(highlighted.map(name => inspected[name]).filter(entry => entry && entry.type !== 'info'))
    for (const entry of Object.values(inspected)) {
      if (!wanted.has(entry)) {
        for (const element of [entry.ghost, entry.labelGhost]) {
          if (element) {
            element.style.display = 'none'
          }
        }
      }
    }
    // the values of a ghost's tracks now, or null where it does not exist (yet)
    const valuesOf = tracks => {
      const values = {}
      for (const name in tracks) {
        if (name === 'info') {
          continue
        }
        const value = valueAt(tracks[name], time)
        if (value === undefined) {
          return null
        }
        values[name] = value
      }
      return values.opacity > 0.5 ? values : null
    }
    for (const entry of wanted) {
      const values = valuesOf(entry.tracks)
      if (!entry.ghost) {
        entry.ghost = create(TAGS[entry.type], { fill: 'none', 'stroke-linecap': 'round' }, ghostLayer)
      }
      const ghost = entry.ghost
      ghost.style.display = values ? '' : 'none'
      if (values) {
        Object.assign(values, { color: GHOST_COLOR, width: 2.5, fill: GHOST_COLOR, fillOpacity: 0.12, reveal: 1, labelOpacity: 1 })
        values.base = 36
        values.size = values.size || 24
        if (entry.type === 'arc' && values.sweep === undefined) {
          Object.assign(values, { start: 0, sweep: 360 })
        } else if (entry.type === 'dot') {
          values.r = 4
        }
        RENDERERS[entry.type](ghost, values)
        ghost.setAttribute('stroke-dasharray', `${7 * unit} ${5 * unit}`)
        if (entry.type === 'dot') {
          // a ring around the point
          ghost.setAttribute('r', 7 * unit)
          ghost.setAttribute('fill', 'none')
          ghost.setAttribute('stroke', GHOST_COLOR)
          ghost.setAttribute('stroke-width', 2.5 * unit)
          ghost.setAttribute('stroke-dasharray', `${4 * unit} ${3 * unit}`)
        }
      }
      // the label where it will stand, also while it is hidden
      if (entry.label) {
        const label = valuesOf(entry.label.tracks)
        if (!entry.labelGhost) {
          entry.labelGhost = create('g', {}, ghostLayer)
        }
        entry.labelGhost.style.display = label ? '' : 'none'
        if (label) {
          label.color = GHOST_COLOR
          RENDERERS.text(entry.labelGhost, label)
        }
      }
    }
  }


  // what the editor's tooltip shows about an object now: its values (Markdown), the step and whether it exists yet
  function describe (name) {
    const entry = inspected[name]
    if (!entry) {
      return null
    }
    const info = valueAt(entry.tracks.info, time)
    return { info: info === undefined ? null : info, step: currentStepIndex() + 1, playing }
  }

  function render () {
    updateNumbers()
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
    renderGhosts()
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
  // the description of a step above the picture, with its number in front like in the list of steps (empty at the
  // starting picture)
  function setCaption (index) {
    const caption = $('caption')
    const text = index >= 0 ? withNumbers(steps()[index].caption) : ''
    const key = index + '\u0000' + text
    caption.captionIndex = index
    if (caption.richText === key) {
      return
    }
    caption.richText = key
    caption.textContent = ''
    if (index >= 0) {
      const number = document.createElement('span')
      number.className = 'step-number'
      number.textContent = String(index + 1)
      caption.appendChild(number)
    }
    renderInline(caption, parseMarkdown(text))
  }

  // the space above the picture (step description) and below it (description, info, next page), each as high as
  // its tallest content over all steps, so that the picture never moves
  function reserveCaptionSpace () {
    if (!animation) {
      return
    }
    const top = $('caption-box')
    const box = $('texts')
    const shownStep = $('caption').captionIndex ?? -1
    const shownInfo = $('info').richText || ''
    const nextShown = !$('next-page').hidden
    // measured as below a picture (a text step's text then gets the whole space, but does not count here)
    const textMode = document.body.classList.contains('textstep')
    document.body.classList.remove('textstep')
    top.style.minHeight = ''
    box.style.minHeight = ''
    let topHeight = 0
    let height = 0
    // the starting picture: the page's description
    setCaption(-1)
    showInfo(withNumbers(animation.description))
    showNextPage(-1)
    topHeight = Math.max(topHeight, top.offsetHeight)
    height = Math.max(height, box.offsetHeight)
    steps().forEach((step, index) => {
      setCaption(index)
      topHeight = Math.max(topHeight, top.offsetHeight)
      if (!step.text) {
        showInfo(withNumbers(step.info))
        showNextPage(index)
        height = Math.max(height, box.offsetHeight)
      }
    })
    setCaption(shownStep)
    showInfo(shownInfo)
    $('next-page').hidden = !nextShown
    top.style.minHeight = topHeight + 'px'
    box.style.minHeight = height + 'px'
    reservedHeights = { top: topHeight, texts: height }
    document.body.classList.toggle('textstep', textMode)
  }

  // the heights reserved above and below the picture (the tallest over the steps with a picture)
  let reservedHeights = { top: 0, texts: 0 }

  // title, description and info in points at a picture width of REFERENCE_WIDTH (like the labels in the picture),
  // never smaller than the minimum in pixels, so that they stay readable in a small window
  const TEXT_SIZES = { title: [38, 18], caption: [30, 15], info: [28, 14] }

  function setTextScale (factor) {
    const style = document.documentElement.style
    for (const name in TEXT_SIZES) {
      const [size, minimum] = TEXT_SIZES[name]
      style.setProperty('--' + name + '-size', Math.max(minimum, size * factor).toFixed(1) + 'px')
    }
  }

  // the picture gets the shape of the view, as large as the window allows
  function layout () {
    if (!animation) {
      return
    }
    const view = animation.view
    const ratio = (view.xmax - view.xmin) / (view.ymax - view.ymin)
    // the space between title and control bar, without the texts above and below the picture and the margins (16 left
    // and right, 10 in all).
    // The texts grow with the picture, so their height depends on its width and the other way round: a few rounds
    // settle it (starting with the full width)
    const content = $('content')
    // a still (contact sheet): the picture fills the frame
    const still = document.body.classList.contains('still')
    const side = still ? 0 : 32
    const top = still ? 0 : 10
    let width = Math.max(1, content.clientWidth - side)
    // the picture's size as in a step with a picture (in a text step it is hidden, but comes back as it was)
    const textMode = document.body.classList.contains('textstep')
    document.body.classList.remove('textstep')
    for (let round = 0; round < 3; round++) {
      const before = width
      setTextScale(width / REFERENCE_WIDTH)
      reserveCaptionSpace()
      // with the reserved heights, not the texts shown now (a text step's long text does not count)
      const available = Math.max(1, content.clientHeight - top - reservedHeights.top - reservedHeights.texts)
      width = Math.max(1, Math.min(content.clientWidth - side, available * ratio))
      if (Math.abs(width - before) < 2) {
        break
      }
    }
    document.body.classList.toggle('textstep', textMode)
    const height = width / ratio
    // the stage gets the picture's height, so that the texts above and below stay right at the picture
    stage.style.flex = 'none'
    stage.style.height = height + 'px'
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
      reportStep()
    } else {
      play()
    }
  }

  function nextStep () {
    pause()
    const next = steps().find(step => step.end > time + EPSILON)
    setTime(next ? next.end : animation.duration)
    reportStep()
  }

  function previousStep () {
    pause()
    const before = steps().filter(step => step.end < time - EPSILON)
    setTime(before.length ? before[before.length - 1].end : 0)
    reportStep()
  }

  // to the start or the end, by button or key
  function goTo (t) {
    pause()
    setTime(t)
    reportStep()
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
      let stopped = false
      if (stopTime !== null && t >= stopTime) {
        t = stopTime
        playing = false
        stopTime = null
        stopped = true
      }
      if (t >= animation.duration) {
        t = animation.duration
        playing = false
        stopped = true
      }
      setTime(t)
      if (stopped) {
        reportStep()
      }
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
    setCaption(index)
    // a text step: its text where the picture is (not in a still of the contact sheet)
    const textMode = index >= 0 && !!list[index].text && !document.body.classList.contains('still')
    document.body.classList.toggle('textstep', textMode)
    // an error (VS Code preview, serve) stays in sight: over the picture, in a text step above the text
    const error = $('error')
    const errorHost = textMode ? $('texts') : stage
    if (error.parentNode !== errorHost) {
      // over the picture: after it; above the text: before it
      errorHost.insertBefore(error, textMode ? errorHost.firstChild : null)
    }
    // the starting picture: what the page is about (the description from the script)
    showInfo(withNumbers(index >= 0 ? list[index].info : animation.description))
    showNextPage(index)
    $('counter').textContent = (index + 1) + ' / ' + list.length
    const timeline = $('timeline')
    if (Number(timeline.value) !== Math.round(time)) {
      timeline.value = String(Math.round(time))
    }
    $('play').classList.toggle('playing', playing)
    markMenu(index)
  }

  // the explanation of a step below its description ("info"), left-aligned
  // the explanation: paragraphs; one beginning with "## " is a heading (in a longer text, above all in a text step)
  function showInfo (text) {
    const info = $('info')
    info.hidden = !text
    if (info.richText === (text || '')) {
      return
    }
    info.richText = text || ''
    info.textContent = ''
    if (!text) {
      return
    }
    // paragraphs that follow each other on new lines; a heading is a block of its own
    let block = null
    for (const paragraph of text.split('\n')) {
      const heading = /^##\s+(.*)$/.exec(paragraph)
      if (heading) {
        const element = document.createElement('div')
        element.className = 'heading'
        renderInline(element, parseMarkdown(heading[1]))
        info.appendChild(element)
        block = null
        continue
      }
      if (block) {
        block.appendChild(document.createElement('br'))
      } else {
        block = document.createElement('div')
        info.appendChild(block)
      }
      renderInline(block, parseMarkdown(paragraph))
    }
  }

  // ------------------------------------------------------------------ the way through an exported book

  // window.GEO_BOOK (geoanim exportdir): { index, previous: { title, href } | null, next: … }
  const book = window.GEO_BOOK || null

  // below the last step: "Weiter: title →"
  function showNextPage (stepIndex) {
    const box = $('next-page')
    const show = !!(book && book.next && animation && stepIndex === steps().length - 1)
    box.hidden = !show
    if (show && box.firstChild.textContent === '') {
      box.firstChild.href = book.next.href
      box.firstChild.textContent = 'Weiter: ' + book.next.title + ' →'
    }
  }

  // in the menu above the steps: overview, previous and next page
  function buildBook () {
    const box = $('book')
    if (!book) {
      return
    }
    const entries = []
    if (book.home) {
      entries.push(['⌂', book.home.title, book.home.href])
    }
    entries.push(['↑', 'Übersicht', book.index])
    if (book.previous) {
      entries.push(['←', book.previous.title, book.previous.href])
    }
    if (book.next) {
      entries.push(['→', book.next.title, book.next.href])
    }
    for (const [arrow, title, href] of entries) {
      const link = document.createElement('a')
      link.href = href
      const mark = document.createElement('span')
      mark.className = 'arrow'
      mark.textContent = arrow
      const text = document.createElement('span')
      text.textContent = title
      link.append(mark, text)
      box.appendChild(link)
    }
    box.hidden = false
    if (book.footer) {
      const footer = $('menu-footer')
      setRichText(footer, book.footer)
      footer.hidden = false
    }
  }

  // ------------------------------------------------------------------ the list of steps

  function buildMenu () {
    const list = $('menu-list')
    list.textContent = ''
    const entries = [{ caption: 'Start', time: 0 }].concat(steps().map(step => ({ caption: step.caption, time: step.end })))
    entries.forEach((entry, index) => {
      const item = document.createElement('li')
      const number = document.createElement('span')
      number.className = 'number'
      number.textContent = index > 0 ? String(index) : ''
      const text = document.createElement('span')
      setRichText(text, entry.caption)
      item.append(number, text)
      item.addEventListener('click', event => {
        // a link in a description opens the link
        if (event.target.closest('a')) {
          return
        }
        pause()
        setTime(entry.time)
        openMenu(false)
        reportStep()
      })
      list.appendChild(item)
    })
    list.markedIndex = null
  }

  // the entry of the current step (0: the start)
  function markMenu (stepIndex) {
    const list = $('menu-list')
    const index = stepIndex + 1
    if (list.markedIndex === index) {
      return
    }
    list.markedIndex = index
    Array.from(list.children).forEach((item, i) => item.classList.toggle('current', i === index))
  }

  function openMenu (open) {
    const menu = $('menu')
    menu.hidden = !open
    $('menu-button').setAttribute('aria-expanded', String(open))
    if (open) {
      const current = $('menu-list').querySelector('.current')
      if (current) {
        current.scrollIntoView({ block: 'nearest' })
      }
    }
  }

  const toggleMenu = () => openMenu($('menu').hidden)

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
    buildMenu()
    document.querySelectorAll('#controls button, #timeline, #menu-button').forEach(control => { control.disabled = false })
    if (keepPosition && stepIndex >= 0 && stepIndex < data.steps.length) {
      time = atStepEnd ? data.steps[stepIndex].end : Math.min(time, data.duration)
    } else if (keepPosition) {
      time = Math.min(time, data.duration)
    } else {
      time = 0
    }
    if (!keepPosition) {
      pause()
      reportedStep = null
    }
    reserveCaptionSpace()
    layout()
    // a page opened at a step (page.html#3)
    if (!keepPosition && !window.GEO_EMBEDDED) {
      applyHash()
    }
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
  $('first').addEventListener('click', () => goTo(0))
  $('previous').addEventListener('click', previousStep)
  $('play').addEventListener('click', togglePlay)
  $('next').addEventListener('click', nextStep)
  $('last').addEventListener('click', () => goTo(animation.duration))
  $('replay').addEventListener('click', replayStep)
  $('timeline').addEventListener('input', event => {
    // read the position before pause() updates the slider
    const position = Number(event.target.value)
    pause()
    setTime(position)
    reportStep()
  })
  $('stops').addEventListener('click', toggleStops)
  scene.addEventListener('click', event => {
    if (!animation) {
      return
    }
    // VS Code preview: Ctrl+click on an object goes to its definition
    if ((event.ctrlKey || event.metaKey) && host) {
      const names = ownerAt(event.clientX, event.clientY)
      if (names) {
        host.postMessage({ type: 'reveal', names })
      }
      return
    }
    if (event.shiftKey && live) {
      copyCoordinates(event)
      return
    }
    togglePlay()
  })

  // ------------------------------------------------------------------ which object is that? (VS Code preview)

  // the names of the object under a place of the screen (its own parts and its label): in the area around it, so that
  // thin lines are easy to hit, the one in the highest layer (points before lines before areas), then the nearest
  function ownerAt (x, y) {
    const owners = animation && animation.owners
    if (!owners) {
      return null
    }
    let best = null
    // rings of 1 to 6 pixels around the place, eight directions each
    const offsets = [[0, 0]]
    for (let r = 1; r <= 6; r++) {
      for (let k = 0; k < 8; k++) {
        offsets.push([Math.round(r * Math.cos(k * Math.PI / 4)), Math.round(r * Math.sin(k * Math.PI / 4))])
      }
    }
    offsets.forEach(([dx, dy], distance) => {
      for (const element of document.elementsFromPoint(x + dx, y + dy)) {
        const owned = element.closest && element.closest('[data-id]')
        const id = owned && scene.contains(owned) ? owned.getAttribute('data-id') : null
        if (id && owners[id]) {
          const layer = itemsById[id] ? itemsById[id].primitive.layer : 0
          if (!best || layer > best.layer || (layer === best.layer && distance < best.distance)) {
            best = { names: owners[id], layer, distance }
          }
          break
        }
      }
    })
    return best ? best.names : null
  }

  // with Ctrl (Cmd) held: the name of the object under the mouse, next to it
  function showOwner (event) {
    let tag = $('owner')
    const names = (event.ctrlKey || event.metaKey) && host ? ownerAt(event.clientX, event.clientY) : null
    if (!names) {
      if (tag) {
        tag.hidden = true
      }
      scene.style.cursor = ''
      return
    }
    if (!tag) {
      tag = document.createElement('div')
      tag.id = 'owner'
      document.body.appendChild(tag)
    }
    tag.textContent = names.join(' = ')
    tag.style.left = (event.clientX + 12) + 'px'
    tag.style.top = (event.clientY + 14) + 'px'
    tag.hidden = false
    scene.style.cursor = 'pointer'
  }

  scene.addEventListener('mousemove', showOwner)
  scene.addEventListener('mouseleave', () => { const tag = $('owner'); if (tag) tag.hidden = true })
  window.addEventListener('keyup', event => { if (event.key === 'Control' || event.key === 'Meta') { const tag = $('owner'); if (tag) tag.hidden = true; scene.style.cursor = '' } })

  // ------------------------------------------------------------------ coordinates ("geoanim serve" only)

  // live: the player shows a script being written (not an exported file)
  const live = !window.GEO_ANIMATION

  // the mathematical coordinates of a mouse position, rounded to hundredths
  function coordinatesAt (event) {
    const box = scene.getBoundingClientRect()
    const round = v => Math.round(v * 100) / 100
    return [round(animation.view.xmin + (event.clientX - box.left) / scale), round(animation.view.ymax - (event.clientY - box.top) / scale)]
  }

  const formatCoordinate = v => v.toFixed(2).replace('-', '−')

  function copyCoordinates (event) {
    const [x, y] = coordinatesAt(event)
    // the format of the script: "2.35 -1.2"
    const text = String(x) + ' ' + String(y)
    const done = () => { $('coords').textContent = 'kopiert: ' + text }
    if (navigator.clipboard) {
      navigator.clipboard.writeText(text).then(done, () => { $('coords').textContent = text })
    } else {
      $('coords').textContent = text
    }
  }

  if (live) {
    document.body.classList.add('live')
    $('coords').hidden = false
    scene.addEventListener('mousemove', event => {
      if (animation) {
        const [x, y] = coordinatesAt(event)
        $('coords').textContent = `x = ${formatCoordinate(x)}   y = ${formatCoordinate(y)}`
      }
    })
    scene.addEventListener('mouseleave', () => { $('coords').textContent = '' })
  }

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
    if (!$('menu').contains(event.target) && !$('menu-button').contains(event.target)) {
      openMenu(false)
    }
  })
  $('menu-button').addEventListener('mousedown', event => event.preventDefault())
  $('menu-button').addEventListener('click', toggleMenu)

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
      Home: () => goTo(0),
      End: () => goTo(animation.duration),
      r: replayStep,
      R: replayStep,
      s: toggleStops,
      S: toggleStops,
      '-': () => changeSpeed(-1),
      '+': () => changeSpeed(1),
      m: toggleMenu,
      M: toggleMenu,
      Escape: () => { openSpeedMenu(false); openMenu(false) }
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

  // ------------------------------------------------------------------ in an editor's preview

  function receive (message) {
    if (!message || typeof message.type !== 'string') {
      return
    }
    if (message.type === 'animation' && message.animation) {
      setAnimation(message.animation, animation !== null && message.keepPosition !== false)
      showError(null)
      // while writing: the end of the step the cursor is in, wherever it is now
      if (typeof message.step === 'number') {
        showStepEnd(message.step)
      }
    } else if (message.type === 'error') {
      showError(message.error || null)
      if (!animation) {
        $('message').textContent = ''
      }
    } else if (message.type === 'step' && animation) {
      showStepEnd(message.index)
    } else if (message.type === 'highlight') {
      highlighted = Array.isArray(message.names) ? message.names : []
      if (animation) {
        renderGhosts()
      }
    } else if (message.type === 'inspect' && host) {
      host.postMessage({ type: 'inspected', id: message.id, result: animation ? describe(message.name) : null })
    }
  }

  // the step the cursor is in: shown at its end (before the first step: the starting picture)
  function showStepEnd (index) {
    pause()
    const list = steps()
    const i = Math.min(index, list.length - 1)
    setTime(i >= 0 ? list[i].end : 0)
    // the editor is there already
    reportedStep = currentStepIndex()
  }

  // when the viewer goes to a step himself (buttons, keys, list, slider) or the animation stops: in an editor's preview
  // the editor puts the cursor on its "step" line, on a page the step goes into the address (page.html#3), so that the
  // page opens there again after "back"; while the slider is dragged only when it rests a moment
  let reportedStep = null
  let reportTimer = null

  function reportStep () {
    clearTimeout(reportTimer)
    reportTimer = setTimeout(() => {
      if (!animation || playing) {
        return
      }
      const index = currentStepIndex()
      if (!window.GEO_EMBEDDED) {
        writeHash(index)
      } else if (host && index !== reportedStep) {
        reportedStep = index
        host.postMessage({ type: 'step', index })
      }
    }, 150)
  }

  // ------------------------------------------------------------------ the step in the address

  // "#3": the end of step 3, "#0" or nothing: the starting picture
  let writtenHash = null

  function applyHash () {
    const match = /^#(\d+)$/.exec(location.hash)
    if (!match || !animation) {
      return
    }
    pause()
    const list = steps()
    const i = Math.min(Number(match[1]), list.length) - 1
    setTime(i >= 0 ? list[i].end : 0)
    writtenHash = location.hash
  }

  // replaces the address without a new entry in the history ("back" still goes to the page before)
  function writeHash (index) {
    const hash = index >= 0 ? '#' + (index + 1) : ''
    if (hash === (location.hash === '#0' ? '' : location.hash)) {
      return
    }
    try {
      history.replaceState(history.state, '', location.pathname + location.search + hash)
    } catch (error) {
      // some browsers do not allow that for local files: the same with a jump mark
      writtenHash = hash || '#0'
      try {
        location.replace(writtenHash)
      } catch (ignored) {
      }
    }
  }

  // a link to another step of this page
  window.addEventListener('hashchange', () => {
    if (location.hash !== writtenHash) {
      applyHash()
    }
  })

  async function start () {
    // "page.html?still#3": only the picture at the end of step 3, without texts and controls (the contact sheet of
    // "geoanim exportdir -sheet")
    if (/[?&]still\b/.test(location.search)) {
      document.body.classList.add('still')
    }
    // the fonts first: the labels are measured with them (bold and italic too, which load only when used)
    try {
      await Promise.all(['400 16px', 'italic 400 16px', '700 16px'].map(face => document.fonts.load(face + ' "STIX Two Text"')))
      await document.fonts.ready
    } catch (error) {
    }
    // a font that arrives later: measure the texts again
    if (document.fonts && document.fonts.addEventListener) {
      document.fonts.addEventListener('loadingdone', () => {
        for (const item of items) {
          item.element.layoutKey = null
          if (item.element.parts) {
            item.element.parts.label.layoutKey = null
          }
        }
        if (animation) {
          render()
        }
      })
    }
    if (window.GEO_ANIMATION) {
      buildBook()
      setAnimation(window.GEO_ANIMATION, false)
    } else if (window.GEO_EMBEDDED) {
      window.addEventListener('message', event => receive(event.data))
      // VS Code: tell the extension that messages can come now
      if (typeof acquireVsCodeApi === 'function') {
        host = acquireVsCodeApi()
        host.postMessage({ type: 'ready' })
      }
    } else {
      poll()
    }
    requestAnimationFrame(tick)
  }

  start()
})()
