# GeoAnim – Referenz der Skriptsprache

Alle Befehle mit allen Schreibweisen und den Optionen, die jeweils erlaubt sind. Erklärungen und Beispiele stehen in der [README](README.md); diese Datei ist zum Nachschlagen.

**Schreibweise hier:** `A`, `B`, `P` stehen für Namen von Punkten, `k` für einen Kreis, `g` für eine Strecke, Gerade oder einen Strahl, `D` für ein Vieleck, `w` für einen Winkel, `x y` für Koordinaten, `n` für eine Zahl. `[…]` ist freiwillig, `…` heißt „beliebig viele“. *Optionen* sind die in der Spalte genannten (siehe [Optionen](#optionen)).

## Inhalt

- [Grundregeln](#grundregeln)
- [Einstellungen](#einstellungen): `title`, `description`, `view`, `background`, `grid`, `axes`, `speed`, `keyword`
- [Texte, Vars, Voreinstellungen](#texte-vars-voreinstellungen): `set`, `var`, `default`
- [Objekte](#objekte): `point`, `segment`, `line`, `ray`, `circle`, `polygon`, `angle`, `text`
- [Zahlen](#zahlen): `number`
- [Schritte und Aktionen](#schritte-und-aktionen): `step`, `info`, `show`, `hide`, `move`, `rotate`, `pause`, `together`
- [Dateien](#dateien): `include`, `use`, `input`, `output`
- [Optionen](#optionen) – welche Option für welchen Befehl
- [Farben](#farben), [Standardwerte](#standardwerte), [Schlüsselwörter](#schlüsselwörter)

## Grundregeln

| Regel | |
|---|---|
| Zeilen | eine Anweisung pro Zeile; Einrückung ist egal |
| Kommentar | `#` bis zum Zeilenende (außer `#rgb` / `#rrggbb` als Farbe und in Texten bis zum Zeilenende) |
| Zahlen | mit Punkt: `2.5`, `-0.5`, `.5` |
| Namen | Buchstaben, Ziffern, `_`, dahinter beliebig viele `'`: `A`, `A'`, `P_1`; Groß-/Kleinschreibung zählt; keine [Schlüsselwörter](#schlüsselwörter) |
| Texte in Anführungszeichen | bei `text`, `label`: `"…"`, darin `\"` und `\n`; oder der Name eines `set`-Textes |
| Texte bis zum Zeilenende | hinter `title`, `description`, `step`, `info`, `keyword`, `set name =` |
| Fortsetzung | eine Zeile, die mit `|` beginnt, setzt den Text der Zeile davor fort; `|` allein beginnt einen neuen Absatz |
| Formeln | `$…$` in AsciiMath, in allen Texten; `\$` ist ein Dollarzeichen |
| Markdown | `**fett**`, `*kursiv*`, `_kursiv_`, `` `Code` ``, `[Text](Adresse)`, `<Adresse>`; `\*`, `\{` usw. halten ein Zeichen fest |
| Zahlen in Texten | `{n}` oder `{n:2}` (2 Nachkommastellen) für eine `number`; nicht innerhalb von `$…$` |
| Koordinaten | mathematisch, y nach oben; Winkel in Grad, gegen den Uhrzeigersinn ab der x-Richtung |

## Einstellungen

Vor dem ersten `step` (außer `keyword`).

| Befehl | Schreibweisen | Hinweise |
|---|---|---|
| `title` | `title Text …` | Überschrift; Rest der Zeile, mit `|` fortsetzbar |
| `description` | `description Text …` | steht unter dem Startbild und ist die Beschreibung der exportierten Seite; mit `|` fortsetzbar |
| `view` | `view x -1 .. 11 y -1 .. 7` | sichtbarer Bereich; vor dem ersten Objekt |
| `background` | `background Farbe` | Hintergrundfarbe |
| `grid` | `grid` · `grid n` | Gitter mit Abstand 1 bzw. n |
| `axes` | `axes` | Koordinatenachsen mit Zahlen |
| `speed` | `speed n` | Tempo der ganzen Animation (0.5 halb so schnell, 2 doppelt so schnell) |
| `keyword` | `keyword Begriff, Begriff …` | Schlüsselwörter der Seite für `exportdir`; Begriffe durch Kommas getrennt, ein Begriff aus einem oder mehreren Wörtern, Endungen nach `/`: `Punkt/e/en`, `rechte/r/n Winkel/s/n`; hinter einer `step`-Zeile verweisen sie auf diesen Schritt; mit `|` fortsetzbar |

## Texte, Vars, Voreinstellungen

| Befehl | Schreibweisen | Hinweise |
|---|---|---|
| `set` | `set name = Text …` | benannter Text für `text` und `label`; mit `|` fortsetzbar; gilt ab seiner Zeile |
| `var` | `var name Optionen…` | Optionen mit Namen; darf andere Vars enthalten; gilt ab seiner Zeile, eine spätere gleichnamige ersetzt sie; was für ein Objekt nicht passt, wird übergangen |
| `default` | `default [Arten…] Optionen…` | Voreinstellung für alle folgenden Objekte der Arten `point` `segment` `line` `ray` `circle` `polygon` `angle` `text` `show` `hide` `move` `rotate`; ohne Art für alle; was nicht passt, wird übergangen |

Reihenfolge der Optionen eines Objekts: Voreinstellungen ohne Art, dann die der Art, dann die eigenen – die spätere gewinnt.

## Objekte

Alle Objekte: `art name = Definition Optionen…`. Vor dem ersten `step` gehören sie zum Startbild, in einem Schritt werden sie dort gezeichnet. Die Optionen *Zeichnen* sind `duration`, `linear`/`ease`/`easein`/`easeout`/`easeinout` und `hidden`.

### `point` – Punkt

| Schreibweise | Punkt |
|---|---|
| `point A = x y` | freier Punkt (kann sich bewegen) |
| `point M = midpoint A B` | Mittelpunkt von A und B |
| `point S = intersect o1 o2` · `point S T = intersect o1 o2` | Schnittpunkt(e) zweier Kreise, Strecken, Geraden, Strahlen |
| `point P = on k at n` | auf einem Kreis, bei n Grad |
| `point P = on g at n` | auf einer Strecke/Geraden/einem Strahl bei A + n·(B − A) |
| `point X = copy P` | freier Punkt an der Stelle, an der P gerade ist |
| `point D = use datei A B …` | Ergebnis einer anderen Datei (siehe [`use`](#dateien)) |

**Optionen:** `color` `size` `label` `nolabel` `labelsize` `shift` (`offset` veraltet), *Zeichnen*.

### `segment`, `ray`, `line` – Strecke, Strahl, Gerade

| Schreibweise | Objekt |
|---|---|
| `segment s = A B` · `segment A B` (heißt dann `AB`) | Strecke von A nach B |
| `ray r = A B` | Strahl ab A durch B, bis zum Bildrand |
| `line g = A B` | Gerade durch A und B, bis zum Bildrand |
| `segment s = use datei A B …` (ebenso `ray`, `line`) | Ergebnis einer anderen Datei |

**Optionen:**

| Option | `segment` | `ray` | `line` |
|---|:-:|:-:|:-:|
| `color` `width` | ✓ | ✓ | ✓ |
| `label` `nolabel` `labelsize` `labelpos` `shift` (`offset` veraltet) | ✓ | ✓ | ✓ |
| `arrow` · `arrow start` · `arrow end` · `arrow both` | ✓ | ✓ | – |
| *Zeichnen* | ✓ | ✓ | ✓ |

### `circle` – Kreis

| Schreibweise | Kreis |
|---|---|
| `circle k = center M through P` | um M durch P (wird ab P gezogen) |
| `circle k = center M radius n` | um M mit festem Radius |
| `circle k = use datei A B …` | Ergebnis einer anderen Datei |

**Optionen:** `color` `width`, *Zeichnen*.

### `polygon` – Vieleck

| Schreibweise | Vieleck |
|---|---|
| `polygon D = A B C …` | gefülltes Vieleck, mindestens 3 Punkte |
| `polygon D = use datei A B …` | Ergebnis einer anderen Datei |

**Optionen:** `color` `width` `fill` `opacity`, *Zeichnen*.

### `angle` – Winkel

| Schreibweise | Winkel |
|---|---|
| `angle w = A S B` | Winkel bei S, gegen den Uhrzeigersinn vom Strahl SA zum Strahl SB (auch über 180°, auch 360°); ein rechter Winkel ohne Beschriftung bekommt den Punkt |
| `angle w = use datei A B …` | Ergebnis einer anderen Datei |

**Optionen:** `color` `width` `fill` `opacity` `label` `nolabel` `labelsize` `radius` `shift` (verschiebt die Beschriftung von der Winkelhalbierenden aus; `offset` ist dasselbe) `arrow` · `arrow start` · `arrow end` · `arrow both`, *Zeichnen*.

### `text` – Text im Bild

| Schreibweise | Text |
|---|---|
| `text t = "…" at x y` · `text t = name at x y` | an festen Koordinaten (zentriert) |
| `text t = "…" at P` · `text t = name at P` | neben einem Punkt, wandert mit ihm |

**Optionen:** `color` `size` `shift` (`offset` veraltet), *Zeichnen*. Ein Text an festen Koordinaten steht mit seiner Mitte dort; `shift` verschiebt ihn von da aus.

## Zahlen

| Schreibweise | Zahl |
|---|---|
| `number n = length s` | Länge einer Strecke |
| `number n = length A B` | Abstand zweier Punkte |
| `number n = degrees w` | Größe eines Winkels in Grad |
| `number n = area D` · `number n = area k` | Flächeninhalt eines Vielecks bzw. Kreises |
| `number n = perimeter D` · `number n = perimeter k` | Umfang eines Vielecks bzw. Kreises |
| `number n = radius k` | Radius eines Kreises |

**Optionen:** keine. Zahlen folgen ihren Objekten; Texte zeigen sie mit `{n}` / `{n:Stellen}` (Standard: Winkel ganze Grad, sonst eine Nachkommastelle; Dezimalkomma).

## Schritte und Aktionen

Aktionen stehen in einem Schritt und laufen nacheinander. Die Optionen *Ablauf* sind `duration` und `linear`/`ease`/`easein`/`easeout`/`easeinout`.

| Befehl | Schreibweisen | Optionen | Hinweise |
|---|---|---|---|
| `step` | `step Text …` | – | neuer Schritt; mit `|` fortsetzbar |
| `info` | `info Text …` | – | Erklärung zum Schritt, linksbündig unter der Beschreibung; mit `|` fortsetzbar; ein weiteres `info` wird ein neuer Absatz |
| `show` | `show A B …` | *Ablauf* | zeigt Objekte (zeichnet versteckte, blendet ausgeblendete ein); schon sichtbare bleiben, wie sie sind |
| | `show label A B …` | *Ablauf* | blendet nur Beschriftungen ein |
| `hide` | `hide A B …` | *Ablauf* | blendet Objekte aus; nicht sichtbare (schon ausgeblendet oder nie gezeigt) bleiben, wie sie sind |
| | `hide label A B …` | *Ablauf* | blendet nur Beschriftungen aus |
| | `hide all` | *Ablauf* | blendet alles aus, was gerade zu sehen ist (etwa am Anfang eines Schritts mit neuer Zeichnung) |
| | `hide label all` | *Ablauf* | blendet alle Beschriftungen aus |
| `move` | `move A to x y` | *Ablauf* | freier Punkt zu Koordinaten |
| | `move A to P` | *Ablauf* | freier Punkt dorthin, wo P gerade ist |
| | `move t to x y` · `move t to P` | *Ablauf* | Text an festen Koordinaten (ein Text neben einem Punkt wandert mit dem Punkt) |
| | `move P to n` | *Ablauf* | Punkt auf seinem Kreis (Grad) bzw. seiner Strecke (Position) |
| | `move label A B … to x y` | *Ablauf* | verschiebt die Beschriftungen auf den Shift x y (von ihrem gewohnten Platz aus), z. B. wenn sie nach einer Bewegung stören |
| | `move A B … along P Q` | *Ablauf* | verschiebt Objekte um den Vektor PQ (ihre freien Punkte; bei einem Text an festen Koordinaten seine Position) |
| `rotate` | `rotate A B … around M by n` | *Ablauf* | dreht Objekte um M um n Grad (negativ: im Uhrzeigersinn) |
| `pause` | `pause` · `pause n` | – | wartet 500 ms bzw. n ms |
| `together` | `together` … `end` | – | alles dazwischen beginnt gleichzeitig |
| | `together duration n` … `end` | `duration` | ebenso, der Block dauert genau n ms |

## Dateien

| Befehl | Schreibweisen | Optionen | Hinweise |
|---|---|---|---|
| `include` | `include pfad` · `include "pfad mit Leerzeichen"` | – | setzt eine Datei an dieser Stelle ein (relativ zur einbindenden Datei) |
| `use` | `art name… = use datei A B … [draw \| steps]` | die der Ergebnisse | benutzt eine Datei wie eine Funktion: Objekte des Aufrufers für ihre Eingaben, Ergebnisse unter den Namen links |
| | `name… = use datei A B …` | die passenden | ohne Art: alle Ausgaben der Reihe nach; `_` überspringt eine |
| | `use datei A B … [draw \| steps]` | – | ohne Ergebnis: zeigt nur die Animation der Datei |
| `input` | `input point A = x y` (ebenso jede andere Art und Definition) | wie die Art | Eingabe, wenn die Datei benutzt wird; allein gilt die Definition |
| `output` | `output point C = …` (jede Definition) · `output name …` | wie die Art | Ergebnis; die benutzte Datei endet mit ihrer letzten `output`-Zeile |

`use`-Varianten: ohne Zusatz nur die Ergebnisse; `draw` zeichnet die Konstruktion im aktuellen Schritt (Hilfsobjekte blenden danach aus); `steps` übernimmt dazu die Schritte der Datei.

## Optionen

| Option | Werte | `point` | `segment` `ray` | `line` | `circle` | `polygon` | `angle` | `text` | Aktionen |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `color` | Farbe | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `width` | n (pt) | | ✓ | ✓ | ✓ | ✓ | ✓ | | |
| `size` | n (pt): Radius bzw. Schriftgröße | ✓ | | | | | | ✓ | |
| `fill` | Farbe | | | | | ✓ | ✓ | | |
| `opacity` | 0 … 1 (Füllung) | | | | | ✓ | ✓ | | |
| `label` | `"…"` oder `set`-Name | ✓ | ✓ | ✓ | | | ✓ | | |
| `nolabel` | – | ✓ | ✓ | ✓ | | | ✓ | | |
| `labelsize` | n (pt) | ✓ | ✓ | ✓ | | | ✓ | | |
| `labelpos` | n (0 bei A, 1 bei B) | | ✓ | ✓ | | | | | |
| `shift` | x y (Koordinateneinheiten) – Verschiebung der Beschriftung von ihrem gewohnten Platz aus | ✓ | ✓ | ✓ | | | ✓ | ✓ | |
| `offset` | x y – *veraltet*: Lage relativ zum Punkt selbst (ohne den gewohnten Abstand); beim Winkel wie `shift` | ✓ | ✓ | ✓ | | | ✓ | ✓ | |
| `radius` | n (Einheiten) | | | | | | ✓ | | |
| `arrow` | – · `start` · `end` · `both` | | ✓ | | | | ✓ | | |
| `hidden` | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `duration` | n (ms) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | `show` `hide` `move` `rotate` `together` |
| `linear` `ease` `easein` `easeout` `easeinout` | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | `show` `hide` `move` `rotate` |

`ease` ist dasselbe wie `easeinout`. Ein Punkt (pt) ist ein Pixel bei 1280 Pixel Bildbreite. Hinter `use` gelten die Optionen der Ergebnisse. Eine direkt geschriebene Option, die nicht passt, ist ein Fehler; aus einer Var oder Voreinstellung wird sie übergangen.

## Farben

`black` `white` `gray` (auch `grey`) `lightgray` (auch `lightgrey`) `red` `darkred` `green` `lightgreen` `blue` `lightblue` `navy` `orange` `yellow` `purple` `violet` `pink` `brown` `teal` `cyan` `magenta` oder `#rgb` / `#rrggbb`.

## Standardwerte

| | Farbe | Größe | erscheint in |
|---|---|---|---|
| Punkt | blau | Radius 5 pt, Beschriftung 24 pt | 400 ms, `easeout` |
| Strecke, Strahl, Gerade | schwarz | 2,5 pt | 800 ms, `ease` |
| Kreis | schwarz | 2,5 pt | 1200 ms, `ease` |
| Vieleck | schwarz, Füllung 0,2 | 2,5 pt | 600 ms, `easeout` |
| Winkel | schwarz, Füllung 0,2 | 1,5 pt, Radius passend zur Beschriftung | 600 ms, `easeout` |
| Text | schwarz | 26 pt | 400 ms, `easeout` |
| `show`, `hide` | | | 400 ms, `easeout` |
| `move`, `rotate` | | | 1000 ms, `ease` |
| `pause` | | | 500 ms |
| Schritt ohne Aktion | | | 500 ms |

## Schlüsselwörter

Diese Wörter können keine Namen sein:

`title` `description` `info` `keyword` `view` `background` `grid` `axes` `speed` `var` `default` `include` `input` `output` `use` `draw` `steps` `copy` `set` `step` `point` `segment` `line` `ray` `circle` `polygon` `angle` `text` `number` `length` `degrees` `area` `perimeter` `show` `hide` `all` `move` `rotate` `along` `around` `by` `pause` `together` `end` `midpoint` `intersect` `center` `through` `radius` `at` `to` `on` `x` `y` `duration` `color` `width` `fill` `opacity` `size` `labelsize` `labelpos` `label` `nolabel` `hidden` `offset` `shift` `arrow` `start` `both` `linear` `ease` `easein` `easeout` `easeinout`
