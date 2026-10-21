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
- **Fehler** werden rot unterstrichen und stehen unter „Probleme“, Fehler in eingebundenen Dateien dort. In der
  Vorschau erscheint der Fehler über der letzten guten Animation.
- Koordinaten unter der Maus und Umschalt+Klick zum Kopieren gibt es wie bei `geoanim serve`.

Die Arbeit macht `geoanim daemon`, einmal im Hintergrund gestartet (MathJax startet daher nur einmal). Gefunden wird
`geoanim` so: die Einstellung `geoanim.path`, sonst der neuere der Builds `GeoAnim\bin\Debug\net10.0\geoanim.exe` und
`…\Release\…` im Arbeitsbereich oder in einem Ordner über der geöffneten Datei, sonst `geoanim` aus dem Suchpfad. Ein Build
läuft als Kopie aus dem Temp-Ordner, damit Visual Studio weiter bauen kann; nach einem neuen Build (Debug oder Release)
startet die Erweiterung den neuesten von selbst, und die Vorschau lädt nach. „GeoAnim: Neu starten“ startet `geoanim` von Hand neu; Meldungen stehen im Ausgabefenster „GeoAnim“.

## Grenzen

Die Grammatik arbeitet mit Mustern, Zeile für Zeile, und weiß nicht, welche Vars definiert sind. Ein Wort an einer Stelle,
an der nur noch Optionen stehen können (etwa nach den zwei Punkten einer Strecke), gilt als Var; in Namenslisten
(`polygon`, `show`, `hide`, Eingaben bei `use`) gilt ein Wort bis zur ersten Option als Objektname.

Kommen Schlüsselwörter, Optionen oder Farben zur Sprache hinzu, gehören sie auch in die Grammatik – der Test
`GrammarTests` prüft das.
