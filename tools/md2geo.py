"""
md2geo: old Markdown pages (the Eleu documentation and worksheets) as GeoAnim documents ("type: document").

    python md2geo.py seite.md [weitere.md …] [-o ordner]
    python md2geo.py ordner [-o ordner]           (all .md in it)

Each page becomes name.geo.md (next to it, or in the -o folder), with
- a head: type: document, title from the first heading;
- {{toc 2 3}} → \\toc 2-3;
- ^^^ right 336 … ^^^ (a picture with its width, beside the text, maybe a caption) → ![caption](bild.jpg){width=9cm float=right};
- ![](bild.jpg=336) → ![](bild.jpg){width=9cm} (pixels at 96 dpi, to half centimetres);
- quotes ("> Anweisungen werden mit ; beendet.") → ::: merke … :::
\\clear stays (it ends the floating); code blocks stay as they are. What it does not know it reports.
"""

import os
import re
import sys


def centimetres(pixels):
    cm = int(pixels) * 2.54 / 96
    return f"{max(0.5, round(cm * 2) / 2):g}cm"


def picture(alt, source, size, side):
    attributes = []
    if size:
        attributes.append("width=" + centimetres(size))
    if side in ("left", "right"):
        attributes.append("float=" + side)
    return f"![{alt}]({source})" + ("{" + " ".join(attributes) + "}" if attributes else "")


IMAGE = re.compile(r"!\[([^\]]*)\]\(([^)=\s]+)(?:=([^)]*))?\)")


def pictures(line, side=None, size=None, caption=None):
    def replace(match):
        alt, source, options = match.group(1), match.group(2), match.group(3)
        own_size, own_side = size, side
        for option in (options or "").split(";"):
            option = option.strip()
            if option.isdigit():
                own_size = own_size or option
            elif option in ("left", "right"):
                own_side = own_side or option
        return picture(caption or alt, source, own_size, own_side)
    return IMAGE.sub(replace, line)


def convert(text, notes):
    lines = text.replace("\r", "").split("\n")
    out, title, fenced, quote = [], None, False, []
    i = 0

    def flush_quote():
        if quote:
            out.extend(["::: merke", *quote, ":::"])
            quote.clear()

    while i < len(lines):
        line = lines[i].rstrip()
        if line.lstrip().startswith("```"):
            flush_quote()
            fenced = not fenced
            out.append(line)
            i += 1
            continue
        if fenced:
            out.append(line)
            i += 1
            continue
        # quotes: what must be remembered
        if line.startswith(">"):
            quote.append(line[1:].strip())
            i += 1
            continue
        flush_quote()
        # (the headings as they are: their numbers order the topics)
        heading = re.match(r"^(#+)\s+(.*)$", line)
        if heading:
            if title is None and len(heading.group(1)) == 1:
                title = heading.group(2).strip()
            out.append(line)
            i += 1
            continue
        toc = re.match(r"^\s*\{\{\s*toc\s*(\d)?\s*(\d)?\s*\}\}\s*$", line)
        if toc:
            levels = (toc.group(1) or "2") + ("-" + toc.group(2) if toc.group(2) else ("-3" if not toc.group(1) else ""))
            out.append("\\toc " + levels)
            i += 1
            continue
        box = re.match(r"^\s*\^\^\^\s*(left|right)?\s*(\d+)?\s*$", line)
        if box:
            # a picture with its width (and side), the other lines its caption
            side, size = box.group(1), box.group(2)
            j = i + 1
            inner = []
            while j < len(lines) and not re.match(r"^\s*\^\^\^\s*$", lines[j]):
                inner.append(lines[j].rstrip())
                j += 1
            images = [l for l in inner if IMAGE.search(l)]
            caption = " ".join(l.strip() for l in inner if l.strip() and not IMAGE.search(l)) or None
            if images:
                out.append("")
                out.append(" ".join(pictures(l.strip(), side, size, caption) for l in images))
                out.append("")
            elif caption:
                notes.append(f"^^^ ohne Bild (Zeile {i + 1}): {caption}")
                out.append(caption)
            i = j + 1
            continue
        if "{{" in line:
            notes.append(f"unbekannt (Zeile {i + 1}): {line.strip()}")
        out.append(pictures(line))
        i += 1
    flush_quote()
    body = re.sub(r"\n{3,}", "\n\n", "\n".join(out)).strip() + "\n"
    head = "---\ntype: document\n" + (f"title: {title}\n" if title else "") + "---\n\n"
    return head + body


def main(arguments):
    output = None
    if "-o" in arguments:
        k = arguments.index("-o")
        output = arguments[k + 1]
        arguments = arguments[:k] + arguments[k + 2:]
    files = []
    for argument in arguments:
        if os.path.isdir(argument):
            files += [os.path.join(argument, f) for f in sorted(os.listdir(argument)) if f.endswith(".md") and not f.endswith(".geo.md")]
        else:
            files.append(argument)
    if not files:
        print(__doc__)
        return 1
    for file in files:
        notes = []
        with open(file, encoding="utf-8-sig") as source:
            text = convert(source.read(), notes)
        stem = os.path.basename(file)
        stem = stem[:-len(".geo.md")] if stem.endswith(".geo.md") else os.path.splitext(stem)[0]
        target = os.path.join(output or os.path.dirname(file), stem + ".geo.md")
        if os.path.abspath(target) == os.path.abspath(file):
            target = os.path.join(output or os.path.dirname(file), stem + ".neu.geo.md")
        os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
        with open(target, "w", encoding="utf-8") as result:
            result.write(text)
        print(f"{file} -> {target}")
        for note in notes:
            print("  " + note)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
