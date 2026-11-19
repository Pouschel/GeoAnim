# GeoAnim – Referenz der Buchseiten in Markdown

Alles, was GeoAnim über gewöhnliches Markdown hinaus in Buchseiten (`….geo.md`) versteht, zum Nachschlagen. Die
Skriptsprache der `.geo`-Dateien steht in der [Referenz](REFERENZ.md), der Überblick in der [README](README.md).

**Stand:** im Aufbau. Bisher gibt es die Vorschau in VS Code; der Export (Web, E-Book, Druck) folgt.

## Inhalt

- [Datei](#datei)
- [Kopf](#kopf): `title`, `description`, `keywords` (Tests: `type`, `subject`, `time`, `groups`)
- [Text und Formeln](#text-und-formeln)
- [Abbildungen](#abbildungen): Verweise, Schritte, eigenes Skript
- [Buch und Web](#buch-und-web): Bildfolge, Zuschnitt
- [Attribute](#attribute): `crop`, `float`, `width`
- [Struktogramme](#struktogramme): `::: nassi`, Schleifen, Verzweigungen, nummerierte Kästen
- [Aussagen](#aussagen): Axiom, Definition, Satz, Beweis …
- [Marken und Verweise](#marken-und-verweise): `{#name}`, `§name`
- [Nummerierung](#nummerierung): Kapitel, Abschnitte, Zähler, `numbering:`
- [Stichwörter](#stichwörter): `{kw}` am Begriff, Links auf andere Seiten
- [Fehler und Warnungen](#fehler-und-warnungen)
- [Tests und Arbeitsblätter](#tests-und-arbeitsblätter): `type: test`, Lücken `[[…]]`, Punkte, `\page`, `geoanim sheet`
- [Beispiel](#beispiel)

## Datei

| | |
|---|---|
| Name | `….geo.md`, neben den Skripten – meist wie das Skript der Seite: `10_Punkte.geo.md` zu `10_Punkte.geo` |
| Warum die Doppelendung | VS Code gibt den Seiten eine eigene Sprache („GeoAnim-Markdown“: Vorschau, Hervorhebung, Gliederung) ohne Konflikt mit seiner Markdown-Unterstützung; andere Programme (GitHub, Markdown-Editoren) zeigen sie weiter als Markdown |
| Aufgabenteilung | das Skript beschreibt Bild und Ablauf (mit kurzen Schrittbeschreibungen), die Seite den Text |

## Kopf

Ein YAML-Block zwischen zwei `---`-Zeilen ganz am Anfang der Datei; alle Einträge sind freiwillig. **Besser ohne:** Der
Titel ist dann überall die erste `#`-Überschrift (ändert man sie, stimmt alles), Stichwörter stehen im Text an ihrer
Stelle (`{kw}`). Der Kopf wird noch gelesen, ist aber nur für alte Seiten da.

| Schlüssel | Schreibweise | Bedeutung |
|---|---|---|
| `title` | `title: Punkte und ihre Lage` | Name der Seite in Menü, Inhaltsverzeichnis und Seitenkopf; ohne ihn gilt die erste `#`-Überschrift (so kann der Name kürzer sein als die Überschrift) |
| `description` | `description: Was ein Punkt ist …` | Kurzbeschreibung (Übersicht, Suchmaschinen) |
| `keywords` | `keywords: [Punkt/e/en, Lage]` | Stichwörter der ganzen Seite (Links führen an den Seitenanfang), Schreibweise wie bei `keyword` in Skripten; auch eines allein oder eines je Zeile mit `- `; Stichwörter an einer bestimmten Stelle: siehe [Stichwörter](#stichwörter) |

Ein unbekannter Schlüssel ist eine Warnung, ein Fehler im YAML ein Fehler (mit Zeile). Für Tests und Arbeitsblätter hat
der Kopf eigene Schlüssel: siehe [Tests und Arbeitsblätter](#tests-und-arbeitsblätter).

## Text und Formeln

| Element | Schreibweise | Hinweise |
|---|---|---|
| Text | CommonMark | Überschriften, Hervorhebung, Listen, Zitate, Code, Links |
| Tabellen | `\| a \| b \|` mit Trennzeile `\|---\|---\|` | |
| Fußnoten | `Text[^1]` · `[^1]: Anmerkung` | |
| Attribute | `{#marke .klasse}` hinter einem Element | |
| Formel im Text | `$bar(AB)$` | AsciiMath wie in Skripten; kein Leerzeichen direkt nach dem ersten und vor dem letzten `$`. Eine lange Formel darf vor ihren Relationszeichen (`=` `<` `<=` `>=` `!=` `~~` `=>` `:=` …, nicht in Klammern) umbrechen – sie bleibt eine Formel, nichts von Hand zerlegen; kurze Formeln (bis 10 em breit, so wie sie erscheinen: `sigma` ist ein Buchstabe) bleiben ganz. Satzzeichen direkt an der Formel (`(` davor, `,` `.` `)` `-` … danach) bleiben mit ihr auf der Zeile |
| abgesetzte Formel | `$$…$$` im Absatz · `$$` … `$$` auf eigenen Zeilen | zentriert auf einer Zeile für sich; ein Satzzeichen direkt dahinter (`$$…$$.`, beim Block `$$.` als Schlusszeile) steht mit auf der (letzten) Formelzeile |
| Gleichungskette | `$$` auf eigener Zeile, dann eine Zeile `a = b`, weitere Zeilen, die mit `=` (oder `<`, `<=`, `~~` …) beginnen, dann `$$` | die Zeilen untereinander, ausgerichtet am Relationszeichen: links die linke Seite (nur in der ersten Zeile), rechts der Rest. Eine Zeile ohne Relation am Anfang setzt die vorige fort; steht in der ersten Zeile nur die linke Seite, beginnt die Kette in der zweiten. Mit Marke die Nummer an der letzten Zeile |
| nummerierte Formel | `$$a^2+b^2=c^2$$ {#gl-pythagoras}.` · beim Block `$$ {#gl-name}` als Schlusszeile (oder als erste Zeile) | eine abgesetzte Formel mit Marke bekommt eine Nummer rechts auf ihrer Zeile – (2.3), siehe [Nummerierung](#nummerierung); `§gl-pythagoras` zeigt „(2.3)“. Formeln im Text (`$…$`) haben keine Nummer |
| Dollarzeichen | `\$` | |
| Sprungmarke | `[nach oben](#oben)` | ein Link mit `#…` bleibt ein Link auf eine Stelle der Seite |
| andere Seite | `[Strecken](20_Strecken.geo.md)` | öffnet in der Vorschau diese Seite |

Eine Gleichungskette ist **ein** Block – die `$$` stehen allein auf ihren Zeilen, dazwischen eine Zeile je Glied:

```geomd
$$
|AC|^2 = (u_x+v_x)^2+(u_y+v_y)^2
= u_x^2+u_y^2+v_x^2+v_y^2+2(u_x v_x+u_y v_y)
= |AB|^2+|BC|^2+2(u_x v_x+u_y v_y).
$$
```

Drei Absätze `$$…$$` untereinander sind drei einzelne Formeln, keine Kette.

Ein `$`, das keine Formel beginnt (kein schließendes, oder ein Leerzeichen dahinter), ist ein Fehler – wie in Skripten.

## Abbildungen

Ein **Bild** auf ein Skript oder eine Bilddatei (PNG, JPEG, GIF, WebP, SVG neben der Seite), **allein im Absatz**;
mehrere solche Bilder in einem Absatz bilden eine Abbildung und stehen nebeneinander. Ein Bild mitten im Text bleibt ein
gewöhnliches Bild.

**Unterschrift und Nummer:** Abbildungen werden nummeriert („Abb. 2.1“, „Abb. 2.2“ … im Kapitel 2, siehe
[Nummerierung](#nummerierung)); die Nummer steht vor der Unterschrift. Die Unterschrift ist der Text in den eckigen Klammern (Markdown und Formeln erlaubt; bei mehreren Bildern
mit `·` verbunden); ist er leer, nimmt ein Standbild die Schrittbeschreibung aus dem Skript. Eine Bildfolge hat ihre
Schrittbeschreibungen an den Einzelbildern. **Marke:** `![…](#bez){#abb-gerade}` (siehe [Marken und Verweise](#marken-und-verweise)).

| Verweis | im Web | im Buch |
|---|---|---|
| `![Unterschrift](datei.geo)` | Player für die ganze Animation | ihr letztes Bild |
| `![](datei.geo#2)` | Standbild am Ende von Schritt 2 | ebenso |
| `![](datei.geo#0)` | das Startbild (vor dem ersten Schritt) | ebenso |
| `![](datei.geo#3..4)` · `#3-4` | Player nur für die Schritte 3 bis 4; beginnt mit dem Bild nach Schritt 2, zählt wie im Skript | Bildfolge: die Standbilder der Schritte nebeneinander |
| `![](#2)` · `![](#3..4)` · `![](#gleich)` | dasselbe aus dem **eigenen Skript** der Seite (`seite.geo` zu `seite.geo.md`) | ebenso |
| `![](#)` | die ganze Animation des eigenen Skripts | ebenso |

**Schritte** werden angegeben durch

| Angabe | Beispiel | Hinweise |
|---|---|---|
| Nummer | `#2`, `#3..4`, `#3-4` | wie im Player; `#0` das Startbild (nur allein, nicht in Bereichen) |
| Marke | `#gleich` | im Skript `step Gleiche Punkte #gleich` (ebenso bei `textstep`): `#name` am Ende des Schritttexts, gehört nicht zum Text, eindeutig in der Datei; bleibt beim Umbenennen des Schritts gültig |
| Name aus dem Schritttext | `#gleiche-punkte` | Text in Kleinbuchstaben, `-` zwischen den Wörtern; ä → ae, ö → oe, ü → ue, ß → ss, é → e; Formeln lesbar (`$bar(AB)$` → `ab`); haben zwei Schritte denselben Namen, braucht einer eine Marke |
| Bereich | `#3..4`, `#verschieden..gleich`, `#2..gleich` | `..` zwischen Anfang und Ende (beliebig gemischt); `-` nur zwischen zwei Nummern |

Ein Bereich beginnt bei Schritt 1 oder später und darf nicht rückwärts laufen. Die Abbildungen zeigen die Skripte, wie
sie **gespeichert** sind.

## Buch und Web

**Im Web** ist jede Abbildung ein Player (Bild, Schrittbeschreibung, Steuerleiste; ohne Titel, Menü und Erklärungen)
oder ein Standbild. Spielt ein Player, halten die anderen der Seite an.

**Im Buch** gibt es nur Standbilder:

- **Bildfolge:** ein Bereich wird zu den Standbildern seiner Schritte nebeneinander, höchstens drei in einer Reihe (bei
  vier Bildern 2 × 2), jedes mit Schrittnummer und Schrittbeschreibung aus dem Skript; darunter die Bildunterschrift.
- **Zuschnitt:** jede Abbildung wird auf ihren Inhalt zugeschnitten – was gezeichnet ist, samt Beschriftungen, aber ohne
  Strahlen und Geraden (die reichen ohnehin bis zum Rand) –, dazu ein Rand von 5 % des Inhalts, mindestens 16 pt. Die
  Abbildung wird dadurch **kleiner**, nicht größer: Linien, Punkte und Schrift behalten die Größe, die sie in der ganzen
  Ansicht hätten; das Seitenverhältnis folgt dem Inhalt. Die Bilder einer Bildfolge bekommen denselben Ausschnitt.
  `{crop=no}` schaltet das ab.

Die Vorschau in VS Code zeigt beides; oben rechts schaltet „Buch“/„Web“ um (Voreinstellung: Buch).

### Bilder aus Dateien: eine Reihe gleicher Höhe

```markdown
![](bilder/halo.jpg) ![](halo.geo#3) ![](skizze.png)          ← füllt die Textbreite, höchstens 8 cm hoch
![](bilder/halo.jpg) ![](skizze.png){height=4cm}              ← die Reihe 4 cm hoch
![](bilder/halo.jpg){width=5cm} ![](halo.geo#3)               ← das Foto 5 cm breit, das andere gleich hoch
```

Eine Abbildung mit Bilddateien (oder mit `{height=…}`) ist eine **Reihe gleicher Höhe**: jedes Bild so breit, wie sein
Seitenverhältnis bei dieser Höhe verlangt. Die Höhe kommt aus `height` an der Abbildung, sonst aus `width` an einem ihrer
Bilder (die anderen richten sich danach), sonst füllt die Reihe die Breite des Texts, höchstens 8 cm hoch. Eine Reihe
ist nie breiter als der Text – sonst wird sie kleiner. Die Auflösung (DPI) einer Datei zählt nicht, nur ihr
Seitenverhältnis.

- Skripte in einer Reihe zeigen ein Bild (`#2` oder das ganze: sein letztes Bild), keine Bildfolge; sie werden auf die
  Höhe der Reihe gebracht, Linien und Texte also mit ihnen größer oder kleiner.
- Fotos werden nicht zugeschnitten (`crop` gilt nur für Skripte).
- Die Unterschrift ist eine Zeile unter der Reihe (die Texte der Bilder mit `·` verbunden).
- Auf einem **Test oder Arbeitsblatt** hat eine Abbildung nur dann „Abb. …“, wenn sie eine Unterschrift oder eine Marke
  hat – ohne steht nur das Bild da („die Zeichnung rechts“).
- Ins Buch kommen die Dateien mit: im HTML eingebettet, im E-Book und im Typst-Projekt (`pictures/`) als Dateien.

## Attribute

In geschweiften Klammern direkt hinter dem Bild, mehrere mit Leerzeichen: `![](#2){float=right width=40%}`; dort
steht auch die Marke: `![](#2){#abb-strahl crop=no}`.

| Attribut | Werte | Bedeutung |
|---|---|---|
| `crop` | `yes` (Voreinstellung) · `no` | im Buch auf den Inhalt zuschneiden oder die ganze `view` zeigen; gilt für das Bild |
| `float` | `left` · `right` | der folgende Text fließt um die Abbildung – **festgelegt, noch nicht umgesetzt**: im Web und im E-Book später, im gedruckten Buch bleiben Abbildungen zentriert |
| `width` | `8cm` · `60mm` · `200pt` · `3in` · `40%` | im Buch die Breite der Abbildung nach dem Zuschnitt (bei einer Bildfolge der ganzen Reihe); der Maßstab ergibt sich daraus. Prozent: Breite zu `float` im Web – **festgelegt, noch nicht umgesetzt** |
| `scale` | `6mm` · `0.5cm` · `14pt` | im Buch die Länge einer Einheit für diese Abbildung |
| `height` | `4cm` · `40mm` · `120pt` · `2in` | die Höhe einer Reihe gleicher Höhe (macht die Abbildung zu einer, siehe oben) |
| `width` in einer Reihe | `5cm` · `50mm` … | die Breite dieses einen Bildes; die Reihe bekommt die Höhe, die es dabei hat – entweder `height` oder ein `width` |
| `nowarn` | (ohne Wert) | *wirkungslos*: schaltete eine Warnung zur Größe der Texte im Druck ab, die es nicht mehr gibt; bleibt erlaubt |

`float`, `width` und `scale` gelten für die ganze Abbildung; haben zwei ihrer Bilder verschiedene Werte, ist das ein
Fehler. In einer Reihe gleicher Höhe gilt `width` dagegen für das eine Bild.

**Größe im Buch:** Es gilt die genaueste Angabe – `width` oder `scale` an der Abbildung, sonst `print width`/`print scale`
im Skript (REFERENZ.md), sonst `print: scale` der `index.yaml`. Die Beschriftungen bleiben dabei so groß, wie die
`index.yaml` sagt (`labels`); eine kleinere Abbildung hat also im Verhältnis größere Texte als der Player, in dem Texte
ein fester Anteil der Bildbreite sind. Wie die Abbildung im Druck aussieht, zeigt die Druckansicht der Skriptvorschau
(Druckersymbol) und die Buchansicht der Seitenvorschau. Beschriftungen an Objekten halten ihren Abstand in pt; ein
freier Text bleibt mit `labeldir`/`labelgap` ebenso auf Abstand (REFERENZ.md, `text`), ein Text nur mit `at x y` rückt
bei kleineren Abbildungen näher an die Linien.

## Struktogramme

Ein Struktogramm (Nassi-Shneiderman-Diagramm) schreibt man in Zeilen wie Pseudocode; GeoAnim zeichnet es (SVG, im PDF, E-Book und in ILIAS als Bild):

```markdown
::: nassi
print("Merle");
repeat (4)
    print("Puschel");
if (x > y)
    print(x);
else if (x == y)
    print("gleich");
else
    print(y);
:::
```

| Zeile | Kasten |
|---|---|
| beliebiger Text | eine Anweisung |
| beginnt mit `repeat`, `while`, `for` (oder `wiederhole`, `solange`, `für`) | eine Kopfschleife: die Zeile als Kopf, die darunter eingerückten Zeilen als Körper |
| beginnt mit `if` (`wenn`) | eine Verzweigung: die Bedingung (ohne `if` und äußere Klammern) im Dreieck, die eingerückten Zeilen links |
| `else` (`sonst`) direkt unter dem `if`, gleich weit eingerückt | die rechte Seite; `else if …`: eine weitere Verzweigung darin |

- Nach dem Schlüsselwort steht beliebiger Text: `repeat (4)`, `repeat ([[4]])` (eine Lücke), `solange Q nicht auf dem Kreis liegt`.
- **Code** – eine Zeile, die auf `;`, `{` oder `}` endet, mit `//` beginnt oder ein Wort mit Klammern ist (`print(x)`, `repeat (4)`) – steht in Festbreitenschrift, anderer Text in der Buchschrift; `` `…` `` macht einen Teil zu Code.
- **Lücken** `[[4]]`: auf dem Schülerblatt eine Linie, mit der Lösung die Antwort rot darauf.
- `::: nassi numbered`: statt der Texte Nummern in den Kästen (in Lesereihenfolge: Schleifenkopf vor dem Körper, Bedingung vor den Seiten) – das leere Struktogramm für „Ordne die Programmteile den Kästchen zu“ (dazu ein `::: match`).
- An den Seiten einer Verzweigung steht `true`/`false`; `::: nassi ja|nein` (oder andere zwei Wörter) ändert das.
- Die Größe folgt der Schrift des Texts; ist die Spalte zu schmal, wird das Struktogramm kleiner.
- `{{…}}`, `alt` und Gruppen gehen in den Zeilen wie überall auf Tests und ILIAS-Fragen.
- Fehler mit Zeile: eine Zeile, die tiefer eingerückt ist, ohne unter `repeat`/`if`/… zu stehen; ein `else` ohne `if`.

## Aussagen

Axiome, Definitionen, Sätze und Ähnliches stehen in Containern aus drei Doppelpunkten:

```markdown
::: axiom {#ax-gerade} Geradenaxiom
Durch zwei verschiedene Punkte verläuft **genau eine Gerade**.
:::
```

→ **Axiom 1 (Geradenaxiom).** *Durch zwei verschiedene Punkte verläuft genau eine Gerade.*

| Container | Anzeige | nummeriert | Darstellung |
|---|---|---|---|
| `axiom` | Axiom | ja | Kasten, kursiv |
| `definition` · `def` | Definition | ja | Kasten (grün) |
| `theorem` | Satz | ja | Kasten, kursiv |
| `lemma` | Lemma | ja | Kasten, kursiv |
| `corollary` | Folgerung | ja | Kasten, kursiv |
| `proof` | Beweis | nein | ohne Kasten, endet mit ∎; gehört zur letzten Aussage davor (Satz, Lemma, Folgerung) auf der Seite – `::: proof §satz-x` für den Beweis einer anderen: „Beweis von Satz 3.2.“ |
| `example` | Beispiel | ja | ohne Kasten |
| `remark` | Bemerkung | nein | ohne Kasten |
| `box` | – (nur ein Titel, wenn es einen gibt) | nein | neutraler grauer Kasten, nicht kursiv |

- **Eröffnungszeile:** `::: art`, dann freiwillig die Marke `{#name}` und ein Titel (Markdown und Formeln erlaubt; er
  erscheint in Klammern) – in beliebiger Reihenfolge: `::: def Strecke {#def-strecke}` ist dasselbe wie
  `::: def {#def-strecke} Strecke`.
- **Inhalt:** beliebiges Markdown bis zur Zeile `:::` – Absätze, Formeln, Listen; die Bezeichnung steht am Anfang des
  ersten Absatzes.
- **`box`** ist der neutrale Kasten für Hervorhebungen ohne Bedeutung als Aussage – etwa eine Leitfrage. Ohne Titel
  nur der Kasten, mit Titel (`::: box Merke`) steht „**Merke.**“ am Anfang. Ein Kasten wird nicht gezählt und hat keine
  Marke; `>` bleibt für Zitate.
- **Zählung:** Axiome haben einen eigenen Zähler, der durch das ganze Buch läuft: Axiom 1, 2, 3 … – sie bilden die
  überschaubare Liste der Grundannahmen. Beispiele haben einen eigenen Zähler im Kapitel (Beispiel 2.1, 2.2 …), alle
  anderen nummerierten Arten teilen sich einen (Definition 2.1, Satz 2.2, Lemma 2.3 …). Siehe
  [Nummerierung](#nummerierung).
- **Verschachteln:** Ein Container in einem anderen braucht außen mehr Doppelpunkte, damit das innere `:::` nicht den
  äußeren schließt: `:::: box` … `::: theorem` … `:::` … `::::`.
- **Im Editor:** Markierte Zeilen schließt die Glühbirne (Strg+.) in einen Container jeder Art ein, steht der Cursor in
  einem, löst sie ihn auf (siehe `editors/vscode/README.md`).
- Die deutschen Bezeichnungen sind vorerst fest; später kommen sie aus der Sprache des Buchs.

## Marken und Verweise

**Marke** – ein Name in geschweiften Klammern mit `#`, mindestens zwei Zeichen (Buchstaben, Ziffern, `-`, `_`):

| an | Schreibweise | Verweis zeigt |
|---|---|---|
| Abbildung | `![…](#bez){#abb-gerade}` | Abb. 2.3 |
| Aussage | `::: axiom {#ax-gerade} …` | Axiom 2, Satz 2.14 |
| Formel | `$$…$$ {#gl-pythagoras}` | (2.3) – „nach §gl-pythagoras“, „aus Ungleichung §gl-dreieck“ |
| Seite | `# Abstandsaxiome {#abstandsaxiome}` (die `#`-Überschrift, der Seitentitel) | Abschnitt 2.1 |
| Kapitel | `# Geraden und ihre Ordnung {#kap-geraden}` in der Kapiteleinführung (`00_….geo.md` oder neben dem Ordner) | Kapitel 2 |
| Abschnitt in einer Seite | `## Die Gerade {#abschnitt-gerade}` | „Die Gerade“ (`##`, `###` … haben keine Nummern) |

**Verweis** – `§name` irgendwo im Text: `Nach §ax-gerade ist die Gerade in §abb-gerade eindeutig.` → „Nach Axiom 2 ist
die Gerade in Abb. 3 eindeutig.“ (im Web und im E-Book ein Link).

| Schreibweise | Bedeutung |
|---|---|
| `§name` | Verweis: zeigt, was die Marke ist („Abb. 3“) |
| `§*name` | mit Titel in Klammern: „Abschnitt 2.4 (Das Axiom von Pasch)“, „Axiom 8 (Axiom von Pasch)“ – bei Seiten, Kapiteln und Aussagen mit Titel; ohne Titel (Abbildung, Formel, Aussage ohne Titel) wie `§name` |
| `§§name` | ebenso, im gedruckten Buch zusätzlich mit Seitenzahl („Abb. 3 auf S. 14“; noch nicht umgesetzt) |
| `[eigener Text](#name)` | gewöhnlicher Link mit eigenem Text |
| `\§name`, `§ 5` | das Paragraphenzeichen selbst |

- Der Name endet beim ersten Zeichen, das kein Buchstabe, keine Ziffer, kein `-` oder `_` ist (in „§abb-gerade.“ gehört
  der Punkt nicht dazu); ein `-` am Ende gehört nicht dazu.
- **Marken gelten im ganzen Buch** (alle `….geo.md` unter dem Ordner mit der `index.yaml`, ohne Buch im selben Ordner; nicht in Dateien und Ordnern mit `_` am Anfang):
  ein Verweis braucht keinen Dateinamen, und verschobene Aussagen bleiben erreichbar. Eine Marke darf im Buch nur einmal
  vorkommen. Die anderen Seiten zählen, wie sie gespeichert sind.
- Ein Verweis auf eine andere Seite zeigt die Nummer, die die Marke dort hat – bei Axiomen die Nummer im ganzen Buch, bei den übrigen Arten vorerst die Nummer auf jener Seite.
- Eine unbekannte Marke ist ein Fehler; die Seite zeigt an der Stelle „??“.
- Fährt man mit der Maus über einen Verweis, zeigt der Tooltip den Titel der Marke (ohne Titel ihren Text: den Anfang
  der Aussage, die Bildunterschrift).
- **Wo eine Aussage verwendet wird:** In der Vorschau und im Buch als eine HTML-Datei (`geoanim html`) steht unter jeder
  Aussage mit Marke klein „Verwendet in: Beweis von Satz 3.4 (Winkelsumme) · Abschnitt 5.2 (Dreiecke)“ – jede Stelle mit
  ihrem Titel und als Link zu ihrem ersten Verweis –, oder „Nicht verwendet.“ Die Stelle ist der Beweis (mit der Aussage, die er beweist), die
  nummerierte Aussage, die Abbildung oder sonst der Abschnitt der Seite, in dem der Verweis steht; mehrere Verweise aus
  derselben Stelle zählen einmal („(2×)“). Im gedruckten Buch und im E-Book steht das nicht.
- **In VS Code:** nach `§` bietet die Vervollständigung alle Marken des Buchs an (mit Art, Nummer und Text), in
  `![](#` bzw. `![](datei.geo#` (auch nach `..`) die Schritte des Skripts (Marke oder Name, mit Nummer und
  Schrittbeschreibung); das Tooltip auf `§name` zeigt, worauf er verweist; F12 bzw. Strg+Klick springt zur Marke, auch
  auf einer anderen Seite. Über einer Aussage mit Marke steht grau, wo sie verwendet wird (ein Klick listet die
  Stellen); Umschalt+F12 auf `§name` oder `{#name}` listet alle Verweise im Buch.

## Nummerierung

**Kapitel und Abschnitte:** Ein Ordner im Buchordner (dem mit der `index.yaml`) ist ein Kapitel, nummeriert in
Buchreihenfolge – die Zahl vor dem Namen gibt nur die Reihenfolge: `10_Abstandsgeometrie` ist Kapitel 1, wenn es der
erste Ordner ist. Seiten und Unterordner eines Kapitels werden gemeinsam in ihrer Reihenfolge nummeriert: Seiten 1.1,
1.2, ein Unterordner 1.3 mit seinen Seiten 1.3.1, 1.3.2 … Ordner ohne Seiten zählen nicht, Seiten direkt im Buchordner
(etwa ein Vorwort) haben keine Nummer. Die `#`-Überschrift einer Seite bekommt ihre Nummer davor („1.2 Abstandsaxiome“),
das Inhaltsverzeichnis des E-Books die Nummern der Kapitel und Seiten. `##`-Überschriften und tiefere bleiben ohne
Nummer.

**Kapiteleinführung:** Eine Seite mit der Nummer 0 in einem Kapitelordner (`20_Geraden/00_Einleitung.geo.md`) führt ins
Kapitel ein. Ihre `#`-Überschrift ist die des Kapitels, mit dessen Nummer („2 Geraden und ihre Ordnung“; ohne
Einführung steht dort der Ordnername), ihr Text steht vor dem ersten Abschnitt, und sie hat keine eigene Nummer – die
Seite danach bleibt 2.1. Eine Marke an ihrer Überschrift ergibt „Kapitel 2“. Ihre Aussagen und Abbildungen zählen im
Kapitel vor denen von 2.1. Im Inhaltsverzeichnis (PDF, E-Book, HTML) steht das Kapitel mit ihrem Titel und führt zu ihr;
im PDF beginnt das Kapitel mit ihr auf einer rechten Seite. Das gilt ebenso für einen Unterordner („2.3“, Verweis „Abschnitt 2.3“).
Statt im Ordner kann die Einführung auch **neben ihm** liegen, mit genau seinem Namen: `20_Geraden.geo.md` neben
`20_Geraden/` ist die Einführung von `20_Geraden/` – gleich wie `20_Geraden/00_….geo.md`; ihre Skripte und Bilder liegen
neben ihr. Hat ein Kapitel beides, ist das ein Fehler („has two introductions“).

**Zähler:** Jede nummerierte Art zählt entweder mit einem eigenen Zähler oder mit dem gemeinsamen. Ein Zähler gilt im
ganzen Buch, in jedem Kapitel oder auf jeder Seite; im Kapitel steht die Kapitelnummer davor (Satz 2.14), auf der Seite
deren Nummer (Satz 2.1.3), im ganzen Buch nichts (Axiom 5). Voreinstellung:

| Art | Zähler | Beispiel |
|---|---|---|
| Axiom | eigener, im ganzen Buch | Axiom 1, 2, 3 … |
| Abbildung | eigener, im Kapitel | Abb. 2.1, 2.2 … |
| Beispiel | eigener, im Kapitel | Beispiel 2.1, 2.2 … |
| Formel (nur mit Marke) | eigener, im Kapitel | (2.1), (2.2) … |
| Definition, Satz, Lemma, Folgerung | gemeinsamer, im Kapitel | Definition 2.1, Satz 2.2, Folgerung 2.3 … |

Beweise, Bemerkungen und Kästen haben keine Nummer. Gezählt wird in Buchreihenfolge über alle Seiten (ohne Dateien und
Ordner mit `_`). Eine einzelne Seite außerhalb eines Buchs zählt für sich, ohne Kapitelnummer.

**Ändern** lässt sich das in der `index.yaml`; jeder Eintrag ist freiwillig, was fehlt, bleibt wie oben:

```yaml
numbering:
  within: chapter     # wo die Zähler neu anfangen: chapter (Satz 2.14), book (Satz 47) oder page (Satz 2.1.3)
  axiom: own book     # eigener Zähler, durch das ganze Buch ("own" allein: für Axiome dasselbe)
  figure: own         # eigener Zähler (im Bereich von within)
  example: own        # shared: zählt mit den Definitionen und Sätzen
  equation: own       # Formeln mit Marke: (2.1), (2.2) …
  default: shared     # der gemeinsame Zähler (mit book, chapter oder page: sein eigener Bereich)
  headings: yes       # Kapitel und Seiten nummeriert (no: ohne Nummern vor den Überschriften)
```

Auch `definition`, `theorem`, `lemma` und `corollary` können `own` bekommen. Unbekannte Einträge und Werte meldet der
Export als Warnung.

## Stichwörter

Ein Stichwort steht dort, wo der Begriff eingeführt wird – am fett (oder kursiv) gesetzten Begriff, mit `kw` in
geschweiften Klammern direkt dahinter. Links auf das Stichwort führen genau an diese Stelle.

```markdown
So entsteht ein **Strahl**{kw=/s/en}, auch **Halbgerade**{kw=/n} genannt.
Die gerade Verbindung heißt **Strecke**{kw}. Ein **Haus**{kw=/es/e,Häuser/n} …
```

| Schreibweise | Stichwort mit den Formen |
|---|---|
| `**Strecke**{kw}` | Strecke |
| `**Strahl**{kw=/s/en}` | Strahl, Strahls, Strahlen – Endungen (mit `/` am Anfang) zum fetten Wort |
| `**Haus**{kw=/es/e,Häuser/n}` | Haus, Hauses, Hause, Häuser, Häusern – nach dem Komma weitere Formen desselben Begriffs, ausgeschrieben (Umlaute, unregelmäßige Formen) |
| `**Geraden**{kw=Gerade/n}` | Gerade, Geraden – ohne `/` am Anfang ausgeschrieben, wenn das fette Wort selbst gebeugt ist |
| `**rechter Winkel**{kw=rechte/r/n Winkel/s}` | mehrere Wörter wie bei `keyword` in Skripten |

- Werte mit Leerzeichen gehen ohne Anführungszeichen: `## Metrische Räume {kw=Metrik/en, metrische/r Raum}` (der Wert
  reicht bis zum nächsten Attribut oder zur schließenden Klammer).
- Der **Begriff** (der Name im Stichwortverzeichnis) ist die erste Form: das fette Wort bzw. das erste ausgeschriebene.
- Ebenso an einer **Überschrift** (`## Der Kreis {kw=Kreis/es/e}`) oder einer **Aussage**
  (`::: def Strecke {#def-strecke kw=Strecke/n}`): der Link führt dann dorthin. Ohne Wert nimmt sie ihren Text bzw.
  Titel.
- Stichwörter der **ganzen Seite** stehen im Kopf (`keywords:`).

**Links:** Die Stichwörter aller Seiten und aller Skripte des Buchs bilden eine Liste; in den anderen Seiten wird das
**erste Vorkommen je Abschnitt** (bis zur nächsten Überschrift) zum Link auf seine Stelle – nur ganze Wörter, mit Groß-
und Kleinschreibung wie angegeben, die längste Form zuerst („rechter Winkel“ vor „Winkel“). Nicht verlinkt wird in
Überschriften, in Links, in Formeln und Code und an der Stelle eines Stichworts selbst; die eigenen Stichwörter einer
Seite nicht auf ihr selbst. `\Wort` verhindert den Link (der Backslash erscheint nicht). Haben zwei Seiten dasselbe
Stichwort, behält es die erste in Buchreihenfolge, die andere bekommt eine Warnung. Hat ein Skript eine gleichnamige
Seite (`seite.geo` neben `seite.geo.md`), zählen nur die Stichwörter der Seite. **In der Vorschau** sind die Links
blau, das Tooltip nennt Stichwort und Zielseite, ein Klick öffnet die Seite (oder das Skript am Schritt) im Editor an
der Stelle.

## Fehler und Warnungen

Fehler werden in VS Code rot unterstrichen und stehen in der Vorschau über der Seite; die Seite wird trotzdem gezeigt.

| Fehler | Beispiel |
|---|---|
| `$` ohne Formel | `kostet 5$` (richtig: `5\$`) |
| Fehler im Kopf | falsches YAML, `title` ohne Text, Stichwort, das kein Wort ist |
| Skript fehlt oder ist fehlerhaft | `![](fehlt.geo)`; ein Fehler im Skript mit Datei und Zeile |
| unbekannter Schritt | `#7` bei vier Schritten; `#nichts` (die Meldung nennt die vorhandenen Namen) |
| mehrdeutiger Name | zwei Schritte heißen „Erster“: einer braucht eine Marke |
| falscher Bereich | `#0..2`, `#3..2` |
| falsche Schrittangabe | `#3...4` |
| eigenes Skript ohne passenden Namen | `![](#2)` in `seite.md` oder in einer noch nie gespeicherten Seite |
| falscher Attributwert | `{crop=vielleicht}`, `{float=oben}`, `{width=breit}` |
| unbekannte Art einer Aussage | `::: satz` (richtig: `::: theorem`) |
| Marke an einem Kasten | `::: box {#leitfrage}` (die Marke gehört an die Überschrift davor) |
| Marke doppelt | auf derselben Seite oder auf einer anderen Seite des Buchs |
| Marke aus einem Zeichen | `{#a}` |
| zwei Marken an einer Abbildung | `![](#1){#abb-a}` und `![](#2){#abb-b}` im selben Absatz |
| unbekannte Marke in einem Verweis | `§nichts` |
| falsches Stichwort | `**Haus**{kw=/1}` (Endungen aus Buchstaben), `{kw}` ohne Wort |

Warnungen (gelb): ein unbekannter Schlüssel im Kopf, ein unbekanntes Attribut, ein Stichwort, das eine andere Seite oder
ein Skript des Buchs schon hat.

## Tests und Arbeitsblätter

Eine `.geo.md` mit `type: test` (oder `type: worksheet`) im Kopf ist ein Blatt für sich, kein Teil eines Buchs – alles
andere (Formeln, Abbildungen aus Skripten, Bilder, Kästen) geht wie auf Buchseiten. Leg solche Dateien nicht in einen
Buchordner.

```markdown
---
type: test               # oder worksheet
title: LEK Binärzahlen
subject: Informatik 10   # vor dem Titel
time: 20 min
groups: [A, B, C]        # ohne: ein Blatt ohne Gruppe
seed: 1                  # der Zufall (anderer seed: neue Blätter)
generators: Gen.csproj   # C#-Projekte (siehe unten)
---

## Bits und Bytes {3}

a) 5 Byte sind [[40]] Bit. *(1 P)*

b) $2^3=$ [[$8$|3em]]

## Wahr oder falsch {2}

Die Astronomie untersucht künstliche Satelliten. [[sol: falsch]]

\space 3cm

\page

## Zusatzaufgabe {+1}

Nenne einen Marsmond: [[Phobos]]

\points
```

| Schreibweise | Schülerblatt | Lösung |
|---|---|---|
| Kopf | Fach: Titel, Gruppe und Zeit rechts; Zeilen für Name, Klasse, Datum (Arbeitsblatt: Name, Datum) | dazu rot „Lösung“ |
| `## Titel {3}` | „1. Titel (3 P)“ – Aufgaben werden durchnummeriert und gezählt; auch `{1,5}` | ebenso |
| `## Titel {+1}` | Zusatzaufgabe „Titel (1 P)“: ohne Nummer, nicht in der Summe | ebenso |
| `[[40]]` | eine Linie, so breit, wie die Antwort braucht (mindestens 4em) | die Antwort rot auf der Linie |
| `[[40\|6em]]` | Linie mit fester Breite (em, cm, mm) | ebenso |
| `[[zwei \| *drei \| vier]]` | eine Auswahl zum Unterstreichen (je Gruppe gemischt, `{ordered}` dahinter: wie geschrieben) | die richtigen rot |
| `## Titel {3} @A` | die Aufgabe nur auf den Blättern dieser Gruppen (`@-B`: auf allen außer B; `@-`: auf keinem) | ebenso |
| `[[$8$]]` | Antworten dürfen Formeln und Markdown enthalten | |
| `[[sol: …]]` | nichts | nur die Lösung, rot, ohne Linie (z. B. zu einer Zeichnung) |
| `\space 3cm` | Platz zum Schreiben (Absatz für sich) | ebenso |
| `\page` | neue Seite (am Bildschirm eine gestrichelte Linie) | ebenso |
| `\points` | „___ / 22 Punkte   Note: ___“ und der Notenspiegel: ab wie vielen Punkten welche Note (1 ab 96 %, 2 ab 80 %, 3 ab 60 %, 4 ab 40 %, 5 ab 20 % der Summe, aufgerundet; ohne Zusatzaufgaben) | ebenso |
| `\points +` | ebenso mit Plusnoten (1+ ab 97 %, 1 ab 94 %, 2+ ab 90 %, 2 ab 80 %, 3+ ab 75 %, 3 ab 60 %, 4 ab 40 %, 5 ab 20 %) | ebenso |

Das Schülerblatt enthält keine Lösung, auch nicht versteckt. Die Vorschau in VS Code zeigt zuerst die Lösung der
ersten Gruppe (oben rechts umschaltbar: Gruppe, Lösung/Schüler); ihr Knopf **Export** (oben rechts) speichert die Datei und schreibt Blätter und Lösung als HTML und PDF neben
sie. Die Schrift ist 12 pt mit weitem Zeilenabstand, die Linien sind so breit, wie man von Hand schreibt (eine Linie je
Zeichen der Antwort und 3em dazu, mindestens 5em).

**Ausgabe:** `geoanim sheet lek.geo.md [-o Ordner] [-pdf]` schreibt neben die Datei je Gruppe ein Schülerblatt
(`lek-A.html` …, ohne Gruppen `lek.html`) und eine Datei mit den Lösungen aller Gruppen nacheinander (`lek-Lsg.html`). Das HTML füllt die
ganze Breite des Fensters und wächst mit ihm – für die Tafel; gedruckt wird es A4. Mit `-pdf` dazu `lek-A.pdf` … und
`lek-Lsg.pdf` über ein Typst-Projekt (`lek-typst`, Vorlage `sheet` in `standard.typ`; eine `style.typ` neben der Datei
ändert das Aussehen).

### Gruppen und Zufall

Jede Gruppe hat ihren eigenen Zufall: aus `seed` im Kopf (ohne: 1) und der Gruppe. Dieselbe Gruppe bekommt immer
dieselben Werte – auf dem Schülerblatt und in der Lösung; ein anderer `seed` gibt neue Blätter.

```markdown
::: let
n = random 3..6                     # ganze Zahl von … bis
z = random 9..63 except n, 32       # ohne diese
x = random 0.5..3 step 0.5          # in Schritten
k = 2^n - random(1, 3)              # rechnen; random(a, b) auch in Ausdrücken
planeten = Merkur, Venus, Erde, Mars, Jupiter
p = pick 3 from planeten            # 3 davon, in der Reihenfolge der Liste
mond = alt Phobos | Io | Titan      # je Gruppe eine
:::

{{n}} Byte sind [[{{8*n}}]] Bit.  ({{bin(z)}})₂ = [[{{z}}]]
Ordne nach dem Abstand: {{shuffle(p)}} [[{{p}}]]
Nenne einen Mond {{alt: des Mars | des Jupiter | des Saturn}}: [[{{mond}}]]

::: pick 3
+ Ein Bit kann genau zwei Zustände darstellen.
- Ein Bit besteht immer aus acht Nullen und Einsen.
- Ein Bit und ein Byte sind dasselbe.
+ …
:::
```

| Schreibweise | Bedeutung |
|---|---|
| `::: let` … `:::` | Werte, von oben nach unten; je Zeile `name = …` (`#` Kommentar) |
| `random a..b` | eine ganze Zahl von a bis b (beide dabei); `step 0.5` in Schritten, `except …` ohne diese |
| `pick 3 from liste` | 3 aus der Liste, in deren Reihenfolge (die Lösung einer Ordnungsaufgabe); gemischt zeigen: `shuffle(…)` |
| `shuffle liste` | die Liste gemischt (wo möglich anders als vorher) |
| `alt A \| B \| C` | Gruppe 1 die erste, Gruppe 2 die zweite …; gibt es weniger als Gruppen, wieder von vorn |
| `a, b, c` | eine Liste (Zahlen, Ausdrücke oder Wörter) |
| `{{…}}` im Text | der Wert eines Ausdrucks – auch in Formeln, Lücken und Aufgabentiteln; Zahlen deutsch (2,5) |
| `{{alt: A \| B \| C}}` | die Alternative der Gruppe, direkt im Text |
| `::: pick 3` … `:::` | 3 der Zeilen zufällig, gemischt, jede ein Absatz (`* …`: eine Liste; ohne Zahl: alle – so werden auch Zeilen mit Lücken je Gruppe gemischt); `+ …`/`- …`: wahre und falsche Aussagen (`+`/`-` sagen nur, welche – jede wird ein Absatz, keine Liste), mindestens eine von jeder Art, dahinter die Lücke mit „wahr“/„falsch“ (`::: pick 3 wf`: „w“/„f“; `::: pick 3 ja\|nein`: diese Wörter) |

Ausdrücke: Zahlen, `"Text"`, Namen aus `let`, `+ - * / % ^`, Klammern, `liste[1]` (ab 1) und die Funktionen
`random(a, b)`, `bin(n)` (`bin(n, 8)`: 8 Stellen), `hex(n)`, `round(x)` (`round(x, 2)`), `floor`, `ceil`, `abs`,
`sqrt`, `min`, `max`, `sum`, `count`, `gcd`, `lcm`, `sorted`, `reversed`, `shuffle`, `first`, `last`, `join(liste, " < ")`.
Ein unbekannter Name, ein Fehler im Ausdruck oder `::: pick` mit zu wenigen Zeilen ist ein Fehler mit Zeile; im Text steht
dann „??“. Das alte `{{sol …}}` meldet, wie es jetzt heißt.

In der Vorschau wählt man oben rechts die Gruppe und schaltet zwischen „Lösung“ und „Schüler“ um.

### Generatoren in C#

Was `let` nicht kann, schreibt man in C#: in einem gewöhnlichen Projekt (eine Klassenbibliothek, net8 bis net10), das
alles benutzen darf – NuGet-Pakete, andere Projekte, DLLs. Der Kopf nennt es (relativ zur Datei; mehrere als Liste):

```markdown
---
type: test
groups: [A, B, C]
generators: ../MintTemplates/MintTemplates.csproj
---

::: let
bc = Info10.BinDec        # ein Objekt der Klasse, für die Gruppe
bc.Bits = 5               # Eigenschaften und Felder setzen
:::

a) {{bc.Query}}  = [[{{bc.Answer}}]]
{{bc.GetTask("b", 4)}}    ← darf Markdown liefern, auch Lücken [[…]] und mehrere Zeilen
{{Info10.BinDec.Max(3, 4)}}   ← statische Methoden der Klasse
```

```csharp
namespace Info10;

public class BinDec(Random random, int group, string name)
{
    public int Bits = 4;
    public string Query => …;
    public string GetTask(string label, int bits) => $"{label}) … [[{…}]]";
}
```

- **Das Objekt** macht GeoAnim mit dem öffentlichen Konstruktor, dessen Parameter von den Typen `Random` (der Zufall der
  Gruppe), `int` (die Nummer der Gruppe ab 0: A 0, B 1 …; ohne Gruppen 0) und `string` (ihr Name, „A“) sind – in
  beliebiger Reihenfolge, jeder davon oder keiner. Jedes Objekt hat seinen eigenen Zufall aus dem der Gruppe: dieselbe
  Gruppe bekommt dieselben Werte, auf dem Schülerblatt und in der Lösung.
- **Benutzen:** öffentliche Eigenschaften, Felder und Methoden (`bc.Query`, `bc.GetTask("b", 4)`, auch hintereinander);
  `BinDec` ohne Namensraum, wenn nur eine Klasse so heißt. Argumente: Zahlen, Texte, `wahr`/`falsch`, Listen (für
  Arrays und `List<…>`), Werte aus `let`; optionale Parameter und `params` gehen.
- **Ergebnis:** Texte so, wie sie sind (also auch Markdown und Lücken `[[…]]` – statt des alten `MakeSol(x)` gibt man
  `[[x]]` zurück), Zahlen deutsch, `bool` als „wahr“/„falsch“, Listen als Listen; andere Objekte bleiben Objekte, deren
  Member man wieder benutzen kann.
- **Bauen:** GeoAnim baut das Projekt (`dotnet build`), wenn seine Quellen – oder die der Projekte, die es benutzt – neuer
  sind als das Gebaute, und lädt eine Kopie: das Projekt lässt sich in Visual Studio weiter bauen, die Vorschau nimmt
  dann den neuen Stand. Der erste Aufruf dauert ein paar Sekunden.
- **Fehler:** Baufehler (`BinDec.cs(12): CS1002: ; erwartet`), eine unbekannte Klasse oder ein unbekanntes Member (mit der
  Liste der vorhandenen) und Ausnahmen aus dem C#-Code (`BinDec.GetTask: IndexOutOfRangeException: …`) sind Fehler an der
  Zeile, die sie benutzt.

### Fragen für ILIAS

`type: ilias` macht aus der Datei Fragen für den ILIAS-Import: je Gruppe einen Fragenpool (`lek-U__qti_1.zip`, `lek-A__qti_1.zip` …) und eine HTML-Datei zur Kontrolle mit Antworten und Punkten (`lek-U.html` …). Gruppen, `let`, `alt`, `pick`, `{{…}}` und Generatoren gehen wie bei Tests – so braucht eine Aufgabe, die sich von Gruppe zu Gruppe nur in Zahlen oder Wörtern unterscheidet, nur einmal dazustehen.

````markdown
---
type: ilias
title: LEK Repeat
groups: [U, A, N]          # Übung, Arbeit, Nachschreiben
seed: 482
---

## Datentypen {cs}

::: let
f = alt "3.14" | "2.75" | "1.5"
:::

`"{{f}}`: [[Number | String | *kein gültiger Datentyp]]

Ein Byte hat [[zwei | vier | *acht | 16]]{ordered} Bits, ein Kibibyte [[1024]]{2 chars=6} Byte.

## print-Ausgabe @A N

```eleu
print({{z}});
```

::: pick ja|nein
+ `print({{z}});`
- `print({{x}} + {{y}});`
:::

## Operatoren {6}

::: match
Zuweisung von Werten -> `=`
Test auf Gleichheit -> `==`
-> `===`
:::

## Passwort

::: choice
- Mein Passwort musste zurückgesetzt werden.
+ Mein Passwort musste __nicht__ zurückgesetzt werden.
:::
````

Jede Überschrift `##` ist eine Frage. Ihre Art ergibt sich aus dem, was darin steht:

| Inhalt | Frage in ILIAS | Punkte |
|---|---|---|
| Lücken `[[…]]` | Lückentext (cloze) | die Summe der Lücken (jede 1, `{2}` dahinter: 2); `## Titel {7}` prüft die Summe nur |
| `::: match` | Zuordnung | `## Titel {6}`, verteilt auf die Paare (ohne: 1 je Paar) |
| `::: choice` | Single Choice | `## Titel {2}` für die richtige Antwort (ohne: 1) |

| Schreibweise | Bedeutung |
|---|---|
| `[[*Number \| String \| kein gültiger Datentyp]]` | Auswahllücke: `*` vor den richtigen Antworten (auch mehreren). Die Auswahl wird je Gruppe gemischt (ILIAS mischt sie dazu für jeden Schüler) |
| `[[…]]{ordered}` | die Auswahl bleibt, wie sie dasteht (für Zahlen, „zwei \| vier \| acht“) |
| `[[take();]]` | Freitextlücke; `[[Binärzahl \| Dualzahl]]`: beides richtig |
| `[[…]]{2 chars=16}` | 2 Punkte; höchstens 16 Zeichen (ohne: die längste Antwort und 5 dazu) |
| `## Titel {cs}` | wie ILIAS geschriebene Antworten wertet: `ci` (Groß-/Kleinschreibung egal), `cs` (genau), `l1`, `l2` (1 oder 2 Buchstaben dürfen abweichen; Voreinstellung `l2`) |
| `## Titel @A N` | die Frage nur für diese Gruppen (geht auch auf Tests und Arbeitsblättern); `@-U`: für alle außer U (auch `@-U -B`); `@-`: beiseitegelegt, für keine |
| `::: pick ja\|nein` | wahr/falsch-Reihe: jede Zeile mit der Auswahl „ja \| nein“ (ohne Wörter: „wahr \| falsch“); ohne Zahl alle Zeilen, gemischt |
| `::: match` | Zeilen `Definition -> Term`; `-> Term`: ein Term, der zu nichts passt. ILIAS mischt die Terme; `::: match all`: beide Seiten |
| `::: choice` | `+ richtige Antwort`, `- falsche`; genau eine richtige |

- **Zuordnung n:n:** Gehört ein Term zu zwei Definitionen (oder eine Definition zu zwei Termen), ist das eine Warnung: ILIAS braucht dann Minuspunkte für falsche Paare. Zwei gleich aussehende Terme (`}` zweimal) sind für ILIAS derselbe.
- **Code:** ```` ```eleu ```` wird farbig (die Wörter aus der Eleu-Erweiterung für VS Code), auch in ILIAS. `{{…}}` geht auch in Code-Blöcken.
- **Zahlen in Code:** `{{…}}` schreibt Zahlen deutsch (2,75). Für Code gehören sie in Anführungszeichen: `alt "3.14" | "2.75"` – dann bleiben sie Text, auch mit führender Null (`"0532"`).
- **Bilder:** Formeln, Abbildungen aus Skripten und Bilddateien kommen als Bilder in die Frage.
- **Kontrolle:** In der Vorschau stehen die Fragen immer mit ihren Antworten und den Punkten jeder Lücke; **Export** (oder `geoanim sheet lek.geo.md`) schreibt ZIPs und HTML neben die Datei. Danach in ILIAS importieren und die Fragen dort durchsehen.
- Text vor der ersten Frage (Hilfsmittel …) steht nur in der Kontrolle.
- **Punkte der Gruppen:** Am Ende der Vorschau und jeder Kontrolldatei steht eine Tabelle: jede Frage mit ihrer Art und ihren Punkten in jeder Gruppe („–“: nicht in dieser Gruppe), die Summen und die Punkte je Art. Sind die Summen verschieden, steht das rot darunter.

## Beispiel

`10_Punkte.geo.md` neben `10_Punkte.geo`:

```markdown
# Punkte und ihre Lage

![](#2)

Ein **Punkt**{kw=Punkt/e/en} beschreibt eine genaue Lage. Er hat keine Ausdehnung.

## Gleiche und verschiedene Punkte

![](#verschieden..gleich)

Die Punkte $M$ und $Q$ liegen an verschiedenen Orten: $M != Q$.
Fallen $M$ und $Q$ zusammen, schreibt man $M = Q$.
```

mit den Schritten im Skript:

```
step Verschiedene Punkte #verschieden
…
step Gleiche Punkte #gleich
```
