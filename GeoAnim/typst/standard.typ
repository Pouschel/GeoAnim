// The standard look of the printed book (Typst). geoanim typst writes this file anew with every export – do not change
// it here. The pages use style.typ, which imports this file; to change the look, put a style.typ of your own beside
// index.yaml that imports it and changes what it wants, e.g.
//
//   #import "standard.typ": *
//   #let book = book.with(page-width: 15.5cm, page-height: 23.5cm, margin: (inside: 1.8cm, outside: 2.2cm, top: 2.4cm, bottom: 2.2cm))
//   #let statement-label(body) = text(weight: "bold", fill: rgb("#8c1f1f"), body)
//   #let link-list = link-list.with(title: [Links ins Netz], intro: base => [Alle Seiten auch unter #link(base)[#base].])
//
// so that it keeps what later versions of GeoAnim add here. The colours go to book (a "#let accent = …" of your own
// would not reach the functions here):
//
//   #let book = book.with(colors: (accent: rgb("#8c1f1f"), fill-box: rgb("#f5f0e6")))
//
// Everything the pages use is defined here: the template (book), the title page, chapters, statements (axioms,
// definitions, theorems …), figures and display formulas. Numbers come from GeoAnim (as on the web and in the e-book);
// Typst numbers only the pages.

// ------------------------------------------------------------------ settings

#let text-font = ("STIX Two Text", "Libertinus Serif")   // STIX Two Text: as in the figures and formulas
// the colours (book's "colors" changes them): numbers of chapters, sections and links; the lines of statements and of
// definitions; the background of boxes
#let default-colors = (
  accent: rgb("#1f4e8c"),
  rule-statement: rgb("#9fb3cf"),
  rule-definition: rgb("#8fbf9a"),
  fill-box: rgb("#f2f3f5"),
)
#let geo-colors = state("geo-colors", default-colors)

// a colour of the book (inside "context")
#let color-of(name) = geo-colors.get().at(name)

// ------------------------------------------------------------------ the book

// text-size: the font size of the text – unless index.yaml says it ("print: text: 10pt"), then that one (the figures and
// formulas are made for it, and the preview shows them in proportion to it: best set it there)
// page-width, page-height, margin: the format of the book's pages; font: the font of the text; colors: colours that
// differ from default-colors, e.g. (accent: rgb("#8c1f1f"))
#let book(
  title: none,
  author: none,
  text-size: 11pt,
  language: "de",
  page-width: 17cm,
  page-height: 24cm,
  margin: (inside: 2cm, outside: 2.5cm, top: 2.6cm, bottom: 2.4cm),
  font: text-font,
  colors: (:),
  body,
) = {
  geo-colors.update(known => known + colors)
  set document(title: title, author: if author == none { () } else { author })
  set page(
    width: page-width,
    height: page-height,
    margin: margin,
    // the running head: the chapter on the left pages, the section on the right; the page number outside
    header: context {
      let here-page = here().page()
      // not on the title pages, not on a page where a chapter starts and not on the empty page before it
      let starts = query(heading.where(level: 1)).map(h => h.location().page())
      if here-page <= 2 or here-page in starts or (calc.even(here-page) and (here-page + 1) in starts) {
        return
      }
      let before = query(selector(heading).before(here())).filter(h => h.outlined)
      let chapter = before.filter(h => h.level == 1)
      let section = before.filter(h => h.level == 2)
      let number = counter(page).display()
      set text(size: 0.85em)
      if calc.even(here-page) {
        number
        h(1fr)
        if chapter.len() > 0 { chapter.last().body }
      } else {
        if section.len() > 0 { section.last().body }
        h(1fr)
        number
      }
      v(-0.4em)
      line(length: 100%, stroke: 0.4pt)
    },
    footer: context {
      let here-page = here().page()
      if query(heading.where(level: 1)).any(h => h.location().page() == here-page) {
        align(center, text(size: 0.85em, counter(page).display()))
      }
    },
  )
  set text(font: font, size: text-size, lang: language)
  set par(justify: true, leading: 0.62em, spacing: 0.95em)
  set heading(numbering: none)
  show heading: set text(weight: "bold")
  show heading.where(level: 1): it => {
    set text(size: 1.9em)
    v(3cm)
    block(it.body)
    v(1.2cm)
  }
  show heading.where(level: 2): it => {
    set text(size: 1.4em)
    block(above: 1.6em, below: 1em, it.body)
  }
  show heading.where(level: 3): it => {
    set text(size: 1.15em)
    block(above: 1.4em, below: 0.7em, it.body)
  }
  show heading.where(level: 4): it => block(above: 1.2em, below: 0.6em, it.body)
  // links work in the PDF, but print looks like text
  show link: it => it
  body
}

// the title page (the author above the title, the book's icon below it, then the description) and its back
#let title-page(title: none, author: none, description: none, icon: none) = {
  page(header: none, footer: none, {
    set par(justify: false)
    v(3.5cm)
    align(center, {
      if author != none {
        text(size: 1.3em, author)
        v(1.2cm)
      }
      text(size: 2.4em, weight: "bold", title)
      if icon != none {
        v(1.5cm)
        box(width: 3.2cm, icon)
      }
      if description != none {
        v(1.5cm)
        block(width: 80%, text(style: "italic", description))
      }
    })
  })
  page(header: none, footer: none, [])
}

// the table of contents
#let contents() = {
  outline(title: [Inhalt], depth: 3, indent: auto)
  pagebreak(weak: true)
}

// a chapter (a folder of the book): on a right page; level 2 for a folder in a chapter
#let chapter(number, title, level: 1) = {
  if level == 1 {
    pagebreak(to: "odd", weak: true)
  }
  heading(level: level, {
    if number != none {
      context text(fill: color-of("accent"), number)
      h(0.6em)
    }
    title
  })
}

// ------------------------------------------------------------------ links to the web

// the number of a link to the web, after its text: the list of links at the end of the book has its address
#let link-mark(number) = context super(text(fill: color-of("accent"), str(number)))

// the list of links at the end of the book: by the chapters of their pages, each with its number, page, text and address;
// "base": the page on the web with all of them (index.yaml "links:"), where a link that moved is put right; "title": the
// heading; "intro": the paragraph below it, a function of base (only with base)
#let link-list(
  base: none,
  chapters: (),
  title: [Links],
  intro: base => [Alle Links stehen auch zum Anklicken unter #link(base)[#base] – dort werden sie aktuell gehalten, falls
    sich eine Adresse ändert. Die Nummern sind die im Text.],
) = {
  chapter(none, title)
  if base != none {
    par(intro(base))
  }
  for c in chapters {
    heading(level: 2, outlined: false, c.title)
    for l in c.links {
      block(above: 0.7em, below: 0.7em, breakable: false, grid(columns: (2.2em, 1fr), gutter: 0pt,
        context text(fill: color-of("accent"), weight: "bold", str(l.number)),
        {
          l.text
          h(0.5em)
          text(fill: luma(40%), [S. #context numbering("1", ..counter(page).at(l.label))])
          linebreak()
          text(size: 0.8em, fill: luma(35%), link(l.url, l.shown))
        }))
    }
  }
}

// the number in front of a page's heading ("2.1")
#let section-number(number) = {
  context text(fill: color-of("accent"), number)
  h(0.5em)
}

// ------------------------------------------------------------------ statements

// "Axiom 2 (Nullabstand)." at the start of a statement
#let statement-label(body) = text(style: "normal", weight: "bold", body)

// the end of a proof
#let qed() = {
  h(1fr)
  sym.qed
}

// the warning sign of "::: merke" (the yellow triangle of ISO 7010 W001)
#let warning-sign = box(width: 1.6em, height: 1.42em, {
  place(polygon(fill: rgb("#ffd200"), stroke: (paint: rgb("#111111"), thickness: 1.2pt, join: "round"),
    (0.8em, 0.1em), (1.52em, 1.36em), (0.08em, 1.36em)))
  place(dx: 0.72em, dy: 0.5em, rect(width: 0.16em, height: 0.48em, fill: black, radius: 0.05em))
  place(dx: 0.7em, dy: 1.07em, circle(radius: 0.1em, fill: black))
})

// axioms, definitions, theorems …: a line on the left; axioms and theorems in italics; proofs, examples and remarks
// without a line; a box (box) grey
#let statement(kind, body) = {
  let italic = kind in ("axiom", "theorem", "lemma", "corollary")
  let content = {
    set text(style: if italic { "italic" } else { "normal" })
    body
  }
  if kind in ("axiom", "theorem", "lemma", "corollary") {
    context block(width: 100%, above: 1.2em, below: 1.2em, inset: (left: 0.8em, y: 0.2em), stroke: (left: 1.5pt + color-of("rule-statement")), content)
  } else if kind == "definition" {
    context block(width: 100%, above: 1.2em, below: 1.2em, inset: (left: 0.8em, y: 0.2em), stroke: (left: 1.5pt + color-of("rule-definition")), content)
  } else if kind == "merke" {
    // what must not be overlooked: a yellow box with a bar, a warning sign on the left
    block(width: 100%, above: 1.2em, below: 1.2em, inset: (left: 0.8em, rest: 0.6em), fill: rgb("#fff6d6"), stroke: (left: 3pt + rgb("#e0a100")),
      grid(columns: (auto, 1fr), column-gutter: 0.8em, warning-sign, content))
  } else if kind == "box" {
    context block(width: 100%, above: 1.2em, below: 1.2em, inset: 0.7em, fill: color-of("fill-box"), radius: 2pt, content)
  } else {
    block(width: 100%, above: 1.1em, below: 1.1em, content)
  }
}

// ------------------------------------------------------------------ formulas and figures

// "$$…$$": a formula on a line of its own, centred; with a mark its number on the right ("(2.3)")
#let display-formula(number: none, body) = block(width: 100%, above: 0.8em, below: 0.8em,
  if number == none { align(center, body) } else {
    grid(columns: (1fr, auto, 1fr), align: (left, center, right + horizon), [], body, number)
  })

// a chain of equations on lines of their own: the rows (left side, rest from the relation on) aligned at the relation,
// centred; with a mark its number on the right of the last row
// (each row a line of its own, so that left side and rest stand on the same baseline)
#let display-chain(number: none, ..rows) = block(width: 100%, above: 0.8em, below: 0.8em, context {
  let rows = rows.pos()
  let widest = calc.max(..rows.map(row => measure(row.at(0)).width))
  // the left side pushed right by a space, not in a box of its own: a box sits on its bottom in older Typst (0.13), not
  // on the formula's baseline
  let lines = rows.map(row => block(above: 0pt, below: 0pt, align(left, [#h(widest - measure(row.at(0)).width)#row.at(0)#h(0.2778em)#row.at(1)])))
  let chain = box(align(left, stack(dir: ttb, spacing: 0.55em, ..lines)))
  if number == none { align(center, chain) } else {
    grid(columns: (1fr, auto, 1fr), align: (left, center, right + bottom), [], chain, number)
  }
})

// "Abb. 2.3" in a caption
#let figure-label(body) = text(weight: "bold", body)

// a figure: its pictures side by side (as large as GeoAnim made them – the scale of index.yaml), smaller if they are
// wider than the text; the caption below
#let geo-figure(caption: none, ..pictures) = block(width: 100%, above: 1.4em, below: 1.4em, breakable: false, {
  layout(size => {
    let row = stack(dir: ltr, spacing: 0.8em, ..pictures.pos())
    let width = measure(row).width
    let fitted = if width > size.width { scale(size.width / width * 100%, reflow: true, row) } else { row }
    align(center, fitted)
  })
  if caption != none {
    v(0.3em)
    align(center, text(size: 0.9em, caption))
  }
})

// a row of pictures of one height (pictures from files, or "{height=…}"): items (ratio: width by height, picture: h => image(…, height: h)) or
// (ratio: …, missing: "address"); height: the row's, else it fills the width, at most max-height; never wider than the
// text
#let geo-row(height: none, max-height: 8cm, gap: 0.6em, caption: none, ..items) = block(width: 100%, above: 1.4em, below: 1.4em, breakable: false, {
  let items = items.pos()
  layout(size => {
    let gap = gap.to-absolute()
    let total = items.map(item => item.ratio).sum()
    let room = size.width - gap * (items.len() - 1)
    let fitting = room / total
    let wanted = if height != none { height.to-absolute() } else { calc.min(max-height.to-absolute(), fitting) }
    let h = calc.min(wanted, fitting)
    align(center, stack(dir: ltr, spacing: gap, ..items.map(item => if "picture" in item {
      (item.picture)(h)
    } else {
      box(width: item.ratio * h, height: h, stroke: 0.6pt + red, inset: 0.5em, text(fill: red, raw(item.missing)))
    })))
  })
  if caption != none {
    v(0.3em)
    align(center, text(size: 0.9em, caption))
  }
})

// a still in a row of stills: its step's description below, as wide as the picture
#let geo-still(picture, caption: none) = context {
  let width = measure(picture).width
  stack(dir: ttb, spacing: 0.5em, picture, block(width: width, align(center, text(size: 0.8em, hyphenate: true, caption))))
}

// a figure GeoAnim could not make (a faulty script): its address in a frame
#let missing-figure(source) = box(stroke: 0.6pt + red, inset: 1em, text(fill: red, raw(source)))

// ------------------------------------------------------------------ tests and worksheets ("type: test" / "worksheet")

// a sheet: A4, no running head, the page number at the bottom only with more than one page ("1 / 2"); several sheets
// in one file (the solutions of all groups): each counts its pages from 1, numbering: "1"
// text-size 12pt and wide lines: students write larger than print
// (dense: a document – no room to write between the lines)
#let sheet(text-size: 12pt, language: "de", margin: (x: 1.8cm, top: 1.6cm, bottom: 1.8cm), font: text-font, numbering: "1 / 1", dense: false, body) = {
  set page(paper: "a4", margin: margin, footer: context {
    let total = counter(page).final().first()
    if total > 1 { align(center, text(size: 0.85em, counter(page).display(numbering, both: numbering.contains("/")))) }
  })
  set text(font: font, size: text-size, lang: language)
  set par(justify: false, leading: if dense { 0.65em } else { 1em }, spacing: if dense { 1em } else { 1.3em })
  show heading: set text(weight: "bold")
  show heading.where(level: 2): it => block(above: 1.6em, below: 0.9em, sticky: true, text(size: 1.15em, it.body))
  show heading.where(level: 3): it => block(above: 1.2em, below: 0.6em, sticky: true, it.body)
  body
}

// the head: title, group and time (with the solutions red), the lines for name, class and date
#let sheet-head(title, info: "", solution: false, fields: ("Name", "Klasse", "Datum")) = {
  block(width: 100%, below: 1.4em, {
    grid(columns: (1fr, auto), align: (left + bottom, right + bottom),
      text(size: 1.35em, weight: "bold", title),
      text(fill: if solution { rgb("#d32f2f") } else { black }, weight: if solution { "bold" } else { "regular" }, info))
    v(0.6em)
    grid(columns: fields.enumerate().map(((i, _)) => if i == 0 { 2fr } else { 1fr }), column-gutter: 1.5em,
      ..fields.map(f => block(width: 100%, stroke: (bottom: 0.6pt), inset: (bottom: 3pt), f + ":")))
    v(0.3em)
    line(length: 100%, stroke: 1.2pt)
  })
}

// a blank to write on; with the solution the answer on it (red); a solution without a blank
#let blank(width) = box(width: width, stroke: (bottom: 0.6pt), outset: (bottom: 3pt), height: 1em)
#let blank-solution(width, body) = box(stroke: (bottom: 0.6pt), outset: (bottom: 3pt),
  box(width: width, align(center, text(fill: rgb("#d32f2f"), weight: "semibold", body))))
#let solution(body) = text(fill: rgb("#d32f2f"), weight: "semibold", body)
