# GeoAnim
          
Animierte geometrische Konstruktionen aus einer kleinen Skriptsprache (oder aus C#). Eine Konstruktion besteht aus Schritten mit Beschreibung; Punkte erscheinen, Strecken wachsen, Kreise werden wie mit dem Zirkel gezogen, Punkte bewegen sich, und alles, was von ihnen abhängt, folgt exakt.

- **`geoanim serve datei.geo`** zeigt die Animation im Browser und aktualisiert sie bei jedem Speichern der Datei. Der Player bleibt dabei am aktuellen Schritt; Fehler erscheinen mit Zeile und Spalte über dem Bild. Über dem Bild laufen unten in der Steuerleiste die Koordinaten unter der Maus mit; Umschalt+Klick kopiert sie im Skriptformat (`2.35 -1.2`) in die Zwischenablage. (Exportierte Dateien zeigen das nicht.)
- **`geoanim export datei.geo`** schreibt **eine** HTML-Datei (rund 100 KB), die ohne Server läuft und weitergegeben werden kann.
- **`geoanim exportdir ordner`** macht das für jede `.geo`-Datei im Ordner und allen Unterordnern (die HTML-Datei jeweils daneben); ein fehlerhaftes Skript wird gemeldet, die übrigen werden trotzdem exportiert.
               
Gezeichnet wird mit SVG, also in jeder Größe scharf. Formeln stehen in AsciiMath zwischen `$…$` und werden beim Übersetzen mit MathJax zu SVG-Pfaden; der Export enthält nur die Zeichen, die vorkommen. Die Schrift ist STIX Two (Text und Formeln aus einer Familie). Abhängigkeiten: siehe [Lizenz](#lizenz).

## Schnellstart

```
dotnet build GeoAnim.slnx
dotnet run --project GeoAnim -- serve Samples/perpendicular-bisector.geo
dotnet run --project GeoAnim -- export Samples/circumcircle.geo
```

In Visual Studio: Startprojekt `GeoAnim`; die Startprofile „serve sample“ und „export sample“ (`GeoAnim/Properties/launchSettings.json`) arbeiten im Repository-Ordner, dort lässt sich die Datei austauschen. Alle `.geo`-Dateien aus `Samples` samt Unterordnern erscheinen im Projekt `GeoAnim` unter „Samples“, neue Dateien von selbst. Optionen: `serve … --port 8080 --no-browser`, `export … -o ziel.html`.

**Player:** Abspielen/Pause (großer runder Knopf, Leertaste, Klick ins Bild), vorheriger/nächster Schritt (← →), Anfang/Ende (Pos1/Ende), Schritt wiederholen (R), Schieberegler mit Marken an den Schrittenden. Die Animation beginnt angehalten beim Startbild und läuft beim Abspielen bis zum Ende durch; „nach jedem Schritt anhalten“ lässt sich zuschalten (S). Geschwindigkeit ¼× bis 4×: Klick auf die Anzeige öffnet die Auswahl, außerdem − / + oder das Mausrad über der Anzeige.

## Beispiel

```
title Mittelsenkrechte
view x -1 .. 11 y -1 .. 7

point A = 3 3
point B = 7 3

step Wir zeichnen die Strecke AB.
  segment AB = A B

step Kreise um A und B mit Radius AB.
  circle k1 = center A through B color gray
  circle k2 = center B through A color gray

step Die Gerade durch die Schnittpunkte ist die Mittelsenkrechte.
  point S T = intersect k1 k2 color red
  line m = S T color red width 3
```

Weitere Beispiele in `Samples/`: Umkreis eines Dreiecks (mit bewegtem Eckpunkt) und Satz des Thales (Punkt läuft auf dem Kreis).

## Die Sprache

Eine Anweisung pro Zeile; Einrückung ist egal. Alle Angaben werden nur durch Leerzeichen getrennt, ohne Kommas und Klammern (Koordinaten: `2 3`). `#` beginnt einen Kommentar (außer `#rgb` / `#rrggbb` als Farbe). Zahlen mit Punkt (`2.5`, `-0.5`). Hinter `title` und `step` ist der Rest der Zeile der Text, so wie er dasteht, ohne Anführungszeichen (auch `#` gehört dann dazu). Andere Texte (`text`, `label`) stehen in doppelten Anführungszeichen (`\"` für ein Anführungszeichen darin), weil danach noch Optionen folgen können. Namen bestehen aus Buchstaben, Ziffern und `_`, gefolgt von beliebig vielen `'` (`A'`, `P_1`); Groß-/Kleinschreibung zählt. Schlüsselwörter (`point`, `to`, `x`, …) sind als Namen nicht erlaubt.

**Lange Texte und Auszeichnungen:** Eine Zeile, die mit `|` beginnt, setzt den Text der `title`- oder `step`-Zeile davor fort (mit einem Leerzeichen angehängt):

```
step Wir zeichnen um $A$ einen Kreis mit dem Radius $|AB|$
| und erhalten so alle Punkte, die von $A$ so weit entfernt sind wie $B$.
```

Titel und Schrittbeschreibungen verstehen etwas Markdown: `**fett**`, `*kursiv*` oder `_kursiv_`, `` `Code` ``, Links `[Satz 1](Prop1.html)` und `<Prop1.html>`; Formeln dürfen darin stehen. `\*` usw. hält ein Zeichen fest. In einer exportierten Datei öffnet ein Link auf eine andere Animation diese im selben Fenster (die HTML-Dateien eines Ordners werden so zu einem verlinkten Buch), Links auf andere Seiten öffnen ein neues Fenster; bei `geoanim serve` öffnen alle Links ein neues Fenster, in der VS-Code-Vorschau öffnet `Prop1.html` die Datei `Prop1.geo` im Editor.

**Formeln:** In allen Texten (`title`, `step`, `text`, `label`) steht zwischen zwei `$` eine Formel in [AsciiMath](https://asciimath.org): `step Kreis um $A$ mit Radius $|AB|$`, `text t = "$alpha + beta = 180^@$" at 2 3`, `$bar(AB)$`, `$vec(BD)$`, `$NN sub RR$`. `\$` ist ein Dollarzeichen. Ein `$` ohne Gegenstück ist ein Fehler mit Zeile und Spalte. Punkte sind standardmäßig mit ihrem Namen als Formel beschriftet: kursiv, `P_1` mit tiefgestellter 1, `A'` mit Strich.

Koordinaten sind mathematisch: die y-Achse zeigt nach oben. Der Maßstab ist in beiden Richtungen gleich; das Bild hat die Form des Bereichs aus `view`.

### Einstellungen (vor dem ersten Schritt)

| Anweisung | Bedeutung |
|---|---|
| `title Mittelsenkrechte` | Überschrift über dem Bild (Rest der Zeile) |
| `view x -1 .. 11 y -1 .. 7` | sichtbarer Bereich (Standard wie hier); vor dem ersten Objekt |
| `background white` | Hintergrundfarbe |
| `grid` / `grid 0.5` | Gitter mit Abstand 1 bzw. 0.5 |
| `axes` | Koordinatenachsen mit Zahlen |
| `default point size 4 labelsize 20` | Voreinstellung: Optionen, die für alle folgenden Objekte der genannten Arten zuerst gelten (siehe unten) |
| `include styles.geo` | bindet eine andere Datei an dieser Stelle ein (siehe unten) |
| `input point A = -3 0` / `output C` | Eingaben und Ergebnisse, wenn die Datei mit `use` benutzt wird (siehe unten) |
| `speed 0.5` | Tempo der ganzen Animation: 1 wie angegeben, 0.5 halb so schnell (alles dauert doppelt so lange), 2 doppelt so schnell |

### Objekte

| Anweisung | Objekt |
|---|---|
| `point A = 2 3` | freier Punkt (kann sich bewegen) |
| `point M = midpoint A B` | Mittelpunkt |
| `point X = copy P` | freier Punkt an der Stelle, an der P gerade ist (z. B. um eine Kopie zu bewegen, ohne die Konstruktion zu ändern) |
| `point S T = intersect k1 k2` | Schnittpunkte (ein oder zwei Namen) |
| `point P = on k at 60` | Punkt auf einem Kreis, Winkel in Grad (gegen den Uhrzeigersinn ab der x-Richtung) |
| `point Q = on AB at 0.25` | Punkt auf Strecke/Gerade/Strahl: A + 0.25·(B − A) |
| `segment s = A B` | Strecke (Kurzform `segment A B` heißt `AB`) |
| `line g = A B` | Gerade (am Bildrand abgeschnitten) |
| `ray r = A B` | Strahl ab A durch B |
| `circle k = center M through P` | Kreis um M durch P |
| `circle k = center M radius 2.5` | Kreis mit festem Radius |
| `polygon D = A B C` | gefülltes Vieleck (mindestens 3 Punkte) |
| `text t = "Hallo" at 5 6` | Text an festen Koordinaten (zentriert) |
| `text t = "90°" at P` | Text neben einem Punkt, wandert mit ihm |

**Startbild und Schritte:** Objekte vor dem ersten `step` gehören zum Startbild und sind sofort da. In einem Schritt definierte Objekte werden dort nacheinander gezeichnet: Punkte erscheinen, Strecken wachsen von A nach B, Geraden von der Mitte nach außen, Kreise werden ab dem Punkt `through` (sonst ab 0°) gezogen, Vielecke und Texte blenden ein. Mit der Option `hidden` wird ein Objekt nur definiert (z. B. Hilfskreise) und erst mit `show` gezeigt – oder nie.

**Reihenfolge der Schnittpunkte:** bei Gerade/Strecke/Strahl und Kreis in Richtung A → B; bei zwei Kreisen zuerst der Punkt links von der Richtung vom ersten zum zweiten Mittelpunkt. Bei nur einem Namen gilt der erste Schnittpunkt, den es gibt (bei Strecke oder Strahl kann der andere außerhalb liegen). Die Reihenfolge bleibt erhalten, wenn sich Punkte bewegen. Ein Schnittpunkt, der beim Bewegen verschwindet (Kreise trennen sich), wird ausgeblendet, samt allem, was von ihm abhängt.

### Schritte und Aktionen

| Anweisung | Bedeutung |
|---|---|
| `step Beschreibung bis zum Zeilenende` | neuer Schritt; der Text erscheint unter dem Bild |
| `show A k1` | zeigt Objekte (zeichnet versteckte, blendet ausgeblendete ein) |
| `hide k1 k2` | blendet Objekte aus |
| `move A to 4 5` | bewegt einen freien Punkt |
| `move P to 150` | bewegt einen Punkt auf seinem Kreis (Winkel) bzw. seiner Strecke (Position) |
| `move C1 to P` | bewegt einen freien Punkt dorthin, wo der Punkt P in diesem Moment ist (P kann konstruiert sein; bewegt sich P später, bleibt C1, wo er ist) |
| `pause 300` | wartet (Standard 500 ms) |
| `together` … `end` | alles dazwischen beginnt gleichzeitig; der Block dauert so lange wie die längste Aktion |
| `together duration 2500` … `end` | ebenso, aber der Block dauert genau 2,5 s: alle Aktionen darin werden im selben Verhältnis gestreckt oder gestaucht |

Aktionen laufen nacheinander. Ein Schritt ohne Aktion dauert 500 ms. Bewegungen werden in Abständen von 40 ms exakt vorausberechnet, damit Kreise durch bewegte Punkte, Schnittpunkte usw. auch zwischendurch stimmen.

### Optionen

Hinter einer Anweisung, in beliebiger Reihenfolge:

| Option | für | Bedeutung |
|---|---|---|
| `color red` / `color #1565c0` | alle Objekte | Farbe |
| `width 3` | Strecke, Gerade, Strahl, Kreis, Vieleck | Linienbreite in Punkt |
| `size 6` | Punkt (Radius), Text (Schriftgröße) | Größe in Punkt |
| `labelsize 20` | Punkt, Strecke, Strahl, Gerade | Schriftgröße der Beschriftung in Punkt (Standard 24) |
| `label "$A'$"` / `nolabel` | Punkt | Beschriftung (Standard: der Name als Formel) bzw. keine |
| `label "$a$"` | Strecke, Strahl, Gerade | Beschriftung, die mit dem Objekt wandert; standardmäßig in der Mitte, knapp rechts von der Richtung A → B (beim gegen den Uhrzeigersinn benannten Dreieck also außen) |
| `labelpos 0.3` | Strecke, Strahl, Gerade | Lage der Beschriftung bei A + 0.3·(B − A) (Standard 0.5); beim Strahl auch über 1, bei der Geraden beliebig |
| `offset 0.3 -0.5` | Punkt (Beschriftung), Text, Strecke/Strahl/Gerade (Beschriftung) | Lage in Koordinateneinheiten relativ zum Punkt bzw. zur Stelle `labelpos`: dort beginnt die Grundlinie des Textes (bei Text an festen Koordinaten und bei Linien: ihre Mitte); ohne `offset` steht die Beschriftung knapp daneben |
| `fill lightblue` | Vieleck | Füllfarbe (Standard: die Linienfarbe) |
| `opacity 0.3` | Vieleck | Deckkraft der Füllung (Standard 0.2) |
| `hidden` | Objekte | nur definieren, nicht zeigen |
| `duration 1200` | Objekte, `show`, `hide`, `move` | Dauer in Millisekunden |
| `linear`, `ease` (= `easeinout`), `easein`, `easeout` | Objekte, `show`, `hide`, `move` | Verlauf der Animation |

Ein Punkt (pt) ist ein Pixel bei 1280 Pixel Bildbreite; Linien und Schriften wachsen mit dem Player.

**Vars – Optionen mit Namen:** `var name optionen…` gibt einer Gruppe von Optionen einen Namen; danach steht der Name überall, wo Optionen stehen können, für diese Optionen.

```
var hilfs color gray width 1.5
var rot hilfs color red size 6 offset 0.2 0.2
circle k1 = center A through B hilfs
point S T = intersect k1 k2 rot
line m = S T rot width 3
```

Eine Var gilt ab ihrer Zeile (eine spätere `var` mit demselben Namen ersetzt sie für die folgenden Zeilen) und darf andere Vars enthalten. Optionen gelten der Reihe nach, die spätere gewinnt: `rot color blue` ist blau mit allem Übrigen aus `rot`. Was aus einer Var für ein Objekt nicht passt, wird übergangen (ein Punkt mit `hilfs` nimmt die Farbe, ignoriert `width`); direkt geschriebene Optionen, die nicht passen, bleiben ein Fehler. Vars und Objekte haben verschiedene Namen.

**Voreinstellungen:** `default arten… optionen…` legt Optionen für alle folgenden Objekte fest, damit nicht jedes einzelne sie braucht. Arten: `point`, `segment`, `line`, `ray`, `circle`, `polygon`, `text`, `show`, `hide`, `move`; ohne Art gilt die Voreinstellung für alle.

```
default point size 4 labelsize 20
default segment line ray circle polygon width 2
default text size 22
default move duration 1500
default color navy
```

Es gilt der Reihe nach: Voreinstellungen ohne Art, dann die für die Art, dann die Optionen des Objekts selbst – die spätere gewinnt. Eine weitere `default`-Zeile ergänzt die bisherigen für die folgenden Objekte. Wie bei einer Var wird übergangen, was nicht passt (`default width 2` betrifft keine Punkte). Vorsicht bei `size` ohne Art: das ist bei Punkten der Radius, bei Texten die Schriftgröße. Vars sind auch hier möglich: `default circle hilfs`.

**Dateien einbinden:** `include pfad` (Rest der Zeile, relativ zur einbindenden Datei) setzt den Inhalt einer anderen Datei an dieser Stelle ein, als stünde er dort – typisch für gemeinsame Voreinstellungen und Vars (`include ../styles.geo`). Eingebundene Dateien dürfen selbst einbinden, aber nicht im Kreis. Fehler darin werden mit Datei und Zeile gemeldet (`styles.geo, line 3, column 1: …`), und `geoanim serve` übersetzt auch neu, wenn eine eingebundene Datei gespeichert wird. Hinweis: Tests und `exportdir` behandeln jede `.geo`-Datei als eigenes Skript; eine Datei nur mit Voreinstellungen und Vars ist das auch (mit leerer Animation).

**Konstruktionen wiederverwenden (`use`):** Eine Datei kann eine andere wie eine Funktion benutzen. Die benutzte Datei nennt ihre Eingaben und Ergebnisse:

```
# Book1/Prop1.geo – gleichseitiges Dreieck über AB
input point A = -3 0      # der Wert gilt nur, wenn die Datei allein läuft
input point B = 3 0
output C
…die Konstruktion wie gewohnt, mit Schritten…
```

```
# Book1/Prop2.geo
point D = use Prop1.geo A B draw offset 0 0.2
```

- Läuft die Datei allein, ist `input` eine gewöhnliche Definition und `output` wird übergangen – sie animiert sich wie immer.
- Beim `use` stehen die Objekte des Aufrufers (in der Reihenfolge der `input`-Zeilen, von passender Art) für die Eingaben; die Ergebnisse bekommen die Namen links vom `=`. Optionen beim `use` gelten für die Ergebnisse, zusätzlich zu den Voreinstellungen des Aufrufers.
- Welche Ergebnisse man abholt, sagt die Art vorne. Bei `output L res` (ein Punkt, eine Strecke):

  ```
  point D = use Prop2.geo A B C        # die Ausgaben der Art point der Reihe nach: L
  segment s = use Prop2.geo A B C      # die Ausgaben der Art segment: res
  D s = use Prop2.geo A B C            # ohne Art: alle Ausgaben der Reihe nach
  _ s = use Prop2.geo A B C            # _ überspringt eine Ausgabe
  ```

  Hinten dürfen Namen fehlen. Ohne Art gelten Optionen nur für die Ergebnisse, zu denen sie passen. Nicht abgeholte Ergebnisse werden trotzdem konstruiert, zählen aber zu den Hilfsobjekten.
- Alle anderen Objekte der Datei bleiben in ihr: Sie bekommen intern eigene Namen und sind für den Aufrufer unsichtbar, Namen können also nicht kollidieren. Die Abhängigkeiten sind echt: Bewegt sich später eine Eingabe, folgen die Ergebnisse.
- Titel, `view`, `grid`, `axes`, `background` und `speed` der Datei gelten nur, wenn sie allein läuft. Ihre Voreinstellungen, Vars und `include`s gelten für sie selbst.
- Was man sieht:
  - `use datei …` (Standard): nur die Ergebnisse, gezeichnet wie gewöhnliche Objekte; die Hilfskonstruktion bleibt unsichtbar.
  - `use datei … draw`: die Konstruktion der Datei wird im aktuellen Schritt gezeichnet (ohne ihre Schrittbeschreibungen und Pausen); am Ende blenden die Hilfsobjekte aus, die Ergebnisse bleiben.
  - `use datei … steps`: wie `draw`, aber die Schritte der Datei werden mit ihren Beschreibungen zu eigenen Schritten.
- Eine benutzte Datei darf ihre Eingaben nicht bewegen, ein- oder ausblenden. Sie darf selbst andere Dateien benutzen (nicht im Kreis); innerhalb eines `use` ohne `draw`/`steps` zeigen auch diese nur ihr Ergebnis. Fehler in der benutzten Datei werden am `use` gemeldet, mit Datei und Zeile: `Line 12, column 15: In Prop1.geo, line 22, column 1: …`.

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

## VS Code

`editors/vscode` ist eine Erweiterung für VS Code: Syntaxhervorhebung (Befehle, Hilfswörter, Optionen, Zahlen, Farben,
neue Objektnamen, Vars, Texte, Formeln, Dateipfade, Kommentare), eine Live-Vorschau neben dem Editor, die schon beim
Tippen aktualisiert und dem Schritt unter dem Cursor folgt, und Fehler als rote Unterstreichung. Dahinter steht
`geoanim daemon` (JSON-Zeilen über Standardein- und -ausgabe). Einrichten: siehe `editors/vscode/README.md`.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `GeoAnim.Core` | Geometrie (Punkte, Strecken, Kreise, Schnittpunkte, Abhängigkeiten), Zeitleiste (`Construction`: Schritte und Aktionen), Animationsdaten (`AnimationData.ToJson`) |
| `GeoAnim.Formulas` | Formeln: `MathText` zerlegt Texte in Text und `$…$`; `MathRenderer` lässt MathJax (AsciiMath → SVG, Schrift STIX2) in der JavaScript-Engine Jint laufen (Start unter einer Sekunde, dann wenige Millisekunden je Formel). MathJax und die Zeichendaten liegen in `mathjax/` und `mathjax-stix2-font/` als eingebettete Ressourcen |
| `GeoAnim.Script` | Skriptsprache: Lexer, Parser, Übersetzung in eine `Construction`; Fehler als `ScriptError` mit Zeile/Spalte |
| `GeoAnim.Viewer` | Player (`view/`: `player.html`, `player.js`, `player.css`, Schrift) als eingebettete Ressourcen; `HtmlExporter` für die eigenständige Datei |
| `GeoAnim` | Kommandozeile `geoanim serve` (EmbedIO, Dateibeobachtung, `/animation.json`) und `geoanim export` |
| `GeoAnim.Tests` | xUnit: Geometrie, Zeitleiste, Sprache, Formeln, Export, Server, alle Beispiele in `Samples` |

**Animationsdaten:** Ein Objekt besteht aus Grundelementen (`dot`, `line`, `arc`, `polygon`, `text`) mit je einer Spur pro Eigenschaft: Schlüssel `[zeit, wert]` oder `[zeit, wert, verlauf]` (Zeit in ms; Verlauf `s` = Sprung, `i`/`o`/`io` = ease in/out/in-out, sonst linear). Zwischen zwei Schlüsseln interpoliert der Player mit dem Verlauf des späteren; vor dem ersten Schlüssel ist ein Element nicht da. Beim Schreiben fallen Schlüssel weg, die auf der Geraden zwischen ihren Nachbarn liegen (Koordinaten auf einen Viertelpunkt genau, sonst auf ein Tausendstel) – vor allem die alle 40 ms berechneten Werte während Bewegungen. Dazu kommen Titel, Bereich, Hintergrund, Gitter, Achsen, Gesamtdauer, die Schritte (`caption`, `start`, `end`) und die Formeln aller Texte (`math`: AsciiMath → `box` = viewBox in Tausendstel em mit der Grundlinie bei y = 0, `svg` = Pfade in `currentColor`).

## Lizenz

GeoAnim steht unter der MIT-Lizenz (`LICENSE`). Fremde Bestandteile:

| Bestandteil | Lizenz | wo |
|---|---|---|
| Schrift STIX Two Text (Fontsource, lateinische Zeichen) | SIL Open Font License 1.1 | `GeoAnim.Viewer/view/fonts/`, steckt in jedem Export |
| MathJax 4 (Auszug) und die Zeichendaten von MathJax STIX2 (aus den STIX-Two-Schriften, OFL 1.1) | Apache 2.0 | `GeoAnim.Formulas/mathjax*/`; im Export stehen nur die erzeugten Pfade |
| Jint | BSD 2-Clause | NuGet-Paket |
| EmbedIO | MIT | NuGet-Paket |
| xUnit (nur Tests) | Apache 2.0 | NuGet-Paket |

## Ideen für später

- Winkelmarkierungen, Kreisbögen als eigene Objekte, gestrichelte Linien, Pfeile
- Farbe oder Breite eines Objekts in einem späteren Schritt ändern (`set k color red`)
- Export als Video oder animiertes SVG
