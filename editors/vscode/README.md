# GeoAnim für VS Code und Visual Studio

Syntaxhervorhebung für GeoAnim-Skripte (`.geo`). Die Grammatik `syntaxes/geo.tmLanguage.json` dient beiden Editoren.

**Kategorien:** Befehle am Zeilenanfang, Hilfswörter (`midpoint`, `center`, `to`, `use` …), Optionen, Zahlen, Farben, neue
Objektnamen (links vom `=`), Vars (bei `var` und dort, wo Optionen stehen), Texte, Formeln `$…$`, Dateipfade (`include`,
`use`), Kommentare. Die Farben kommen aus dem Farbschema des Editors.

## VS Code

Den Ordner `editors/vscode` als Erweiterung einbinden, ohne Marketplace – in einer Eingabeaufforderung im Repository:

```
mklink /J "%USERPROFILE%\.vscode\extensions\geoanim-geo" "%CD%\editors\vscode"
```

(Eine Verknüpfung, damit Änderungen an der Grammatik ohne Kopieren wirken; alternativ den Ordner dorthin kopieren.)
Danach VS Code neu starten. Strg+# schaltet Kommentare um; `"` und `$` werden automatisch geschlossen.

## Visual Studio

Visual Studio liest TextMate-Grammatiken aus `%USERPROFILE%\.vs\Extensions\<Sprache>\Syntaxes\`:

```
mkdir "%USERPROFILE%\.vs\Extensions\GeoAnim\Syntaxes"
copy editors\vscode\syntaxes\geo.tmLanguage.json "%USERPROFILE%\.vs\Extensions\GeoAnim\Syntaxes\"
```

Danach offene `.geo`-Dateien schließen und neu öffnen. Visual Studio übernimmt nur die Farben (keine
Kommentar-Tastenkürzel). Nach einer Änderung der Grammatik die Datei erneut kopieren.

## Grenzen

Die Grammatik arbeitet mit Mustern, Zeile für Zeile, und weiß nicht, welche Vars definiert sind. Ein Wort an einer Stelle,
an der nur noch Optionen stehen können (etwa nach den zwei Punkten einer Strecke), gilt als Var; in Namenslisten
(`polygon`, `show`, `hide`, Eingaben bei `use`) gilt ein Wort bis zur ersten Option als Objektname.

Kommen Schlüsselwörter, Optionen oder Farben zur Sprache hinzu, gehören sie auch in die Grammatik – der Test
`GrammarTests` prüft das.
