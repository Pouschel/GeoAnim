# GeoAnim für VS Code

Syntaxhervorhebung, Live-Vorschau und Fehlermeldungen für GeoAnim-Skripte (`.geo`) und Buchseiten in Markdown (`.geo.md`). Die Grammatik steht in `syntaxes/geo.tmLanguage.json` (Seiten: `geomd.tmLanguage.json`, die Markdown-Grammatik von VS Code, mit den Formeln aus `geomd-math.tmLanguage.json`).

**Kategorien:** Befehle am Zeilenanfang, Hilfswörter (`midpoint`, `center`, `to`, `use` …), Optionen, Zahlen, Farben, neue
Objektnamen (links vom `=`), Vars (bei `var` und dort, wo Optionen stehen), Texte, Formeln `$…$`, Dateipfade (`include`,
`use`), Kommentare. Die Farben kommen aus dem Farbschema des Editors.

## Einrichten

Den Ordner `editors/vscode` als Erweiterung einbinden, ohne Marketplace – in einer Eingabeaufforderung im Repository:

```
mklink /J "%USERPROFILE%\.vscode\extensions\geoanim-geo" "%CD%\editors\vscode"
```

(Eine Verknüpfung, damit Änderungen an der Erweiterung ohne Kopieren wirken.) Danach VS Code neu starten – nach einer
Änderung an der Erweiterung ebenso (oder „Developer: Reload Window“).

## Vorschau und Fehler

- **Vorschau:** Symbol oben rechts im Editor, Strg+K V oder „GeoAnim: Vorschau öffnen“. Sie zeigt die Animation neben
  dem Editor, aktualisiert sich schon beim Tippen (nicht erst beim Speichern) und bleibt dabei am aktuellen Schritt. Sie
  folgt der jeweils aktiven `.geo`-Datei.
- **Schritt folgt Cursor:** Steht der Cursor in einem Schritt, zeigt die Vorschau dessen Ende (vor dem ersten `step`:
  das Startbild). Gezählt werden die `step`-Zeilen der Datei selbst.
- **Cursor folgt Schritt:** Gehst du in der Vorschau selbst zu einem Schritt (Schaltflächen, Tasten, Schrittliste,
  Schieberegler) oder hält die Wiedergabe an, springt der Cursor auf dessen `step`-Zeile (beim Startbild an den
  Dateianfang) – nicht, wenn er schon in diesem Schritt steht, und nicht während die Animation läuft. Der Fokus bleibt
  in der Vorschau.
- **together-Blöcke** (Strg+. oder die Glühbirne): Sind Zeilen markiert, schließt „In together … end einschließen“ sie
  ein (eine Stufe weiter eingerückt; eine Dauer schreibt man bei Bedarf selbst dahinter). Für `.geo`-Dateien ist eine Einrückungsstufe zwei Leerzeichen (Voreinstellung der Erweiterung; in den Einstellungen unter `[geo]` änderbar). Steht der
  Cursor in einem together-Block, löst „together-Block auflösen“ ihn auf und rückt die Zeilen wieder aus.
- **Fortsetzungszeilen:** Enter in einer Zeile mit `info`, `description`, `set name = …` oder in einer `|`-Zeile beginnt
  die nächste Zeile mit `| ` (zweimal Rücktaste, wenn du nicht weiterschreiben willst). Nach `title` und `step` nicht.
- **Fehler** werden rot unterstrichen und stehen unter „Probleme“, Fehler in eingebundenen Dateien dort. In der
  Vorschau erscheint der Fehler über der letzten guten Animation.
- **Schlüsselwörter eines Buchs:** Gehört das Skript zu einem Buch (ein Ordner darüber hat eine `index.yaml`), setzt die
  Vorschau die Links der Schlüsselwörter wie `geoanim exportdir` – so sieht man schon beim Schreiben, welche Wörter
  verlinkt werden. Ein Klick öffnet das Skript mit dem Schlüsselwort, bei einem Schlüsselwort hinter einer `step`-Zeile
  steht der Cursor dann auf diesem Schritt. Die anderen Skripte werden dafür nur gelesen, nicht übersetzt (es zählt ihr
  gespeicherter Stand). Hat ein anderes Skript dasselbe Schlüsselwort, ist die `keyword`-Zeile gelb unterstrichen, mit der
  anderen Stelle im Hinweis; wie beim Export behält es das Skript, das im Buch zuerst kommt.
- **Links** in Titel und Beschreibung: `[Satz 1](Prop1.html)` öffnet `Prop1.geo` im Editor, Links auf Webseiten den
  Browser.
- **Tooltip und Definition:** Fährt die Maus über den Namen einer Var, eines Textes (`set`) oder eines Objekts, zeigt
  ein Tooltip seine Definition (die Zeile, beim Text den Text) und woher sie stammt, auch aus eingebundenen Dateien.
  F12 oder Strg+Klick springt zur Definition, Alt+F12 blendet sie zum Ändern an Ort und Stelle ein. Solange das Skript
  einen Fehler hat, gelten die zuletzt vollständig gelesenen Definitionen.
- **Werte und Geister:** Ist die Vorschau offen, zeigt das Tooltip eines Objekts zusätzlich seine Werte im aktuellen
  Vorschaubild (Koordinaten, bei Strecken die Länge, bei Kreisen Mittelpunkt und Radius, bei Winkeln die Größe), ob es
  sichtbar, ausgeblendet oder gar nicht gezeichnet ist (`hidden`), und seine Farbe – bzw. dass es dort noch nicht
  definiert ist. Zugleich erscheint das Objekt in der Vorschau als orange gestrichelter „Geist“, auch wenn es versteckt
  ist (Hilfspunkte, Hilfsgeraden): für ein paar Sekunden nach dem Tooltip, und solange der Cursor auf seinem Namen steht. Seine Beschriftung erscheint dabei mit, an der Stelle, an der sie stehen wird (mit `shift`), auch wenn Objekt oder Beschriftung noch verborgen sind – so lässt sich eine Beschriftung platzieren, bevor sie zu sehen ist. Ebenso erscheinen versteckte Texte (außer Texten unter anderen, `below`).
  Der Geist folgt der Animation, auch beim Abspielen.
- **Vom Bild zum Skript:** In der Vorschau mit gedrückter `Strg` (macOS: `Cmd`) über die Zeichnung fahren zeigt den Namen
  des Objekts unter der Maus (auch über seiner Beschriftung); `Strg`+Klick springt zu seiner Definition, zeigt das Tooltip
  mit den Werten und den Geist – die Vorschau bleibt dabei an ihrem Schritt. Dünne Linien muss man nicht genau treffen;
  liegen Dinge übereinander, gewinnen Punkte vor Linien vor Flächen.
- **Gliederung:** Die Ansicht „Gliederung“, die Brotkrumen über dem Editor und `Strg+Umschalt+O` zeigen das Startbild und
  die Schritte (nummeriert wie im Player, `textstep` gekennzeichnet), darunter jeweils die dort definierten Objekte,
  Zahlen, Vars und Texte mit Symbolen nach Art; ein Klick springt hin. Mit „Sticky Scroll“ steht oben im Editor, in welchem
  Schritt man gerade schreibt.
- **Falten:** Jeder Schritt (bis zum nächsten) und jeder `together`-Block lässt sich einklappen.
- **Farbfelder:** Hinter `color`, `fill` und `background` steht ein Farbquadrat (Namen und `#rrggbb`); ein Klick öffnet den
  Farbwähler, die neue Farbe wird als `#rrggbb` eingesetzt (oder als Name, wenn sie genau eine benannte Farbe ist).
- **Umbenennen** (F2): benennt ein Objekt, eine Var, einen Text (`set`) oder eine Zahl in der ganzen Datei um – überall,
  wo der Name als Name steht (in Anweisungen, als Var unter den Optionen, als Text bei `text`/`label`, im Platzhalter
  `{l}`), nicht aber Wörter in Beschreibungen und Formeln (`$bar(AB)$` bleibt). Der neue Name wird geprüft (kein
  Schlüsselwort, nicht vergeben). Namen aus eingebundenen Dateien lassen sich noch nicht umbenennen.
- **Alle Verweise** (Umschalt+F12) listet die Stellen eines Namens; steht der Cursor auf einem Namen, sind seine
  Vorkommen in der Datei hervorgehoben.
- Koordinaten unter der Maus und Umschalt+Klick zum Kopieren gibt es wie bei `geoanim serve`.
- **Buchseiten in Markdown (`….geo.md`,** Schreibweise in der [Referenz der Buchseiten](../../REFERENZ-MARKDOWN.md)**):** Sie haben eine eigene Sprache, „GeoAnim-Markdown“: Hervorhebung wie Markdown
  (mit dem YAML-Kopf), dazu Formeln `$…$` und `$$ … $$`; die Gliederung zeigt die Überschriften verschachtelt,
  Abschnitte, Kopf und Codeblöcke lassen sich falten; Zeilen werden umgebrochen angezeigt. Gewöhnliche `.md`-Dateien
  bleiben bei der Markdown-Unterstützung von VS Code. Es gibt dieselbe Vorschau wie für Skripte – Symbol oben rechts,
  Strg+K V oder „GeoAnim: Vorschau öffnen“. Sie zeigt die Seite, wie GeoAnim sie setzt (Schrift, Formeln als SVG, Abbildungen). Oben rechts
  schaltet „Buch“/„Web“ um, wie die Abbildungen erscheinen: wie im Buch (Voreinstellung; `#2` ein Standbild, `#3..4` die
  Standbilder der Schritte nebeneinander – höchstens drei in einer Reihe – mit Nummer und Schrittbeschreibung, eine ganze
  Animation ihr letztes Bild; jede Abbildung auf ihren Inhalt zugeschnitten, außer mit `{crop=no}`) oder wie im Web
  (Player). Die Vorschau aktualisiert sich beim Tippen und zeigt dieselbe Stelle wie der Editor (beim Blättern im Editor blättert
  sie mit). Abbildungen, an denen sich nichts ändert, bleiben beim Tippen stehen (eine laufende Animation läuft weiter);
  sie zeigen die Skripte, wie sie gespeichert sind – nach dem Speichern eines Skripts aktualisiert sich die Seite, auch
  wenn das Skript in einem anderen VS-Code-Fenster (oder einem anderen Programm) gespeichert wurde: die Erweiterung
  beobachtet die Skripte und Seiten des Buchs, auch außerhalb des geöffneten Ordners. Fehler
  (eine Formel ohne schließendes `$`, ein fehlendes oder fehlerhaftes Skript, ein unbekannter Schritt, ein Fehler im Kopf)
  werden unterstrichen und stehen über der Seite. Ein Link auf eine andere Seite (`….geo.md`) öffnet diese. Die Marke
  eines Schritts (`step … #marke`) ist im Skript hervorgehoben. **Verweise:** nach `§` bietet die Vervollständigung die
  Marken des ganzen Buchs an (Abbildungen, Aussagen, Abschnitte – mit Nummer und Text), in `![](#` bzw.
  `![](datei.geo#` (auch nach `..`) die Schritte des Skripts mit Nummer und Schrittbeschreibung; das Tooltip auf
  `§name` zeigt das Ziel, F12 bzw. Strg+Klick springt zur Marke (auch auf eine andere Seite), ebenso ein Klick auf einen
  Verweis in der Vorschau. **Editor und Vorschau gemeinsam:** Der Absatz, in dem der Cursor steht, ist in der Vorschau
  links gelb markiert; ein markierter Text (auf einer Zeile, ohne Formeln) ist dort gelb hinterlegt – genau die Stelle,
  auch wenn das Wort im Absatz mehrmals vorkommt – und die Vorschau blättert hin, wenn sie nicht zu sehen ist.
  Umgekehrt setzt ein Doppelklick (oder Strg+Klick) auf ein Wort oder eine Formel in der Vorschau den Cursor im Editor
  genau dorthin (markiert); die Vorschau bleibt dabei stehen. **Stichwörter** (`**Strahl**{kw=/s/en}`) der anderen
  Seiten und Skripte sind in der Vorschau blau verlinkt (erstes Vorkommen je Abschnitt; Tooltip: Stichwort und Zielseite);
  ein Klick öffnet die Zielseite im Editor an der Stelle des Stichworts (ein Skript an seinem Schritt). Ein Stichwort, das
  eine andere Seite schon hat, ist gelb unterstrichen. Seiten haben ihre eigene Vorschau neben der
  der Skripte: Beide können gleichzeitig offen sein (etwa in zwei Fenstern), und jede folgt dem aktiven Editor ihrer Art.
  **Aussagen** (Strg+. oder die Glühbirne): Sind Zeilen markiert, schließt „In Definition einschließen“, „In Axiom …“,
  „In Satz …“, „In Hilfssatz …“, „In Folgerung …“, „In Beweis …“, „In Beispiel …“, „In Bemerkung …“ oder „In Kasten …“
  sie in `::: art` … `:::` ein – mit einer Leerzeile davor und danach, wo der Text weitergeht; der Cursor steht dann
  hinter der Art, für Marke und Titel (`{#name} Titel`). Enthalten die Zeilen schon einen Container, bekommt der neue
  außen einen Doppelpunkt mehr (`:::: box`). Steht der Cursor in einem Container, löst „Axiom-Block auflösen“ (usw.)
  den innersten auf: Eröffnungs- und Schlusszeile fallen weg, der Inhalt bleibt. Container lassen sich falten.

Die Arbeit macht `geoanim daemon`, einmal im Hintergrund gestartet (MathJax startet daher nur einmal). Gefunden wird
`geoanim` so: die Einstellung `geoanim.path`, sonst der neuere der Builds `GeoAnim\bin\Debug\net10.0\geoanim.exe` und
`…\Release\…` im Arbeitsbereich oder in einem Ordner über der geöffneten Datei – auch in einem Ordner `GeoAnim` daneben, so dass Skripte in einem eigenen Repository neben dem Code (`C:\Code\GeoBook` neben `C:\Code\GeoAnim`) ihn ebenfalls finden –, sonst `geoanim` aus dem Suchpfad. Liegt der Code woanders, zeigt `geoanim.path` auf den Ordner des GeoAnim-Repositorys (oder direkt auf `geoanim.exe`). Ein Build
läuft als Kopie aus dem Temp-Ordner, damit Visual Studio weiter bauen kann; nach einem neuen Build (Debug oder Release)
startet die Erweiterung den neuesten von selbst, und die Vorschau lädt nach. „GeoAnim: Neu starten“ startet `geoanim` von Hand neu; Meldungen stehen im Ausgabefenster „GeoAnim“.

## Grenzen

Die Grammatik arbeitet mit Mustern, Zeile für Zeile, und weiß nicht, welche Vars definiert sind. Ein Wort an einer Stelle,
an der nur noch Optionen stehen können (etwa nach den zwei Punkten einer Strecke), gilt als Var; in Namenslisten
(`polygon`, `show`, `hide`, Eingaben bei `use`) gilt ein Wort bis zur ersten Option als Objektname.

Kommen Schlüsselwörter, Optionen oder Farben zur Sprache hinzu, gehören sie auch in die Grammatik – der Test
`GrammarTests` prüft das.
