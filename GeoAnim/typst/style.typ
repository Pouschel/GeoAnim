// The look of the printed book (Typst). geoanim typst writes this file next to book.typ, unless the book's folder has a
// style.typ of its own: then that one. To change the look, copy this file into the book's folder (beside index.yaml)
// and change it there.
//
// Everything the pages use is defined here: the template (book), the title page, chapters, statements (axioms,
// definitions, theorems …), figures and display formulas. Numbers come from GeoAnim (as on the web and in the e-book);
// Typst numbers only the pages.

// ------------------------------------------------------------------ settings

#let text-font = ("STIX Two Text", "Libertinus Serif")   // STIX Two Text: as in the figures and formulas
#let accent = rgb("#1f4e8c")
#let rule-statement = rgb("#9fb3cf")
#let rule-definition = rgb("#8fbf9a")
#let fill-box = rgb("#f2f3f5")

// ------------------------------------------------------------------ the book

// text-size: the font size of the text – unless index.yaml says it ("print: text: 10pt"), then that one (the figures and
// formulas are made for it, and the preview shows them in proportion to it: best set it there)
#let book(title: none, author: none, text-size: 11pt, language: "de", body) = {
  set document(title: title, author: if author == none { () } else { author })
  set page(
    width: 17cm,
    height: 24cm,
    margin: (inside: 2cm, outside: 2.5cm, top: 2.6cm, bottom: 2.4cm),
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
  set text(font: text-font, size: text-size, lang: language)
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

// the title page and its back
#let title-page(title: none, author: none, description: none) = {
  page(header: none, footer: none, {
    v(5cm)
    align(center, {
      text(size: 2.4em, weight: "bold", title)
      if author != none {
        v(1.5cm)
        text(size: 1.3em, author)
      }
      if description != none {
        v(2cm)
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
      text(fill: accent, number)
      h(0.6em)
    }
    title
  })
}

// the number in front of a page's heading ("2.1")
#let section-number(number) = {
  text(fill: accent, number)
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

// axioms, definitions, theorems …: a line on the left; axioms and theorems in italics; proofs, examples and remarks
// without a line; a box (box) grey
#let statement(kind, body) = {
  let italic = kind in ("axiom", "theorem", "lemma", "corollary")
  let content = {
    set text(style: if italic { "italic" } else { "normal" })
    body
  }
  if kind in ("axiom", "theorem", "lemma", "corollary") {
    block(width: 100%, above: 1.2em, below: 1.2em, inset: (left: 0.8em, y: 0.2em), stroke: (left: 1.5pt + rule-statement), content)
  } else if kind == "definition" {
    block(width: 100%, above: 1.2em, below: 1.2em, inset: (left: 0.8em, y: 0.2em), stroke: (left: 1.5pt + rule-definition), content)
  } else if kind == "box" {
    block(width: 100%, above: 1.2em, below: 1.2em, inset: 0.7em, fill: fill-box, radius: 2pt, content)
  } else {
    block(width: 100%, above: 1.1em, below: 1.1em, content)
  }
}

// ------------------------------------------------------------------ formulas and figures

// "$$…$$": a formula on a line of its own, centred
#let display-formula(body) = block(width: 100%, above: 0.8em, below: 0.8em, align(center, body))

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

// a figure GeoAnim could not make (a faulty script): its address in a frame
#let missing-figure(source) = box(stroke: 0.6pt + red, inset: 1em, text(fill: red, raw(source)))
