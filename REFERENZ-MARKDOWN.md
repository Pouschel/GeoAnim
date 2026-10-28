# GeoAnim – Referenz der Buchseiten in Markdown

Alles, was GeoAnim über gewöhnliches Markdown hinaus in Buchseiten (`….geo.md`) versteht, zum Nachschlagen. Die
Skriptsprache der `.geo`-Dateien steht in der [Referenz](REFERENZ.md), der Überblick in der [README](README.md).

**Stand:** im Aufbau. Bisher gibt es die Vorschau in VS Code; der Export (Web, E-Book, Druck) folgt.

## Inhalt

- [Datei](#datei)
- [Kopf](#kopf): `title`, `description`, `keywords`
- [Text und Formeln](#text-und-formeln)
- [Abbildungen](#abbildungen): Verweise, Schritte, eigenes Skript
- [Buch und Web](#buch-und-web): Bildfolge, Zuschnitt
- [Attribute](#attribute): `crop`, `float`, `width`
- [Fehler und Warnungen](#fehler-und-warnungen)
- [Beispiel](#beispiel)

## Datei

| | |
|---|---|
| Name | `….geo.md`, neben den Skripten – meist wie das Skript der Seite: `10_Punkte.geo.md` zu `10_Punkte.geo` |
| Warum die Doppelendung | VS Code gibt den Seiten eine eigene Sprache („GeoAnim-Markdown“: Vorschau, Hervorhebung, Gliederung) ohne Konflikt mit seiner Markdown-Unterstützung; andere Programme (GitHub, Markdown-Editoren) zeigen sie weiter als Markdown |
| Aufgabenteilung | das Skript beschreibt Bild und Ablauf (mit kurzen Schrittbeschreibungen), die Seite den Text |

## Kopf

Ein YAML-Block zwischen zwei `---`-Zeilen ganz am Anfang der Datei; alle Einträge sind freiwillig.

| Schlüssel | Schreibweise | Bedeutung |
|---|---|---|
| `title` | `title: Punkte und ihre Lage` | Name der Seite in Menü, Inhaltsverzeichnis und Seitenkopf; ohne ihn gilt die erste `#`-Überschrift (so kann der Name kürzer sein als die Überschrift) |
| `description` | `description: Was ein Punkt ist …` | Kurzbeschreibung (Übersicht, Suchmaschinen) |
| `keywords` | `keywords: [Punkt/e/en, Lage]` | Stichwörter, Schreibweise wie bei `keyword` in Skripten; auch eines allein oder eines je Zeile mit `- ` |

Ein unbekannter Schlüssel ist eine Warnung, ein Fehler im YAML ein Fehler (mit Zeile).

## Text und Formeln

| Element | Schreibweise | Hinweise |
|---|---|---|
| Text | CommonMark | Überschriften, Hervorhebung, Listen, Zitate, Code, Links |
| Tabellen | `\| a \| b \|` mit Trennzeile `\|---\|---\|` | |
| Fußnoten | `Text[^1]` · `[^1]: Anmerkung` | |
| Attribute | `{#marke .klasse}` hinter einem Element | |
| Formel im Text | `$bar(AB)$` | AsciiMath wie in Skripten; kein Leerzeichen direkt nach dem ersten und vor dem letzten `$` |
| abgesetzte Formel | `$$` … `$$` auf eigenen Zeilen | zentriert auf einer Zeile für sich |
| Dollarzeichen | `\$` | |
| Sprungmarke | `[nach oben](#oben)` | ein Link mit `#…` bleibt ein Link auf eine Stelle der Seite |
| andere Seite | `[Strecken](20_Strecken.geo.md)` | öffnet in der Vorschau diese Seite |

Ein `$`, das keine Formel beginnt (kein schließendes, oder ein Leerzeichen dahinter), ist ein Fehler – wie in Skripten.

## Abbildungen

Ein **Bild** auf ein Skript, **allein im Absatz**; mehrere solche Bilder in einem Absatz bilden eine Abbildung und
stehen nebeneinander. Der Text in den eckigen Klammern ist die Bildunterschrift (Markdown und Formeln erlaubt; bei
mehreren Bildern mit `·` verbunden). Ein Bild mitten im Text bleibt ein gewöhnliches Bild.

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

## Attribute

In geschweiften Klammern direkt hinter dem Bild, mehrere mit Leerzeichen: `![](#2){float=right width=40%}`.

| Attribut | Werte | Bedeutung |
|---|---|---|
| `crop` | `yes` (Voreinstellung) · `no` | im Buch auf den Inhalt zuschneiden oder die ganze `view` zeigen; gilt für das Bild |
| `float` | `left` · `right` | der folgende Text fließt um die Abbildung – **festgelegt, noch nicht umgesetzt**: im Web und im E-Book später, im gedruckten Buch bleiben Abbildungen zentriert |
| `width` | `40%` · `60mm` | Breite der Abbildung (zu `float`) – **festgelegt, noch nicht umgesetzt** |

`float` und `width` gelten für die ganze Abbildung; haben zwei ihrer Bilder verschiedene Werte, ist das ein Fehler.

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

Warnungen (gelb): ein unbekannter Schlüssel im Kopf, ein unbekanntes Attribut.

## Beispiel

`10_Punkte.geo.md` neben `10_Punkte.geo`:

```markdown
---
title: Punkte und ihre Lage
description: Was ein Punkt ist, wie wir ihn bezeichnen und wann zwei Punktnamen dieselbe Lage beschreiben.
keywords: [Punkt/e/en]
---

# Punkte und ihre Lage

![](#2)

Ein **Punkt** beschreibt eine genaue Lage. Er hat keine Ausdehnung.

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
