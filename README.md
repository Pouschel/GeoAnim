# GeoAnim
          
Animierte geometrische Konstruktionen aus einer kleinen Skriptsprache (oder aus C#). Eine Konstruktion besteht aus Schritten mit Beschreibung; Punkte erscheinen, Strecken wachsen, Kreise werden wie mit dem Zirkel gezogen, Punkte bewegen sich, und alles, was von ihnen abhängt, folgt exakt.

- **`geoanim serve datei.geo`** zeigt die Animation im Browser und aktualisiert sie bei jedem Speichern der Datei. Der Player bleibt dabei am aktuellen Schritt; Fehler erscheinen mit Zeile und Spalte über dem Bild. Über dem Bild laufen unten in der Steuerleiste die Koordinaten unter der Maus mit; Umschalt+Klick kopiert sie im Skriptformat (`2.35 -1.2`) in die Zwischenablage. (Exportierte Dateien zeigen das nicht.)
- **`geoanim export datei.geo`** schreibt **eine** HTML-Datei (rund 100 KB), die ohne Server läuft und weitergegeben werden kann.
- **`geoanim exportdir ordner [-o ausgabeordner] [-assets] [-sheet] [-serve [port]]`** exportiert einen Ordner als **Buch**: eine Seite für jede `.geo`-Datei im Ordner und allen Unterordnern – jeweils daneben oder, mit `-o`, in derselben Ordnerstruktur unter dem Ausgabeordner (so bleiben Quellen und erzeugte Dateien getrennt) – und dort eine `index.html` als Übersicht: Jeder Unterordner ist ein Kapitel, das sich mit einem Klick aufklappt (mit der Zahl seiner Seiten; welche Kapitel offen waren, merkt sich der Browser), darin seine Seiten mit Titel und Kurzbeschreibung und seine Unterordner wieder als aufklappbare Unterkapitel; die Seiten des Ordners selbst stehen offen. Dateien ohne Zeichnung (etwa reine Stil-Dateien) fehlen dort. In der Konsole zeigt eine einzige Zeile, welches Skript gerade an der Reihe ist; Fehler (rot) und Warnungen (gelb) stehen auf eigenen Zeilen darüber, am Ende eine Zusammenfassung. Ein fehlerhaftes Skript wird gemeldet, die übrigen werden trotzdem exportiert.
  - **Reihenfolge und Namen:** Eine Zahl mit Unterstrich am Anfang eines Datei- oder Ordnernamens legt die Reihenfolge fest (natürlich sortiert, `2_…` vor `10_…`; Dateien und Unterordner eines Ordners gemeinsam: `40_Winkelsätze/` steht zwischen `30_….geo` und `50_….geo`, in der Übersicht wie beim Blättern) und fällt im Export weg. Die Namen schreibt man, wie sie heißen sollen, mit Umlauten und Leerzeichen; die Adresse macht der Export daraus: ä → ae, ö → oe, ü → ue, ß → ss, é → e, Leerzeichen → `-`, andere Sonderzeichen entfallen (dafür gibt es eine Warnung). `10_Grundlagen/40_Winkelsätze/20_Maße und Größen.geo` wird `Grundlagen/Winkelsaetze/Masse-und-Groessen.html`. Die Kapitelüberschrift der Übersicht ist der Ordnername ohne Nummer, Bindestriche und Unterstriche werden zu Leerzeichen (`30_Strahl-und-Gerade` → „Strahl und Gerade“). Ergeben zwei Dateien dieselbe Adresse, ist das ein Fehler.
  - **Hilfsdateien:** Dateien und Ordner, deren Name mit `_` beginnt (`_def.geo`, `_Stile/`), bekommen keine Seite; sie dienen nur für `include` und `use`.
  - **Links:** Links zwischen den Seiten schreibt man mit den Namen der Skripte (`[Strahl](30_Strahl-und-Gerade.html#2)` – so öffnet sie auch die VS-Code-Vorschau); der Export setzt die Namen der Seiten ein. Die Schlüsselwörter der Seiten (`keyword`, siehe unten) werden in den anderen Seiten automatisch verlinkt.
  - **Blättern:** Im Menü jeder Seite stehen über den Schritten „Übersicht“, die vorige und die nächste Seite in Buchreihenfolge (über Ordnergrenzen hinweg); unter dem letzten Schritt steht zusätzlich „Weiter: …“ mit der nächsten Seite.
  - **Mit `-assets`:** Statt dass jede Seite Player, Stylesheets und Schriften enthält (etwa 140 KB je Seite), liegen diese einmal im Ordner `assets` an der Wurzel des Buchs, und die Seiten verweisen darauf (dann nur wenige KB je Seite) – gedacht für den Webserver, wo der Browser die Dateien nur einmal lädt. Die Verweise tragen einen Fingerabdruck (`player.js?v=…`), so dass nach einer neuen Version von GeoAnim kein Browser die alte aus seinem Zwischenspeicher nimmt. Ohne `-assets` bleibt jede Seite für sich allein lauffähig.
  - **Mit `-sheet`:** zusätzlich eine `kontaktbogen.html` für den Autor (nicht verlinkt, nicht in der `sitemap.xml`): für jede Seite in Buchreihenfolge das Startbild und das Bild am Ende jedes Schritts, mit Nummer und Schrittbeschreibung, nebeneinander – zum schnellen Durchsehen, ob nach einer Änderung (etwa an Schriftgrößen) irgendwo etwas nicht mehr passt. Jedes Bild ist die echte Seite im Standbild-Modus (`Seite.html?still#3`: nur die Zeichnung am Ende von Schritt 3, ohne Texte und Steuerleiste) und führt per Klick dorthin. Oben lässt sich die Bildbreite umschalten (450, 720, 1080 Pixel), denn Texte skalieren mit ihr; die Bilder laden erst beim Hinscrollen.
  - **Mit `-serve`:** Nach dem Export läuft ein Webserver auf `http://localhost:8080/` (oder dem angegebenen Port; ist er belegt, der nächste freie) mit dem Ausgabeordner als Wurzel, ohne Zwischenspeicher: Nach einem neuen Export (von Hand) zeigt ein Neuladen im Browser den aktuellen Stand. Skripte werden nicht überwacht. Ein Browser wird nicht geöffnet; Strg+C beendet.
  - **`index.yaml`** im Quellordner beschreibt das Buch (alle Einträge sind freiwillig; unbekannte und eine fehlende Bilddatei werden als Warnung gemeldet):
    ```yaml
    title: Ebene Geometrie in Animationen      # Titel der Übersicht (sonst der Ordnername)
    description: Von Punkten und Strecken …    # Einleitung und Beschreibung der Übersicht
    icon: icons/winkel.svg                     # Symbol im Browser-Tab (svg, png, ico; relativ zur index.yaml)
    language: de                               # Sprache aller Seiten (<html lang="de">)
    url: https://mathepedia.de/geometrie/      # Adresse des Buchs im Netz
    home:                                      # Rückweg zur eigenen Website: im Menü und oben auf der Übersicht
      title: mathepedia.de
      url: https://mathepedia.de/
    footer: © … · [Impressum](https://…) · [Datenschutz](https://…)   # unter der Übersicht und am Ende des Menüs
    ```
    Das Icon steckt ohne `-assets` in jeder Seite (als Daten-URL), mit `-assets` liegt es in `assets/`. Mit `url` bekommt jede Seite ihre Adresse als `canonical`-Link und eine Vorschau für das Teilen in Messengern und sozialen Netzen (Open Graph: Titel, Beschreibung, Name des Buchs), und es entsteht eine `sitemap.xml` mit allen Seiten (Datum: letzte Änderung des Skripts) für Suchmaschinen. Kapitelüberschriften der Übersicht kommen weiter aus den Ordnernamen.
  - Seiten mit alten Namen im Ausgabeordner (etwa nach dem Umbenennen) löscht der Export nicht.
               
Gezeichnet wird mit SVG, also in jeder Größe scharf. Formeln stehen in AsciiMath zwischen `$…$` und werden beim Übersetzen mit MathJax zu SVG-Pfaden; der Export enthält nur die Zeichen, die vorkommen. Die Schrift ist STIX Two (Text und Formeln aus einer Familie). Abhängigkeiten: siehe [Lizenz](#lizenz).

## Schnellstart

```
dotnet build GeoAnim.slnx
dotnet run --project GeoAnim -- serve Samples/perpendicular-bisector.geo
dotnet run --project GeoAnim -- export Samples/circumcircle.geo
```

In Visual Studio: Startprojekt `GeoAnim`; die Startprofile „serve sample“ und „export sample“ (`GeoAnim/Properties/launchSettings.json`) arbeiten im Repository-Ordner, dort lässt sich die Datei austauschen. Alle `.geo`-Dateien aus `Samples` samt Unterordnern erscheinen im Projekt `GeoAnim` unter „Samples“, neue Dateien von selbst. Optionen: `serve … --port 8080 --no-browser`, `export … -o ziel.html`.

**Player:** Von oben nach unten: Überschrift, die Beschreibung des aktuellen Schritts mit seiner Nummer (grau vorangestellt wie in der Schrittliste, passend zu den Marken auf dem Schieberegler; beim Startbild leer), das Bild, darunter `info` bzw. beim Startbild `description` und auf der letzten Seite „Weiter: …“. Über und unter dem Bild ist Platz für den längsten Text aller Schritte reserviert, damit das Bild beim Blättern nicht springt. Überschrift, Schrittbeschreibung und `info` wachsen mit dem Bild wie die Beschriftungen darin (bei 1280 Pixeln Bildbreite 38, 30 und 28 Punkt, so wie `labelsize` gemessen wird), werden aber nie kleiner als 18, 15 bzw. 14 Pixel; so bleibt das Verhältnis von Text und Zeichnung in jeder Fenstergröße gleich. Beschriftungen und Texte im Bild wachsen ebenso mit; in einem kleinen Bild fällt der kleinste nie unter 12 Pixel, größere bekommen eine Untergrenze, die mit ihrer Größe sanfter wächst (12 Pixel mal Wurzel aus dem Größenverhältnis zum kleinsten) – so bleibt ein größerer Text immer der größere, ohne das kleine Bild zu überladen. Ausdrücklich kleiner als 12 angegebene Texte haben ihre eigene Größe als Untergrenze. Der Knopf oben links (oder M) öffnet die Liste aller Schritte; ein Klick springt ans Ende des Schritts. Abspielen/Pause (großer runder Knopf, Leertaste, Klick ins Bild), vorheriger/nächster Schritt (← →), Anfang/Ende (Pos1/Ende), Schritt wiederholen (R), Schieberegler mit Marken an den Schrittenden. Die Animation beginnt angehalten beim Startbild und hält beim Abspielen nach jedem Schritt an; durchlaufen bis zum Ende lässt sich zuschalten, indem man „nach jedem Schritt anhalten“ ausschaltet (S). Der aktuelle Schritt steht in der Adresse (`Seite.html#3`, ohne neuen Eintrag im Verlauf): Eine solche Adresse öffnet die Seite am Ende von Schritt 3 (`#0`: Startbild), und nach „Zurück“ steht man wieder dort, wo man die Seite verlassen hat; auch von Hand gesetzte Links können so auf einen Schritt zeigen (`[Endpunkte](Strecken.html#2)`). Geschwindigkeit ¼× bis 4×: Klick auf die Anzeige öffnet die Auswahl, außerdem − / + oder das Mausrad über der Anzeige.

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

Zum Nachschlagen: [REFERENZ.md](REFERENZ.md) listet alle Befehle mit allen Schreibweisen und den jeweils erlaubten Optionen.

Eine Anweisung pro Zeile; Einrückung ist egal. Alle Angaben werden nur durch Leerzeichen getrennt, ohne Kommas und Klammern (Koordinaten: `2 3`). `#` beginnt einen Kommentar (außer `#rgb` / `#rrggbb` als Farbe). Zahlen mit Punkt (`2.5`, `-0.5`). Hinter `title` und `step` ist der Rest der Zeile der Text, so wie er dasteht, ohne Anführungszeichen (auch `#` gehört dann dazu). Andere Texte (`text`, `label`) stehen in doppelten Anführungszeichen (`\"` für ein Anführungszeichen darin), weil danach noch Optionen folgen können. Namen bestehen aus Buchstaben, Ziffern und `_`, gefolgt von beliebig vielen `'` (`A'`, `P_1`); Groß-/Kleinschreibung zählt. Schlüsselwörter (`point`, `to`, `x`, `by`, `length`, …) sind als Namen nicht erlaubt.

**Lange Texte und Auszeichnungen:** Eine Zeile, die mit `|` beginnt, setzt den Text der `title`-, `step`- oder `set`-Zeile davor fort (mit einem Leerzeichen angehängt); eine Zeile mit nur `|` beginnt einen neuen Absatz:

```
step Wir zeichnen um $A$ einen Kreis mit dem Radius $|AB|$
| und erhalten so alle Punkte, die von $A$ so weit entfernt sind wie $B$.
|
| Ein zweiter Absatz.
```

Längere Texte für das Bild bekommen mit `set` einen Namen und stehen dann bei `text` und `label` statt `"…"` (in Anführungszeichen ist `\n` ein Zeilenumbruch):

```
set hinweis = Die Strecke $bar(AP)$ ist **genauso lang**
| wie $c$.
|
| *Damit ist der Satz bewiesen.*
text t = hinweis at 5 3.5
```

Ein Text gilt ab seiner Zeile; Texte, Vars und Objekte haben verschiedene Namen.

Alle Texte verstehen etwas Markdown: `**fett**`, `*kursiv*` oder `_kursiv_`, `` `Code` ``, Links `[Satz 1](Prop1.html)` und `<Prop1.html>`; Formeln dürfen darin stehen. `\*` usw. hält ein Zeichen fest. Im Bild werden Texte mehrzeilig gesetzt (bei `text` an festen Koordinaten zentriert), Links dort nur unterstrichen. In einer exportierten Datei öffnet ein Link auf eine andere Animation diese im selben Fenster (die HTML-Dateien eines Ordners werden so zu einem verlinkten Buch), Links auf andere Seiten öffnen ein neues Fenster; bei `geoanim serve` öffnen alle Links ein neues Fenster, in der VS-Code-Vorschau öffnet `Prop1.html` die Datei `Prop1.geo` im Editor.

**Formeln:** In allen Texten (`title`, `step`, `text`, `label`) steht zwischen zwei `$` eine Formel in [AsciiMath](https://asciimath.org): `step Kreis um $A$ mit Radius $|AB|$`, `text t = "$alpha + beta = 180^@$" at 2 3`, `$bar(AB)$`, `$vec(BD)$`, `$NN sub RR$`. `\$` ist ein Dollarzeichen. Ein `$` ohne Gegenstück ist ein Fehler mit Zeile und Spalte. Punkte sind standardmäßig mit ihrem Namen als Formel beschriftet: kursiv, `P_1` mit tiefgestellter 1, `A'` mit Strich.

Koordinaten sind mathematisch: die y-Achse zeigt nach oben. Der Maßstab ist in beiden Richtungen gleich; das Bild hat die Form des Bereichs aus `view`.

### Einstellungen (vor dem ersten Schritt)

| Anweisung | Bedeutung |
|---|---|
| `title Mittelsenkrechte` | Überschrift über dem Bild (Rest der Zeile) |
| `description Kurzbeschreibung …` | worum es auf der Seite geht: steht unter dem Startbild (linksbündig wie `info`, mit Markdown und Formeln) und ist die Beschreibung der exportierten Seite für Suchmaschinen (`meta description`, mehrzeilig mit `|`); ohne sie beschreibt sich die Seite mit dem Anfang ihrer Schrittbeschreibungen (Formeln lesbar gemacht, etwa 250 Zeichen) |
| `view x -1 .. 11 y -1 .. 7` | sichtbarer Bereich (Standard wie hier); vor dem ersten Objekt |
| `background white` | Hintergrundfarbe |
| `grid` / `grid 0.5` | Gitter mit Abstand 1 bzw. 0.5 |
| `axes` | Koordinatenachsen mit Zahlen |
| `set name = Text …` | benannter Text (mehrzeilig mit `|`) für `text` und `label` (siehe unten) |
| `default point size 4 labelsize 20` | Voreinstellung: Optionen, die für alle folgenden Objekte der genannten Arten zuerst gelten (siehe unten) |
| `keyword Punkt/e/en, Satz/es des Pythagoras` | Schlüsselwörter dieser Seite für `exportdir` (siehe unten; mehrzeilig mit `|`); hinter einer `step`-Zeile verweisen sie auf diesen Schritt |
| `include styles.geo` | bindet eine andere Datei an dieser Stelle ein (siehe unten) |
| `input point A = -3 0` / `output C` | Eingaben und Ergebnisse, wenn die Datei mit `use` benutzt wird (siehe unten) |
| `speed 0.5` | Tempo der ganzen Animation: 1 wie angegeben, 0.5 halb so schnell (alles dauert doppelt so lange), 2 doppelt so schnell |

### Objekte

| Anweisung | Objekt |
|---|---|
| `point A = 2 3` | freier Punkt (kann sich bewegen) |
| `point M = midpoint A B` | Mittelpunkt |
| `point D = parallel g through C` / `point D = perpendicular g through C` | der Punkt C + (B − A) für die Gerade (Strecke, Strahl) g = AB, bzw. dieser Vektor um 90° gegen den Uhrzeigersinn gedreht: mit C zusammen bestimmt er die Parallele bzw. Senkrechte zu g durch C; er folgt g und C |
| `point X = copy P` | freier Punkt an der Stelle, an der P gerade ist (z. B. um eine Kopie zu bewegen, ohne die Konstruktion zu ändern) |
| `point S T = intersect k1 k2` | Schnittpunkte (ein oder zwei Namen) |
| `point P = on k at 60` | Punkt auf einem Kreis, Winkel in Grad (gegen den Uhrzeigersinn ab der x-Richtung) |
| `point Q = on AB at 0.25` | Punkt auf Strecke/Gerade/Strahl: A + 0.25·(B − A) |
| `segment s = A B` | Strecke (Kurzform `segment A B` heißt `AB`) |
| `line g = A B` | Gerade (am Bildrand abgeschnitten) |
| `ray r = A B` | Strahl ab A durch B |
| `circle k = center M through P` | Kreis um M durch P |
| `circle k = center M radius 2.5` | Kreis mit festem Radius |
| `circle k = center M radius A B` / `circle k = center M radius l` | Kreis, dessen Radius der Zeichnung folgt – „mit dem Zirkel abgegriffen“: der Abstand von A und B bzw. eine Zahl (gemessen oder berechnet, etwa `number r = l + 0.5` oder `number r = l1 + l2`); so lassen sich Strecken übertragen (Dreieck aus drei Seiten, Parallele mit dem Zirkel, …) |
| `line p = parallel g through C` / `line s = perpendicular g through C` | Parallele bzw. Senkrechte zu g durch C (ebenso `segment` – von C aus, so lang wie g – und `ray`); sie folgt g und C |
| `arc b = k from A to B` | Kreisbogen auf k gegen den Uhrzeigersinn von A bis B (A, B geben die Richtungen vom Mittelpunkt an; der Bogen bleibt auf dem Kreis, wenn er sich ändert); Optionen wie bei einer Strecke, auch `arrow` |
| `sector s = k from A to B` | Kreissektor: die beiden Radien und der Bogen dazwischen, gefüllt wie ein Vieleck (`fill`, `opacity`), Beschriftung innen |
| `arc b = k from 30 to 120` / `… from A to 90` | Bogen bzw. Sektor mit Richtungen in Grad statt Punkten (gegen den Uhrzeigersinn ab Osten; auch Konstanten), gemischt mit Punkten möglich – ohne erst Punkte auf dem Kreis zu konstruieren |
| `arc b = k from A to B clockwise arrow` | im Uhrzeigersinn von A nach B (also der andere Bogen als ohne `clockwise`), so wird er auch gezeichnet; `arrow` sitzt am Ende und zeigt in Drehrichtung, `arrow start` am Anfang – etwa für eine Drehung im Uhrzeigersinn; ebenso beim Sektor |
| `polygon D = A B C` | gefülltes Vieleck (mindestens 3 Punkte) |
| `text t = "Hallo" at 5 6` | Text an festen Koordinaten (zentriert) |
| `text t = "90°" at P` | Text neben einem Punkt, wandert mit ihm |
| `text t2 = "…" below t1` / `below t1 gap 0.2` | Text eine Zeile unter dem Text `t1` (einem Text an Koordinaten oder selbst unter einem), mit dessen Ausrichtung und linkem bzw. rechtem Rand; der Abstand richtet sich nach der tatsächlichen Höhe von `t1` (Formeln mit Brüchen schieben weiter nach unten), `gap` gibt zusätzlich Platz in Einheiten; der Block wandert mit `t1`, eine Zeile lässt sich mit `shift` einrücken (die Zeilen darunter folgen ihr) |
| `number l = length s` | Zahl: Länge einer Strecke (`length A B`: Abstand zweier Punkte); ebenso `degrees w` (Winkelgröße in Grad), `area D` (Flächeninhalt eines Vielecks oder Kreises), `perimeter D` (Umfang), `radius k` (siehe unten) |
| `number tx = 5.2` / `number h = a * sqrt(3) / 2` | Zahl, berechnet aus festen Zahlen und anderen Zahlen (siehe unten): mit `+ - * / ^` (Potenz), Klammern, `sqrt(…)`, `abs(…)` und `pi`, Leerzeichen nach Belieben |

**Konstanten und Rechnungen:** `number tx = 5.2` legt eine Zahl fest, `number ty = tx / 2 + 1` rechnet mit Zahlen, die vorher definiert sind. Hängt eine Rechnung nur von festen Zahlen ab, ist sie eine **Konstante**: Ihr Wert steht beim Einlesen fest, und ihr Name darf überall stehen, wo das Skript eine Zahl erwartet – Koordinaten (`point A = tx ty`, `text t = "…" at tx ty`, `move A to tx 0`), Optionen (`size s`, `shift dx 0`, `duration d`), `rotate … by w`, `on k at w`, `pause d`. So verschiebt man eine Gruppe, indem man eine Zahl ändert; gerechnet wird nur in der `number`-Zeile, nicht an der Stelle der Verwendung. Rechnungen mit gemessenen Zahlen (`number halb = l / 2`) folgen der Zeichnung wie diese und dienen nur der Anzeige in Texten; als Koordinate sind sie ein Fehler (welcher ihrer Werte wäre gemeint?). Eine Konstante zeigt in `{tx}` so viele Nachkommastellen, wie ihr Wert braucht (höchstens 3), eine Rechnung mit gemessenen Zahlen so viele wie die genaueste von ihnen. `pi`, `sqrt` und `abs` können keine Namen von Zahlen sein. Dieselben Ausdrücke sollen später auch Funktionsgraphen beschreiben.

**Zahlen:** `number` misst etwas an anderen Objekten und folgt ihnen, wenn sie sich bewegen. Texte zeigen eine Zahl mit dem Platzhalter `{l}`, mit eigener Anzahl Nachkommastellen `{l:2}` – in `text`, Beschriftungen (`label`), `set`-Texten, `step`, `info` und `description`. Der Player setzt den Wert in jedem Bild neu ein, mit Dezimalkomma; Längen, Flächen, Umfänge und Radien zeigen standardmäßig eine Nachkommastelle, Winkel ganze Grad. Einheiten und das Gradzeichen schreibt man selbst dazu: `text t = "$|AB| =$ {l} cm" at 5 1`, `label "{w}°"`. Ein Platzhalter darf nicht in einer Formel stehen (Formeln werden beim Export einmal gesetzt), sondern zwischen ihren Stücken: `"$alpha =$ {w}°"`. Ein Platzhalter darf vor seiner Zahl stehen (`step Der Winkel ist {w}° groß`, darunter `number w = …`), muss aber eine Zahl der Datei nennen. `\{` ist eine geschweifte Klammer. Außerhalb des Players (Beschreibung der Seite, Übersicht) stehen die Werte vom Ende der Animation.

**Startbild und Schritte:** Objekte vor dem ersten `step` gehören zum Startbild und sind sofort da. In einem Schritt definierte Objekte werden dort nacheinander gezeichnet: Punkte erscheinen, Strecken wachsen von A nach B, Geraden von der Mitte nach außen, Kreise werden ab dem Punkt `through` (sonst ab 0°) gezogen, Vielecke und Texte blenden ein. Mit der Option `hidden` wird ein Objekt nur definiert (z. B. Hilfskreise) und erst mit `show` gezeigt – oder nie.

**Reihenfolge der Schnittpunkte:** bei Gerade/Strecke/Strahl und Kreis in Richtung A → B; bei zwei Kreisen zuerst der Punkt links von der Richtung vom ersten zum zweiten Mittelpunkt. Bei nur einem Namen gilt der erste Schnittpunkt, den es gibt (bei Strecke oder Strahl kann der andere außerhalb liegen). Die Reihenfolge bleibt erhalten, wenn sich Punkte bewegen. Ein Schnittpunkt, der beim Bewegen verschwindet (Kreise trennen sich), wird ausgeblendet, samt allem, was von ihm abhängt.

### Schritte und Aktionen

| Anweisung | Bedeutung |
|---|---|
| `step Beschreibung bis zum Zeilenende` | neuer Schritt; der Text erscheint mit der Schrittnummer über dem Bild |
| `textstep Beschreibung` | Schritt nur mit Text, wie eine Buchseite ohne Abbildung: Der Player blendet das Bild aus, die `info`-Texte des Schritts nutzen die ganze Fläche (bei langem Text scrollbar). Erlaubt sind nur `info`, `keyword`, `pause` und `number`; Zeichnen, Ein-/Ausblenden oder Bewegen ist ein Fehler. Im nächsten Schritt ist das Bild wieder da, wie es war; seine Größe ändert sich durch Textschritte nicht. Beim Durchlaufen dauert er so lange wie seine `pause`, ohne sie eine Lesezeit nach Textlänge (3 s + ¼ s je Wort) |
| `info Erklärung …` | Erklärung zum aktuellen Schritt, unter dem Bild linksbündig (mehrzeilig mit `|`, Absätze, Markdown, Formeln; ein weiteres `info` wird ein neuer Absatz) |
| `info ## Zwischenüberschrift` | ein Absatz, der mit `## ` beginnt, ist eine Zwischenüberschrift (fett, etwas größer, mit Abstand davor) – gedacht für längere Texte, vor allem im `textstep`; eine eigene `info`-Zeile (oder nach einem `|` allein). Schlüsselwörter werden darin nicht verlinkt |
| `show A k1` | zeigt Objekte (zeichnet versteckte, blendet ausgeblendete ein); was schon zu sehen ist, bleibt, wie es ist |
| `hide k1 k2` | blendet Objekte aus; was nicht zu sehen ist (schon ausgeblendet oder nie gezeigt), bleibt, wie es ist – hat ein `show`/`hide` nichts zu tun, kostet es keine Zeit |
| `hide all` / `hide label all` | blendet alles aus, was gerade zu sehen ist (etwa am Anfang eines Schritts, der eine neue Zeichnung beginnt), bzw. alle Beschriftungen; in einer mit `use` benutzten Datei nur deren eigene Objekte |
| `highlight a b` | hebt Objekte hervor, um auf sie hinzuweisen: zweimal kurz in der Hervorhebungsfarbe (Standard orange, `color …`), Linien dicker, Punkte und Texte größer, Füllungen kräftiger (1200 ms) |
| `highlight a b keep` / `unhighlight a b` | hebt Objekte hervor, bis `unhighlight` es zurücknimmt (etwa während einer Erklärung „diese Seiten sind gleich lang“); beides in 400 ms. Voreinstellungen mit `default highlight color red duration 1500` |
| `show label A B s` / `hide label A B s` | blendet nur die Beschriftungen ein bzw. aus (Punkte, sowie Strecken, Strahlen, Geraden und Winkel mit `label`); sie wandern weiter mit ihren Objekten. Auch für mit `nolabel` angelegte Objekte; vor dem Zeichnen gilt es für das spätere Erscheinen |
| `move A to 4 5` | bewegt einen freien Punkt – oder einen Text an festen Koordinaten (`move t to 4 5`, `move t to P`; ein Text neben einem Punkt wandert mit diesem) |
| `move P to 150` | bewegt einen Punkt auf seinem Kreis (Winkel) bzw. seiner Strecke (Position) |
| `move C1 to P` | bewegt einen freien Punkt dorthin, wo der Punkt P in diesem Moment ist (P kann konstruiert sein; bewegt sich P später, bleibt C1, wo er ist) |
| `move label A s to 0.3 -0.2` | verschiebt Beschriftungen auf einen neuen `shift` (gleitend, 400 ms), etwa wenn sie nach einer Bewegung etwas überdecken |
| `move label A to SE` / `move label A to 300 gap 12` | dreht die Beschriftung eines Punkts auf einem Bogen um ihn in eine andere Richtung (wie bei `labeldir`, den kürzeren Weg), mit `gap` auch auf einen anderen Abstand; eine Beschriftung ohne `labeldir` beginnt dabei bei `NE` |
| `move A B C along P Q` | verschiebt Objekte um den Vektor von P nach Q (so wie er beim Start der Bewegung ist): bewegt werden die freien Punkte, aus denen sie gebaut sind (bei einem Vieleck seine Ecken, bei einem Mittelpunkt dessen Punkte, bei einem Text an festen Koordinaten seine Position), alles davon Abhängige folgt |
| `rotate A B C around M by 60` | dreht Objekte um M um 60° gegen den Uhrzeigersinn (negativ: im Uhrzeigersinn, auch über 360°); die freien Punkte laufen auf Kreisbögen, die Figur bleibt unterwegs unverformt; Voreinstellungen mit `default rotate duration …` |
| `reflect A B C across g` / `reflect A B C across P Q` | spiegelt Objekte an der Geraden g (auch einer Strecke oder einem Strahl) bzw. an der Geraden PQ, wie sie zu Beginn liegt: Die freien Punkte laufen senkrecht zur Achse auf ihre Bilder, mit dem Kosinus einer halben Drehung – so sieht es aus, wenn die Figur im Raum um die Achse umklappt (wie eine Buchseite; auf halbem Weg liegt alles auf der Achse). Mit `straight` laufen die Punkte gleichmäßig hinüber. Was ganz gespiegelt wird, behält seine Lage: Winkel bleiben so groß (sie werden danach im Uhrzeigersinn gemessen), die Beschriftung einer Strecke bleibt auf derselben Seite der Figur. Wer das Original behalten will, kopiert die Punkte vorher (`copy`) |
| `reflect A B C in Z` | spiegelt Objekte am Punkt Z: eine Drehung um 180° (die Figur bleibt unterwegs unverformt); mit `straight` laufen die Punkte geradlinig durch Z auf ihre Bilder |
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
| `label "$A'$"` / `nolabel` | Punkt | Beschriftung (Standard: der Name als Formel) bzw. zunächst keine (mit `show label` später einblendbar; bei Linien und Winkeln ebenso, wenn sie ein `label` haben) |
| `label "$a$"` | Strecke, Strahl, Gerade, Winkel | Beschriftung, die mit dem Objekt wandert; bei Linien standardmäßig in der Mitte, knapp rechts von der Richtung A → B (beim gegen den Uhrzeigersinn benannten Dreieck also außen), beim Winkel auf der Winkelhalbierenden im Sektor (beim rechten Winkel statt des Punkts) |
| `arrow` / `arrow start` / `arrow both` | Strecke, Strahl, Winkel | Pfeilspitze am Ende (bei B; `arrow end` ist dasselbe), am Anfang (bei A) oder an beiden Enden (Maßpfeile); beim Strahl sitzt die Spitze am Bildrand; beim Winkel auf dem Bogen: am Ende zeigt sie gegen den Uhrzeigersinn (die Drehrichtung von A B C), mit `start` im Uhrzeigersinn. Die Spitze wächst mit der Linienbreite, hat deren Farbe und wandert beim Zeichnen mit. Für Winkelschenkel als Pfeile: `segment SP = S P arrow` mit ausgeblendetem Punkt P |
| `halo` | Punkt, Strecke, Strahl, Gerade, Winkel, Text | Beschriftung (bzw. Text) mit einem schmalen Rand in der Hintergrundfarbe, beim Winkel in der Farbe seiner Fläche: Linien, die darunter verlaufen, enden kurz vor den Buchstaben, statt durch sie hindurchzugehen. Liegt die Beschriftung über einer anders gefärbten Fläche, ist der Rand dort als heller Saum zu sehen – daher eine Option, kein Standard (z. B. `var innenwinkel … halo`) |
| `back` / `front` | alle Objekte | Stapelung: Normalerweise liegen unabhängig von der Zeichenreihenfolge Füllungen (Vielecke, Winkel) unten, darüber Linien und Kreise, Punkte, Beschriftungen und ganz oben Texte. `back` legt das Objekt unter alle anderen, auch unter die Füllungen (z. B. Verschiebungspfeile unter einer halb durchsichtigen Figur), `front` über alle außer Texten. Seine Beschriftung bleibt bei den Beschriftungen |
| `labeldir NW` / `labeldir 200` | Punkt | Richtung der Beschriftung vom Punkt aus: `N`, `NE`, `E`, `SE`, `S`, `SW`, `W`, `NW` oder Grad (gegen den Uhrzeigersinn ab Osten, wie bei `rotate`). Die Beschriftung steht so weit draußen, dass zwischen Punkt und dem Rand der Schrift immer derselbe Zwischenraum bleibt – ein breites $A_1'$ wie ein schmales $B$. `shift` wirkt zusätzlich zur Feinarbeit |
| `labelgap 12` | Punkt | Zwischenraum zwischen Punkt und Beschriftung bei `labeldir`, in Punkt (Standard 4), z. B. wenn eine Linie dicht vorbeiläuft |
| `labelpos 0.3` | Strecke, Strahl, Gerade | Lage der Beschriftung bei A + 0.3·(B − A) (Standard 0.5); beim Strahl auch über 1, bei der Geraden beliebig |
| `shift 0.3 -0.2` | Punkt, Strecke, Strahl, Gerade, Winkel, Text | verschiebt die Beschriftung (bzw. den Text) in Koordinateneinheiten von dort aus, wo sie ohne Angabe stünde: `shift 0 0` ändert nichts. Während der Animation nachjustieren mit `move label A to 0.3 -0.2` |
| `offset 0.3 -0.5` | Punkt (Beschriftung), Text, Strecke/Strahl/Gerade (Beschriftung), Winkel (Beschriftung) | *veraltet, besser `shift`:* Lage in Koordinateneinheiten relativ zum Punkt bzw. zur Stelle `labelpos`: dort beginnt die Grundlinie des Textes (bei Text an festen Koordinaten und bei Linien: ihre Mitte); ohne `offset` steht die Beschriftung knapp daneben. Beim Winkel verschiebt `offset` die Beschriftung von ihrem Platz auf der Winkelhalbierenden aus (z. B. bei übereinanderliegenden Winkelbögen); der Bogen bleibt, wo er ist |
| `align left` / `align center` / `align right` | Text | ein Text an Koordinaten beginnt dort, steht mittig darauf (Standard) bzw. endet dort; mehrzeilige Texte sind entsprechend bündig – gut für Erläuterungen, die untereinander erscheinen (mit `below`) |
| `fill lightblue` / `fill` | Vieleck, Winkel, Kreis | Füllfarbe (Standard und bei `fill` allein: die Linienfarbe); ein Kreis wird nur mit `fill` zur gefüllten Kreisscheibe, deren Füllung beim Zeichnen mit dem Zirkelstrich mitwächst |
| `opacity 0.3` | Vieleck, Winkel, gefüllter Kreis | Deckkraft der Füllung (Standard 0.2) |
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

**Voreinstellungen:** `default arten… optionen…` legt Optionen für alle folgenden Objekte fest, damit nicht jedes einzelne sie braucht. Arten: `point`, `segment`, `line`, `ray`, `circle`, `polygon`, `angle`, `text`, `show`, `hide`, `highlight`, `unhighlight`, `move`, `rotate`, `reflect`; ohne Art gilt die Voreinstellung für alle.

```
default point size 4 labelsize 20
default segment line ray circle polygon width 2
default text size 22
default move duration 1500
default color navy
```

Es gilt der Reihe nach: Voreinstellungen ohne Art, dann die für die Art, dann die Optionen des Objekts selbst – die spätere gewinnt. Eine weitere `default`-Zeile ergänzt die bisherigen für die folgenden Objekte. Wie bei einer Var wird übergangen, was nicht passt (`default width 2` betrifft keine Punkte). Vorsicht bei `size` ohne Art: das ist bei Punkten der Radius, bei Texten die Schriftgröße. Vars sind auch hier möglich: `default circle hilfs`.

**Schlüsselwörter:** `keyword` legt fest, unter welchen Begriffen eine Seite zu finden ist; die Begriffe werden durch Kommas getrennt, ein Begriff kann aus einem oder mehreren Wörtern bestehen (`keyword Punkt/e/en, Satz/es des Pythagoras`). `Punkt/e/en` steht für den Wortstamm allein oder mit genau einer der Endungen: Punkt, Punkte, Punkten. Jedes Wort eines Begriffs darf eigene Endungen haben; gemeint sind alle Kombinationen (`rechte/r/n Winkel/s/n`: rechter Winkel, rechten Winkels, …; unpassende Kombinationen schaden nicht, sie kommen im Text einfach nicht vor). Wo sich der Stamm ändert, kommt ein weiterer Begriff dazu (`Satz/es, Sätze/n`). Ein Komma am Zeilenende vor einer `|`-Fortsetzung ist erlaubt. Beim Export mit `geoanim exportdir` wird in der Erklärung (`info`) jedes Schritts und in der Beschreibung (`description`, unter dem Startbild) der *anderen* Seiten das erste Vorkommen je Seite zum Link (nicht in den Schrittbeschreibungen: sie sind Überschriften und stehen auch im Menü) auf die Seite mit dem Schlüsselwort, mit relativem Pfad auch über Ordner hinweg. Steht `keyword` vor dem ersten Schritt, führt der Link an den Anfang der Seite; steht es hinter einer `step`-Zeile, ans Ende dieses Schritts (`Strecken.html#2`). Es zählen nur ganze Wörter in genau dieser Schreibweise („gerade“ ist nicht „Gerade“, „Punktsymmetrie“ nicht „Punkt“); bei überlappenden Begriffen gewinnt der längere. Formeln, `Code` und von Hand gesetzte Links bleiben unverändert. Ein Backslash vor einem Wort verhindert den Link an dieser Stelle und wird nicht angezeigt: `auf der einen \Seite` (bei einem Begriff aus mehreren Wörtern vor jedem Wort, das selbst ein Schlüsselwort ist: `\rechter \Winkel`). Gibt es ein Schlüsselwort auf zwei Seiten, meldet `exportdir` das als Fehler mit beiden Stellen als `Datei:Zeile` (im Terminal von VS Code anklickbar) und verlinkt auf die erste. **Stichwortverzeichnis:** Neben der `index.html` entsteht eine `stichwoerter.html`, verlinkt unter der Einleitung der Übersicht: alle Begriffe alphabetisch unter ihren Anfangsbuchstaben (Umlaute beim Grundbuchstaben), jeder mit der Seite (und dem Schritt), die ihn erklärt, und den Seiten, die ihn erwähnen. Ein Begriff erscheint einmal, unter seinen Wortstämmen ohne Endungen (`Satz/es des Pythagoras` → „Satz des Pythagoras“). Es gehört zur `sitemap.xml`. `export` einer einzelnen Datei und `serve` setzen keine solchen Links; die VS-Code-Vorschau setzt sie, wenn das Skript zu einem Buch gehört (ein Ordner darüber hat eine `index.yaml`), siehe `editors/vscode/README.md`. Die Schlüsselwörter einer mit `use` benutzten Datei gelten nicht; eingebundene (`include`) wie geschrieben.

**Dateien einbinden:** `include pfad` (Rest der Zeile, relativ zur einbindenden Datei; auch in Anführungszeichen, `include "meine Stile.geo"`) setzt den Inhalt einer anderen Datei an dieser Stelle ein, als stünde er dort – typisch für gemeinsame Voreinstellungen und Vars (`include ../styles.geo`). Eingebundene Dateien dürfen selbst einbinden, aber nicht im Kreis. Fehler darin werden mit Datei und Zeile gemeldet (`styles.geo, line 3, column 1: …`), und `geoanim serve` übersetzt auch neu, wenn eine eingebundene Datei gespeichert wird. Hinweis: Die Tests übersetzen jede `.geo`-Datei als eigenes Skript; eine Datei nur mit Voreinstellungen und Vars ist das auch (mit leerer Animation). Für `exportdir` beginnt der Name solcher Dateien am besten mit `_` (`_def.geo`), dann entsteht keine Seite dafür.

**Konstruktionen wiederverwenden (`use`):** Eine Datei kann eine andere wie eine Funktion benutzen. Die benutzte Datei nennt ihre Eingaben und Ergebnisse:

```
# Book1/Prop1.geo – gleichseitiges Dreieck über AB
input point A = -3 0      # der Wert gilt nur, wenn die Datei allein läuft
input point B = 3 0
…die Konstruktion wie gewohnt, mit Schritten…
output point C = intersect k_1 k_2   # oder: output C, nach der Definition von C
…was nur beim Alleinlaufen gezeigt wird, z. B. ein Beweis…
```

```
# Book1/Prop2.geo
point D = use Prop1.geo A B draw offset 0 0.2
```

- Läuft die Datei allein, ist `input` eine gewöhnliche Definition, und sie animiert sich wie immer – bis zum Ende.
- `output` steht wie `input` vor einer Definition (`output point C = …`, `output segment res = A L`, auch `output D s = use …`). Die Kurzform `output C side` gibt Objekte aus, die vorher definiert wurden – oder eine Eingabe unverändert zurück.
- **Beim `use` endet die Datei mit ihrer letzten `output`-Zeile.** Was danach kommt (ein Beweis, Bewegungen zur Veranschaulichung), gehört nur zum Alleinlaufen. Eine `output`-Zeile steht deshalb nach der Konstruktion ihrer Objekte; steht sie davor, ist das ein Fehler (auch beim Alleinlaufen). Mehrere `output`-Zeilen sind möglich, maßgeblich für das Ende ist die letzte.
- Ein Pfad mit Leerzeichen steht in Anführungszeichen: `point D = use "Buch 1/Satz 1.geo" A B`.
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
- **Ohne Ergebnis:** `use datei A B` am Zeilenanfang zeigt nur die Konstruktion bzw. Animation der Datei – etwa eine wiederkehrende Veranschaulichung wie „diese Strecken sind gleich lang“. Die Datei läuft dann im aktuellen Schritt samt ihren Pausen ab (`draw` lässt die Pausen weg, `steps` übernimmt ihre Schritte); sie braucht keine `output`-Zeile und läuft, wenn sie keine hat, bis zum Ende.
- Was man sieht:
  - `use datei …` (Standard): nur die Ergebnisse, gezeichnet wie gewöhnliche Objekte; die Hilfskonstruktion bleibt unsichtbar.
  - `use datei … draw`: die Konstruktion der Datei wird im aktuellen Schritt gezeichnet (ohne ihre Schrittbeschreibungen und Pausen); am Ende blenden die Hilfsobjekte aus, die Ergebnisse bleiben.
  - `use datei … steps`: wie `draw`, aber die Schritte der Datei werden mit ihren Beschreibungen zu eigenen Schritten.
- Eine benutzte Datei darf ihre Eingaben nicht bewegen, ein- oder ausblenden. Sie darf selbst andere Dateien benutzen (nicht im Kreis); innerhalb eines `use` ohne `draw`/`steps` zeigen auch diese nur ihr Ergebnis. Fehler in der benutzten Datei werden am `use` gemeldet, mit Datei und Zeile: `Line 12, column 15: In Prop1.geo, line 22, column 1: …`.

**Farben:** `black`, `white`, `gray`, `lightgray`, `red`, `darkred`, `green`, `lightgreen`, `blue`, `lightblue`, `navy`, `orange`, `yellow`, `purple`, `violet`, `pink`, `brown`, `teal`, `cyan`, `magenta` oder `#rgb` / `#rrggbb`.

**Standardwerte:** Punkte blau, Radius 5 pt, erscheinen in 400 ms; Linien schwarz, 2,5 pt, 800 ms; Kreise 1200 ms; Vielecke 600 ms; Texte 26 pt, 400 ms; `show`/`hide` 400 ms; `move` 1000 ms mit `ease`.

## Buchseiten in Markdown (im Aufbau)

Längere Texte eines Buchs schreibt man in Markdown statt in `info`-Zeilen: eine Datei `….geo.md` neben den Skripten, die
ihre Abbildungen aus `.geo`-Dateien holt. So beschreibt das Skript nur noch Bild und Ablauf, das Markdown den Text –
Grundlage für das Buch im Web, als E-Book und im Druck. Bisher gibt es die Vorschau in VS Code (umschaltbar zwischen
Buch und Web); der Export folgt.

```markdown
---
title: Punkte und ihre Lage
keywords: [Punkt/e/en]
---

# Punkte und ihre Lage

![](#2)

Ein **Punkt** beschreibt eine genaue Lage. Man schreibt $M != Q$, wenn …
```

Der Kopf gibt Titel, Beschreibung und Stichwörter; Formeln stehen wie in Skripten zwischen `$…$`. Ein Bild auf ein Skript
wird zur Abbildung: `![](datei.geo)` die ganze Animation, `#2` ein Standbild am Ende von Schritt 2, `#3..4` die Schritte 3
bis 4 (im Web als Player, im Buch als Bildfolge), Schritte auch über Marken (`step Gleiche Punkte #gleich`) oder ihren
Namen; `![](#2)` meint das gleichnamige Skript der Seite. Im Buch werden Abbildungen auf ihren Inhalt zugeschnitten.
Abbildungen werden nummeriert; Aussagen stehen in Containern (`::: axiom {#ax-gerade} Geradenaxiom` … `:::`, ebenso
`definition`/`def`, `theorem`, `lemma`, `corollary`, `proof`, `example`, `remark`); Marken (`{#name}` an Abbildungen,
Aussagen und Überschriften) gelten im ganzen Buch, und `§name` im Text wird zum Verweis („Axiom 2“, „Abb. 3“).
Alles im Einzelnen: **[Referenz der Buchseiten](REFERENZ-MARKDOWN.md)**.

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
Tippen aktualisiert und dem Schritt unter dem Cursor folgt, Fehler als rote Unterstreichung und Tooltips mit Definition und aktuellen Werten eines Objekts, das dabei in der Vorschau als Geist erscheint (auch versteckte Hilfsobjekte); ebenso eine Vorschau für die Markdown-Seiten eines Buchs. Dahinter steht
`geoanim daemon` (JSON-Zeilen über Standardein- und -ausgabe). Einrichten: siehe `editors/vscode/README.md`.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `GeoAnim.Core` | Geometrie (Punkte, Strecken, Kreise, Schnittpunkte, Abhängigkeiten), Zeitleiste (`Construction`: Schritte und Aktionen), Animationsdaten (`AnimationData.ToJson`) |
| `GeoAnim.Formulas` | Formeln: `MathText` zerlegt Texte in Text und `$…$`; `MathRenderer` lässt MathJax (AsciiMath → SVG, Schrift STIX2) in der JavaScript-Engine Jint laufen (Start unter einer Sekunde, dann wenige Millisekunden je Formel). MathJax und die Zeichendaten liegen in `mathjax/` und `mathjax-stix2-font/` als eingebettete Ressourcen |
| `GeoAnim.Script` | Skriptsprache: Lexer, Parser, Übersetzung in eine `Construction`; Fehler als `ScriptError` mit Zeile/Spalte |
| `GeoAnim.Viewer` | Player (`view/`: `player.html`, `player.js`, `player.css`, Schrift) und Seite für Markdown (`page.html`, `page.js`, `page.css`) als eingebettete Ressourcen; `HtmlExporter` für die eigenständige Datei; `MarkdownPage` liest eine Buchseite in Markdown (Markdig) |
| `GeoAnim` | Kommandozeile `geoanim serve` (EmbedIO, Dateibeobachtung, `/animation.json`) und `geoanim export` |
| `GeoAnim.Tests` | xUnit: Geometrie, Zeitleiste, Sprache, Formeln, Export, Server, alle Beispiele in `Samples` |

**Animationsdaten:** Ein Objekt besteht aus Grundelementen (`dot`, `line`, `arc`, `polygon`, `text`; Strahlen und Geraden mit `unbounded`) mit je einer Spur pro Eigenschaft: Schlüssel `[zeit, wert]` oder `[zeit, wert, verlauf]` (Zeit in ms; Verlauf `s` = Sprung, `i`/`o`/`io` = ease in/out/in-out, sonst linear). Zwischen zwei Schlüsseln interpoliert der Player mit dem Verlauf des späteren; vor dem ersten Schlüssel ist ein Element nicht da. Beim Schreiben fallen Schlüssel weg, die auf der Geraden zwischen ihren Nachbarn liegen (Koordinaten auf einen Viertelpunkt genau, sonst auf ein Tausendstel) – vor allem die alle 40 ms berechneten Werte während Bewegungen. Dazu kommen Titel, Bereich, Hintergrund, Gitter, Achsen, Gesamtdauer, die Schritte (`caption`, `start`, `end`) und die Formeln aller Texte (`math`: AsciiMath → `box` = viewBox in Tausendstel em mit der Grundlinie bei y = 0, `svg` = Pfade in `currentColor`).

## Lizenz

GeoAnim steht unter der MIT-Lizenz (`LICENSE`). Fremde Bestandteile:

| Bestandteil | Lizenz | wo |
|---|---|---|
| Schrift STIX Two Text (Fontsource, lateinische Zeichen) | SIL Open Font License 1.1 | `GeoAnim.Viewer/view/fonts/`, steckt in jedem Export |
| MathJax 4 (Auszug) und die Zeichendaten von MathJax STIX2 (aus den STIX-Two-Schriften, OFL 1.1) | Apache 2.0 | `GeoAnim.Formulas/mathjax*/`; im Export stehen nur die erzeugten Pfade |
| Jint | BSD 2-Clause | NuGet-Paket |
| EmbedIO | MIT | NuGet-Paket |
| Markdig | BSD 2-Clause | NuGet-Paket |
| YamlDotNet | MIT | NuGet-Paket |
| xUnit (nur Tests) | Apache 2.0 | NuGet-Paket |

## Ideen für später

- Winkelmarkierungen, Kreisbögen als eigene Objekte, gestrichelte Linien, Pfeile
- Farbe oder Breite eines Objekts in einem späteren Schritt ändern (etwa `change k color red`)
- Export als Video oder animiertes SVG
