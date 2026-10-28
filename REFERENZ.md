# GeoAnim – Referenz der Skriptsprache

Alle Befehle mit allen Schreibweisen und den Optionen, die jeweils erlaubt sind. Erklärungen und Beispiele stehen in der [README](README.md); diese Datei ist zum Nachschlagen.

**Schreibweise hier:** `A`, `B`, `P` stehen für Namen von Punkten, `k` für einen Kreis, `g` für eine Strecke, Gerade oder einen Strahl, `D` für ein Vieleck, `w` für einen Winkel, `x y` für Koordinaten, `n` für eine Zahl. `[…]` ist freiwillig, `…` heißt „beliebig viele“. *Optionen* sind die in der Spalte genannten (siehe [Optionen](#optionen)).

## Inhalt

- [Grundregeln](#grundregeln)
- [Einstellungen](#einstellungen): `title`, `description`, `view`, `background`, `grid`, `axes`, `speed`, `keyword`
- [Texte, Vars, Voreinstellungen](#texte-vars-voreinstellungen): `set`, `var`, `default`
- [Objekte](#objekte): `point`, `segment`, `line`, `ray`, `circle`, `arc`, `sector`, `polygon`, `angle`, `text`
- [Zahlen](#zahlen): `number`
- [Schritte und Aktionen](#schritte-und-aktionen): `step`, `textstep`, `info`, `show`, `hide`, `highlight`, `unhighlight`, `move`, `rotate`, `reflect`, `pause`, `together`
- [Dateien](#dateien): `include`, `use`, `input`, `output`
- [Optionen](#optionen) – welche Option für welchen Befehl
- [Farben](#farben), [Standardwerte](#standardwerte), [Schlüsselwörter](#schlüsselwörter)
- [Markdown-Seiten](#markdown-seiten) – Kopf und Abbildungen einer Buchseite in Markdown

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
| `keyword` | `keyword Begriff, Begriff …` | Schlüsselwörter der Seite für `exportdir` (verlinkt in `info` und `description` der anderen Seiten, nicht in Schrittbeschreibungen); Begriffe durch Kommas getrennt, ein Begriff aus einem oder mehreren Wörtern, Endungen nach `/`: `Punkt/e/en`, `rechte/r/n Winkel/s/n`; hinter einer `step`-Zeile verweisen sie auf diesen Schritt; mit `|` fortsetzbar |

## Texte, Vars, Voreinstellungen

| Befehl | Schreibweisen | Hinweise |
|---|---|---|
| `set` | `set name = Text …` | benannter Text für `text` und `label`; mit `|` fortsetzbar; gilt ab seiner Zeile |
| `var` | `var name Optionen…` | Optionen mit Namen; darf andere Vars enthalten; gilt ab seiner Zeile, eine spätere gleichnamige ersetzt sie; was für ein Objekt nicht passt, wird übergangen |
| `default` | `default [Arten…] Optionen…` | Voreinstellung für alle folgenden Objekte der Arten `point` `segment` `line` `ray` `circle` `arc` `sector` `polygon` `angle` `text` `show` `hide` `highlight` `unhighlight` `move` `rotate` `reflect`; ohne Art für alle; was nicht passt, wird übergangen |

Reihenfolge der Optionen eines Objekts: Voreinstellungen ohne Art, dann die der Art, dann die eigenen – die spätere gewinnt.

## Objekte

Alle Objekte: `art name = Definition Optionen…`. Vor dem ersten `step` gehören sie zum Startbild, in einem Schritt werden sie dort gezeichnet. Die Optionen *Zeichnen* sind `duration`, `linear`/`ease`/`easein`/`easeout`/`easeinout` und `hidden`.

### `point` – Punkt

| Schreibweise | Punkt |
|---|---|
| `point A = x y` | freier Punkt (kann sich bewegen) |
| `point M = midpoint A B` | Mittelpunkt von A und B |
| `point D = parallel g through C` · `point D = perpendicular g through C` | C + (B − A) für g = AB (bzw. um 90° gedreht): mit C die Parallele bzw. Senkrechte zu g durch C |
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
| `line p = parallel g through C` · `perpendicular g through C` | Parallele bzw. Senkrechte zu g durch C (ebenso `segment`, `ray`) |
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
| `circle k = center M radius A B` · `radius l` | um M, Radius = Abstand von A und B bzw. die Zahl l (folgt der Zeichnung) |
| `circle k = use datei A B …` | Ergebnis einer anderen Datei |

**Optionen:** `color` `width`, `fill` (gefüllte Kreisscheibe: `fill lightblue` oder `fill` allein in der Linienfarbe) mit `opacity` (Standard 0,2), *Zeichnen*. Die Füllung wächst beim Zeichnen mit dem Zirkelstrich mit und liegt bei den Flächen der Vielecke und Winkel.

### `polygon` – Vieleck

| Schreibweise | Vieleck |
|---|---|
| `polygon D = A B C …` | gefülltes Vieleck, mindestens 3 Punkte |
| `polygon D = use datei A B …` | Ergebnis einer anderen Datei |

**Optionen:** `color` `width` `fill` `opacity`, *Zeichnen*.

### `arc`, `sector` – Kreisbogen, Kreissektor

| Schreibweise | Objekt |
|---|---|
| `arc b = k from A to B` | Bogen des Kreises k gegen den Uhrzeigersinn von A bis B (den anderen Bogen: `from B to A`); A und B geben nur die Richtungen vom Mittelpunkt an |
| `sector s = k from A to B` | Kreissektor: die beiden Radien und der Bogen dazwischen, gefüllt |
| `arc b = k from 30 to 120` · `from A to 90` | statt eines Punkts eine Richtung in Grad (gegen den Uhrzeigersinn ab Osten), auch eine Konstante; ebenso beim Sektor |
| `arc b = k from A to B clockwise` | im Uhrzeigersinn von A nach B (der andere Bogen als ohne `clockwise`), so auch gezeichnet; `arrow` zeigt in Zeichenrichtung; ebenso beim Sektor |

**Optionen:** `arc`: `color` `width` `label` (außen an der Bogenmitte) `labelsize` `shift` `arrow` · `arrow start` · `arrow both` `halo`, *Zeichnen*. `sector`: `color` `width` `fill` (Standard: Linienfarbe) `opacity` (Standard 0,2) `label` (innen auf der Winkelhalbierenden) `labelsize` `shift` `halo`, *Zeichnen*. Beide wachsen beim Zeichnen von A nach B und bleiben auf dem Kreis, wenn er sich ändert.

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
| `text t = "…" below s` · `text t = "…" below s gap n` | eine Zeile unter dem Text s (einem Text an Koordinaten oder selbst unter einem), mit dessen Ausrichtung; `gap`: n Einheiten Abstand zusätzlich; wandert mit s |

**Optionen:** `color` `size` `align` · `align left` · `align center` · `align right` `shift` (`offset` veraltet), *Zeichnen*. Ein Text an festen Koordinaten steht standardmäßig mit seiner Mitte dort (`align center`); `align left` beginnt dort, `align right` endet dort (mehrzeilige Texte entsprechend bündig). `shift` verschiebt ihn von da aus.

## Zahlen

| Schreibweise | Zahl |
|---|---|
| `number n = length s` | Länge einer Strecke (eines Bogens: Bogenlänge) |
| `number n = length A B` | Abstand zweier Punkte |
| `number n = degrees w` | Größe eines Winkels in Grad (eines Bogens oder Sektors: Mittelpunktswinkel) |
| `number n = area D` · `number n = area k` | Flächeninhalt eines Vielecks, Kreises oder Sektors |
| `number n = perimeter D` · `number n = perimeter k` | Umfang eines Vielecks, Kreises oder Sektors |
| `number n = radius k` | Radius eines Kreises |
| `number n = 5.2` · `number n = a * sqrt(3) / 2` | berechnet aus Zahlen und anderen Zahlen: `+ - * / ^`, Klammern, `sqrt( )`, `abs( )`, `pi`; Leerzeichen beliebig, `#` beginnt einen Kommentar. Nur aus festen Zahlen berechnet: eine **Konstante**, überall verwendbar, wo eine Zahl steht (Koordinaten, Optionen, `move … to`, `rotate … by`, `pause`, …); mit gemessenen Zahlen: folgt ihnen, nur für Texte (`{n}`) |

**Optionen:** keine. Zahlen folgen ihren Objekten; Texte zeigen sie mit `{n}` / `{n:Stellen}` (Standard: Winkel ganze Grad, sonst eine Nachkommastelle; Dezimalkomma).

## Schritte und Aktionen

Aktionen stehen in einem Schritt und laufen nacheinander. Die Optionen *Ablauf* sind `duration` und `linear`/`ease`/`easein`/`easeout`/`easeinout`.

| Befehl | Schreibweisen | Optionen | Hinweise |
|---|---|---|---|
| `step` | `step Text …` · `step Text … #marke` | – | neuer Schritt; mit `|` fortsetzbar; `#marke` am Ende: sein Name für Abbildungen einer Markdown-Seite (eindeutig in der Datei, gehört nicht zum Text; ebenso bei `textstep`) |
| `textstep` | `textstep Text …` | – | Schritt nur mit Text: seine `info`-Texte stehen dort, wo sonst das Bild ist (bei langem Text scrollbar); erlaubt sind nur `info`, `keyword`, `pause` und Zahlen. Das Bild ist im nächsten Schritt wieder da, wie es war. Dauer beim Durchlaufen: eine `pause`, sonst eine Lesezeit (3 s + ¼ s je Wort) |
| `info` | `info Text …` | – | Erklärung zum Schritt, linksbündig unter der Beschreibung; mit `|` fortsetzbar; ein weiteres `info` wird ein neuer Absatz; `info ## Titel` ist eine Zwischenüberschrift |
| `show` | `show A B …` | *Ablauf* | zeigt Objekte (zeichnet versteckte, blendet ausgeblendete ein); schon sichtbare bleiben, wie sie sind |
| | `show label A B …` | *Ablauf* | blendet nur Beschriftungen ein |
| `hide` | `hide A B …` | *Ablauf* | blendet Objekte aus; nicht sichtbare (schon ausgeblendet oder nie gezeigt) bleiben, wie sie sind |
| | `hide label A B …` | *Ablauf* | blendet nur Beschriftungen aus |
| | `hide all` | *Ablauf* | blendet alles aus, was gerade zu sehen ist (etwa am Anfang eines Schritts mit neuer Zeichnung) |
| | `hide label all` | *Ablauf* | blendet alle Beschriftungen aus |
| `highlight` | `highlight A B …` | *Ablauf*, `color` | hebt Objekte hervor: zweimal kurz farbig (Standard orange) und dicker bzw. größer, Füllungen leuchten auf |
| | `highlight A B … keep` | *Ablauf*, `color` | ebenso, bleibt aber hervorgehoben bis `unhighlight` |
| `unhighlight` | `unhighlight A B …` | *Ablauf* | nimmt ein `highlight … keep` zurück |
| `move` | `move A to x y` | *Ablauf* | freier Punkt zu Koordinaten |
| | `move A to P` | *Ablauf* | freier Punkt dorthin, wo P gerade ist |
| | `move t to x y` · `move t to P` | *Ablauf* | Text an festen Koordinaten (ein Text neben einem Punkt wandert mit dem Punkt) |
| | `move P to n` | *Ablauf* | Punkt auf seinem Kreis (Grad) bzw. seiner Strecke (Position) |
| | `move label A B … to x y` | *Ablauf* | verschiebt die Beschriftungen auf den Shift x y (von ihrem gewohnten Platz aus), z. B. wenn sie nach einer Bewegung stören |
| | `move label A B … to SE` · `to 300` · `… gap n` | *Ablauf* | dreht die Beschriftungen von Punkten auf einem Bogen um den Punkt in eine andere Richtung (`N NE E SE S SW W NW` oder Grad), mit `gap` auch auf einen anderen Abstand |
| | `move A B … along P Q` | *Ablauf* | verschiebt Objekte um den Vektor PQ (ihre freien Punkte; bei einem Text an festen Koordinaten seine Position) |
| `rotate` | `rotate A B … around M by n` | *Ablauf* | dreht Objekte um M um n Grad (negativ: im Uhrzeigersinn) |
| `reflect` | `reflect A B … across g` · `reflect A B … across P Q` | *Ablauf*, `straight` | spiegelt Objekte an der Geraden g (auch Strecke oder Strahl) bzw. PQ: die Figur klappt um wie eine Buchseite; mit `straight` laufen die Punkte gleichmäßig senkrecht hinüber |
| | `reflect A B … in Z` | *Ablauf*, `straight` | spiegelt Objekte am Punkt Z: Drehung um 180°; mit `straight` laufen die Punkte geradlinig durch Z |
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
| `fill` | Farbe oder – (eigene Farbe) | | | | ✓ | ✓ | ✓ | | |
| `opacity` | 0 … 1 (Füllung) | | | | ✓ | ✓ | ✓ | | |
| `label` | `"…"` oder `set`-Name | ✓ | ✓ | ✓ | | | ✓ | | |
| `nolabel` | – | ✓ | ✓ | ✓ | | | ✓ | | |
| `labelsize` | n (pt) | ✓ | ✓ | ✓ | | | ✓ | | |
| `labelpos` | n (0 bei A, 1 bei B) | | ✓ | ✓ | | | | | |
| `labeldir` | `N` `NE` `E` `SE` `S` `SW` `W` `NW` oder Grad (gegen den Uhrzeigersinn ab Osten): Richtung der Beschriftung vom Punkt aus | ✓ | | | | | | | |
| `labelgap` | n (pt, Standard 4): Zwischenraum zwischen Punkt und Beschriftung bei `labeldir` | ✓ | | | | | | | |
| `shift` | x y (Koordinateneinheiten) – Verschiebung der Beschriftung von ihrem gewohnten Platz aus | ✓ | ✓ | ✓ | | | ✓ | ✓ | |
| `offset` | x y – *veraltet*: Lage relativ zum Punkt selbst (ohne den gewohnten Abstand); beim Winkel wie `shift` | ✓ | ✓ | ✓ | | | ✓ | ✓ | |
| `radius` | n (Einheiten) | | | | | | ✓ | | |
| `align` | `left` · `center` · `right` | | | | | | | ✓ | |
| `arrow` | – · `start` · `end` · `both` | | ✓ | | | | ✓ | | |
| `keep` | – | | | | | | | | `highlight` |
| `halo` | – (schmaler Rand in Hintergrundfarbe um die Beschriftung, beim Winkel in der Farbe seiner Fläche: Linien enden vor den Buchstaben) | ✓ | ✓ | ✓ | | | ✓ | ✓ | |
| `back` · `front` | – (ganz nach hinten, unter alle Füllungen · ganz nach vorn, über alles außer Texten) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `hidden` | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `straight` | – | | | | | | | | `reflect` |
| `duration` | n (ms) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | `show` `hide` `highlight` `unhighlight` `move` `rotate` `reflect` `together` |
| `linear` `ease` `easein` `easeout` `easeinout` | – | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | `show` `hide` `highlight` `unhighlight` `move` `rotate` `reflect` |

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
| `highlight` | orange | | 1200 ms, `linear` (mit `keep` 400 ms, `ease`) |
| `unhighlight` | | | 400 ms, `ease` |
| `move`, `rotate`, `reflect` | | | 1000 ms, `ease` |
| `pause` | | | 500 ms |
| Schritt ohne Aktion | | | 500 ms |

## Markdown-Seiten

Eine Buchseite in Markdown (`….geo.md` neben den Skripten, im Aufbau; Erklärung in der [README](README.md#buchseiten-in-markdown-im-aufbau)).

| Element | Schreibweise | Hinweise |
|---|---|---|
| Kopf | `---` · `title: …` · `description: …` · `keywords: [Punkt/e/en, …]` · `---` | am Dateianfang, alles freiwillig; ohne `title` gilt die erste `#`-Überschrift |
| Formel | `$…$` · `$$` … `$$` auf eigenen Zeilen | AsciiMath; `\$` ist ein Dollarzeichen |
| Abbildung | `![Unterschrift](datei.geo)` | die ganze Animation; allein im Absatz |
| | `![…](datei.geo#2)` · `#gleich` · `#0` | Standbild am Ende eines Schritts (Nummer, Marke oder Name aus dem Schritttext); `#0` das Startbild |
| | `![…](datei.geo#3..4)` · `#3-4` · `#von..bis` | Player nur für die Schritte von … bis (ab Schritt 1) |

## Schlüsselwörter

Diese Wörter können keine Namen sein:

`title` `description` `info` `keyword` `view` `background` `grid` `axes` `speed` `var` `default` `include` `input` `output` `use` `draw` `steps` `copy` `set` `step` `textstep` `point` `segment` `line` `ray` `circle` `arc` `sector` `polygon` `angle` `text` `number` `length` `degrees` `area` `perimeter` `show` `hide` `all` `highlight` `unhighlight` `keep` `move` `rotate` `reflect` `along` `around` `across` `in` `by` `pause` `together` `end` `midpoint` `intersect` `parallel` `perpendicular` `center` `through` `radius` `at` `from` `to` `on` `x` `y` `below` `gap` `align` `left` `right` `duration` `color` `width` `fill` `opacity` `size` `labelsize` `labelpos` `labeldir` `labelgap` `label` `nolabel` `hidden` `offset` `shift` `arrow` `start` `both` `straight` `back` `front` `halo` `clockwise` `linear` `ease` `easein` `easeout` `easeinout`
