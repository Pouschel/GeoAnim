# GeoAnim

Animierte geometrische Konstruktionen aus einer kleinen Skriptsprache (oder aus C#). Eine Konstruktion besteht aus Schritten mit Beschreibung; Punkte erscheinen, Strecken wachsen, Kreise werden wie mit dem Zirkel gezogen, Punkte bewegen sich, und alles, was von ihnen abhängt, folgt exakt.

- **`geoanim serve datei.geo`** zeigt die Animation im Browser und aktualisiert sie bei jedem Speichern der Datei. Der Player bleibt dabei am aktuellen Schritt; Fehler erscheinen mit Zeile und Spalte über dem Bild.
- **`geoanim export datei.geo`** schreibt **eine** HTML-Datei (rund 100 KB), die ohne Server läuft und weitergegeben werden kann.

Gezeichnet wird mit SVG, also in jeder Größe scharf. Es gibt keine Abhängigkeiten außer der Schrift Lato (SIL Open Font License, `GeoAnim.Viewer/view/fonts/LICENSE-lato.txt`) und EmbedIO für den Webserver.

## Schnellstart

```
dotnet build GeoAnim.slnx
dotnet run --project GeoAnim -- serve Samples/perpendicular-bisector.geo
dotnet run --project GeoAnim -- export Samples/circumcircle.geo
```

In Visual Studio: Startprojekt `GeoAnim`; die Startprofile „serve sample“ und „export sample“ (`GeoAnim/Properties/launchSettings.json`) arbeiten im Repository-Ordner, dort lässt sich die Datei austauschen. Optionen: `serve … --port 8080 --no-browser`, `export … -o ziel.html`.

**Player:** Abspielen/Pause (Leertaste, Klick ins Bild), vorheriger/nächster Schritt (← →), Anfang/Ende (Pos1/Ende), Schritt wiederholen (R), „nach jedem Schritt anhalten“ an/aus (S), Geschwindigkeit ¼× bis 4×, Schieberegler mit Marken an den Schrittenden. Die Animation beginnt angehalten beim Startbild.

## Beispiel

```
title "Mittelsenkrechte"
view x -1 .. 11, y -1 .. 7

point A = (3, 3)
point B = (7, 3)

step "Wir zeichnen die Strecke AB."
  segment AB = A B

step "Kreise um A und B mit Radius AB."
  circle k1 = center A through B color gray
  circle k2 = center B through A color gray

step "Die Gerade durch die Schnittpunkte ist die Mittelsenkrechte."
  point S, T = intersect k1 k2 color red
  line m = S T color red width 3
```

Weitere Beispiele in `Samples/`: Umkreis eines Dreiecks (mit bewegtem Eckpunkt) und Satz des Thales (Punkt läuft auf dem Kreis).

## Die Sprache

Eine Anweisung pro Zeile; Einrückung ist egal. `#` beginnt einen Kommentar (außer `#rgb` / `#rrggbb` als Farbe). Zahlen mit Punkt (`2.5`, `-0.5`), Texte in doppelten Anführungszeichen (`\"` für ein Anführungszeichen darin). Namen bestehen aus Buchstaben, Ziffern und `_`, gefolgt von beliebig vielen `'` (`A'`, `P_1`); Groß-/Kleinschreibung zählt. Schlüsselwörter (`point`, `to`, `x`, …) sind als Namen nicht erlaubt.

Koordinaten sind mathematisch: die y-Achse zeigt nach oben. Der Maßstab ist in beiden Richtungen gleich; das Bild hat die Form des Bereichs aus `view`.

### Einstellungen (vor dem ersten Schritt)

| Anweisung | Bedeutung |
|---|---|
| `title "…"` | Überschrift über dem Bild |
| `view x -1 .. 11, y -1 .. 7` | sichtbarer Bereich (Standard wie hier); vor dem ersten Objekt |
| `background white` | Hintergrundfarbe |
| `grid` / `grid 0.5` | Gitter mit Abstand 1 bzw. 0.5 |
| `axes` | Koordinatenachsen mit Zahlen |

### Objekte

| Anweisung | Objekt |
|---|---|
| `point A = (2, 3)` | freier Punkt (kann sich bewegen) |
| `point M = midpoint A B` | Mittelpunkt |
| `point S, T = intersect k1 k2` | Schnittpunkte (ein oder zwei Namen) |
| `point P = on k at 60` | Punkt auf einem Kreis, Winkel in Grad (gegen den Uhrzeigersinn ab der x-Richtung) |
| `point Q = on AB at 0.25` | Punkt auf Strecke/Gerade/Strahl: A + 0.25·(B − A) |
| `segment s = A B` | Strecke (Kurzform `segment A B` heißt `AB`) |
| `line g = A B` | Gerade (am Bildrand abgeschnitten) |
| `ray r = A B` | Strahl ab A durch B |
| `circle k = center M through P` | Kreis um M durch P |
| `circle k = center M radius 2.5` | Kreis mit festem Radius |
| `polygon D = A B C` | gefülltes Vieleck (mindestens 3 Punkte) |
| `text t = "Hallo" at (5, 6)` | Text an festen Koordinaten (zentriert) |
| `text t = "90°" at P` | Text neben einem Punkt, wandert mit ihm |

**Startbild und Schritte:** Objekte vor dem ersten `step` gehören zum Startbild und sind sofort da. In einem Schritt definierte Objekte werden dort nacheinander gezeichnet: Punkte erscheinen, Strecken wachsen von A nach B, Geraden von der Mitte nach außen, Kreise werden ab dem Punkt `through` (sonst ab 0°) gezogen, Vielecke und Texte blenden ein. Mit der Option `hidden` wird ein Objekt nur definiert (z. B. Hilfskreise) und erst mit `show` gezeigt – oder nie.

**Reihenfolge der Schnittpunkte:** bei Gerade/Strecke/Strahl und Kreis in Richtung A → B; bei zwei Kreisen zuerst der Punkt links von der Richtung vom ersten zum zweiten Mittelpunkt. Die Reihenfolge bleibt erhalten, wenn sich Punkte bewegen. Ein Schnittpunkt, der beim Bewegen verschwindet (Kreise trennen sich), wird ausgeblendet, samt allem, was von ihm abhängt.

### Schritte und Aktionen

| Anweisung | Bedeutung |
|---|---|
| `step "Beschreibung"` | neuer Schritt; der Text erscheint unter dem Bild |
| `show A k1` | zeigt Objekte (zeichnet versteckte, blendet ausgeblendete ein) |
| `hide k1 k2` | blendet Objekte aus |
| `move A to (4, 5)` | bewegt einen freien Punkt |
| `move P to 150` | bewegt einen Punkt auf seinem Kreis (Winkel) bzw. seiner Strecke (Position) |
| `pause 300` | wartet (Standard 500 ms) |
| `together` … `end` | alles dazwischen beginnt gleichzeitig; der Block dauert so lange wie die längste Aktion |

Aktionen laufen nacheinander. Ein Schritt ohne Aktion dauert 500 ms. Bewegungen werden in Abständen von 40 ms exakt vorausberechnet, damit Kreise durch bewegte Punkte, Schnittpunkte usw. auch zwischendurch stimmen.

### Optionen

Hinter einer Anweisung, in beliebiger Reihenfolge:

| Option | für | Bedeutung |
|---|---|---|
| `color red` / `color #1565c0` | alle Objekte | Farbe |
| `width 3` | Strecke, Gerade, Strahl, Kreis, Vieleck | Linienbreite in Punkt |
| `size 6` | Punkt (Radius), Text (Schriftgröße) | Größe in Punkt |
| `label "A'"` / `nolabel` | Punkt | Beschriftung (Standard: der Name) bzw. keine |
| `offset (8, -20)` | Punkt (Beschriftung), Text | Verschiebung in Punkt, x nach rechts, y nach oben |
| `fill lightblue` | Vieleck | Füllfarbe (Standard: die Linienfarbe) |
| `opacity 0.3` | Vieleck | Deckkraft der Füllung (Standard 0.2) |
| `hidden` | Objekte | nur definieren, nicht zeigen |
| `duration 1200` | Objekte, `show`, `hide`, `move` | Dauer in Millisekunden |
| `linear`, `ease` (= `easeinout`), `easein`, `easeout` | Objekte, `show`, `hide`, `move` | Verlauf der Animation |

Ein Punkt (pt) ist ein Pixel bei 1280 Pixel Bildbreite; Linien und Schriften wachsen mit dem Player.

**Farben:** `black`, `white`, `gray`, `lightgray`, `red`, `darkred`, `green`, `lightgreen`, `blue`, `lightblue`, `navy`, `orange`, `yellow`, `purple`, `violet`, `pink`, `brown`, `teal`, `cyan`, `magenta` oder `#rgb` / `#rrggbb`.

**Standardwerte:** Punkte blau, Radius 5 pt, erscheinen in 400 ms; Linien schwarz, 2,5 pt, 800 ms; Kreise 1200 ms; Vielecke 600 ms; Texte 26 pt, 400 ms; `show`/`hide` 400 ms; `move` 1000 ms mit `ease`.

## C#

Dieselben Möglichkeiten ohne Skript (`GeoAnim.Core`); das Skript wird genau in diese Aufrufe übersetzt:

```csharp
var c = new Construction { Title = "Mittelsenkrechte" };
GeoPoint a = c.Point("A", 3, 3);
GeoPoint b = c.Point("B", 7, 3);
c.Step("Wir zeichnen die Strecke AB.");
c.Segment("AB", a, b);
c.Step("Kreise um A und B.");
GeoCircle k1 = c.Circle("k1", a, b, new Options { Color = Color.Parse("gray") });
GeoCircle k2 = c.Circle("k2", b, a, new Options { Color = Color.Parse("gray") });
c.Step("Die Mittelsenkrechte.");
var (s, t) = c.Intersect("S", "T", k1, k2);
c.Line("m", s, t, new Options { Color = Color.Parse("red") });
File.WriteAllText("mittelsenkrechte.html", HtmlExporter.Export(c.Build()));
```

## Aufbau

| Projekt | Inhalt |
|---|---|
| `GeoAnim.Core` | Geometrie (Punkte, Strecken, Kreise, Schnittpunkte, Abhängigkeiten), Zeitleiste (`Construction`: Schritte und Aktionen), Animationsdaten (`AnimationData.ToJson`) |
| `GeoAnim.Script` | Skriptsprache: Lexer, Parser, Übersetzung in eine `Construction`; Fehler als `ScriptError` mit Zeile/Spalte |
| `GeoAnim.Viewer` | Player (`view/`: `player.html`, `player.js`, `player.css`, Schrift) als eingebettete Ressourcen; `HtmlExporter` für die eigenständige Datei |
| `GeoAnim` | Kommandozeile `geoanim serve` (EmbedIO, Dateibeobachtung, `/animation.json`) und `geoanim export` |
| `GeoAnim.Tests` | xUnit: Geometrie, Zeitleiste, Sprache, Export, Server |

**Animationsdaten:** Ein Objekt besteht aus Grundelementen (`dot`, `line`, `arc`, `polygon`, `text`) mit je einer Spur pro Eigenschaft: Schlüssel `[zeit, wert]` oder `[zeit, wert, verlauf]` (Zeit in ms; Verlauf `s` = Sprung, `i`/`o`/`io` = ease in/out/in-out, sonst linear). Zwischen zwei Schlüsseln interpoliert der Player mit dem Verlauf des späteren; vor dem ersten Schlüssel ist ein Element nicht da. Dazu kommen Titel, Bereich, Hintergrund, Gitter, Achsen, Gesamtdauer und die Schritte (`caption`, `start`, `end`).

## Ideen für später

- Formeln in Beschreibungen und im Bild mit MathJax (AsciiMath als Eingabe, SVG als Ausgabe)
- Winkelmarkierungen, Kreisbögen als eigene Objekte, gestrichelte Linien, Pfeile
- Farbe oder Breite eines Objekts in einem späteren Schritt ändern (`set k color red`)
- Export als Video oder animiertes SVG
