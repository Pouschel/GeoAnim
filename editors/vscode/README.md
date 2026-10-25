# GeoAnim für VS Code

Syntaxhervorhebung, Live-Vorschau und Fehlermeldungen für GeoAnim-Skripte (`.geo`). Die Grammatik steht in `syntaxes/geo.tmLanguage.json`.

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
  ein (eine Stufe weiter eingerückt; eine Dauer schreibt man bei Bedarf selbst dahinter). Steht der
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
  ist (Hilfspunkte, Hilfsgeraden): für ein paar Sekunden nach dem Tooltip, und solange der Cursor auf seinem Namen steht.
  Der Geist folgt der Animation, auch beim Abspielen.
- **Umbenennen** (F2): benennt ein Objekt, eine Var, einen Text (`set`) oder eine Zahl in der ganzen Datei um – überall,
  wo der Name als Name steht (in Anweisungen, als Var unter den Optionen, als Text bei `text`/`label`, im Platzhalter
  `{l}`), nicht aber Wörter in Beschreibungen und Formeln (`$bar(AB)$` bleibt). Der neue Name wird geprüft (kein
  Schlüsselwort, nicht vergeben). Namen aus eingebundenen Dateien lassen sich noch nicht umbenennen.
- **Alle Verweise** (Umschalt+F12) listet die Stellen eines Namens; steht der Cursor auf einem Namen, sind seine
  Vorkommen in der Datei hervorgehoben.
- Koordinaten unter der Maus und Umschalt+Klick zum Kopieren gibt es wie bei `geoanim serve`.

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
