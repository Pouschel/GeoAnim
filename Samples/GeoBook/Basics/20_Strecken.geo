include ../def.geo

title Strecken und ihre Länge

# Für diese Seite: Strecke mit Überstrich, Länge mit senkrechten Strichen.
# Alle Vergleiche sind reine Verschiebungen. Beide Endpunkte einer
# Vergleichsstrecke bewegen sich gleichzeitig um denselben Vektor.
speed 0.75

var vergleich color orange width 5
var einheit color green width 5
var teil color orange width 6
var hilfspunkt hidden nolabel

step Definition
info Die gerade Verbindung zwischen zwei verschiedenen 
|Punkten heißt **Strecke**.

together
  point A = -6 1.5 color blue  offset -0.4 0.2
  point B = 6 1.5 color blue  offset 0.25 0.2
end
pause 500
segment AB = A B color blue label "$bar(AB)$" nolabel offset 0 -0.85 duration 1500

step Endpunkte und Bezeichnung
info $A$ und $B$ heißen **Endpunkte** der Strecke. Sie gehören zur Strecke.
| Wir schreiben $bar(AB)$. Auch $bar(BA)$ bezeichnet dieselbe Strecke.

show label A B AB

step Streckenlänge
info Die **Länge einer Strecke** ist der Abstand zwischen ihren Endpunkten.
| Wir schreiben dafür $|AB|$.
| Bewegt sich ein Endpunkt näher an den anderen heran oder weiter weg, ändert sich die Länge.

pause 1000
move B to 2 1.5
pause 700
move B to 7 1.5
pause 700
move B to 6 1.5

hide label AB

step Kürzer
info Zum Vergleichen legen wir die Strecken mit einem Endpunkt aneinander und richten sie gleich aus.
| Beim Verschieben bleiben ihre Längen unverändert.
| $bar(CD)$ endet früher als $bar(AB)$: $bar(CD)$ ist **kürzer**. Es gilt $|CD| < |AB|$.

together
  point C = -5 -2 color orange offset -0.6 -0.7
  point D = 3 -2 color orange offset 0.2 -0.7
end
segment CD = C D vergleich
pause 1200
#hide label C
together
  move C to -6 1.5
  move D to 2 1.5
end
text kurz = "$|CD| < |AB|$" at 0 4 

step Länger
info $bar(EF)$ ist **länger** als $bar(AB)$.
| Wenn die linken Endpunkte zusammenfallen, reicht $bar(EF)$ über $B$ hinaus.
| Es gilt $|EF| > |AB|$.

together
  hide C D CD kurz
  point E = -7 -2 color orange offset -0.6 -0.7
  point F = 8 -2 color orange offset 0.2 -0.7
end
segment EF = E F vergleich
pause 1200
#hide label E
together
  move E to -6 1.5
  move F to 9 1.5
end
text lang = "$|EF| > |AB|$" at 0 4 


step Gleich lang
info Lassen sich zwei Strecken vollständig zur Deckung bringen, sind sie **gleich lang**.
| Hier fallen beide Endpunktpaare zusammen. Es gilt $|GH| = |AB|$.

together
  hide E F EF lang
  point G = -6 -2 color orange offset -0.6 -0.7
  point H = 6 -2 color orange offset 0.2 -0.7
end
segment GH = G H vergleich
pause 1200
#hide label G H
together
  move G to A
  move H to B
end
text gleich = "$|GH| = |AB|$" at 0 4 color black

var tsz size 32

step Eine Längeneinheit festlegen
info Zum Messen wählen wir eine **Einheitsstrecke**.
| Ihre Länge ist unsere Längeneinheit. Die grüne Strecke 
| steht hier für **eine Einheit**.

together
  hide G H GH gleich
  point U = -6 -2.5 color green nolabel
  point V = -3 -2.5 color green nolabel
end
segment UV = U V einheit
text einheitsname = "Einheitsstrecke" at -4.5 -3.4 tsz size 32 color green
text einheitsmass = "1 Einheit" at -4.5 -1.6 tsz color green

step Messen
info Beim **Messen** vergleichen wir eine Länge mit der 
| festgelegten Längeneinheit.  Wir legen gleich lange Kopien 
| der Einheitsstrecke lückenlos aneinander.
| Jede Kopie behält beim Verschieben ihre Länge.

# Kopie 1: Länge 3, Verschiebung (0, 4).
point U_1 = copy U hilfspunkt
point V_1 = copy V hilfspunkt
segment mess1 = U_1 V_1 teil duration 250
together
  move U_1 to -6 1.5
  move V_1 to -3 1.5
end
point mark1 = -3 1.5 color black nolabel size 4 duration 150
text nummer1 = "1" at -4.5 0.5  tsz
text zaehler1 = "1 Einheit abgetragen" at 3 -2.5 color black tsz
pause 800

# Kopie 2: Länge 3, Verschiebung (3, 4).
point U_2 = copy U hilfspunkt
point V_2 = copy V hilfspunkt
segment mess2 = U_2 V_2 teil color brown duration 250
together
  move U_2 to -3 1.5
  move V_2 to 0 1.5
end
point mark2 = 0 1.5 color black nolabel size 4 duration 150
text nummer2 = "2" at -1.5 0.5  tsz
hide zaehler1 duration 100
text zaehler2 = "2 Einheiten abgetragen" at 3 -2.5 color black tsz
pause 800

# Kopie 3: Länge 3, Verschiebung (6, 4).
point U_3 = copy U hilfspunkt
point V_3 = copy V hilfspunkt
segment mess3 = U_3 V_3 teil duration 250
together
  move U_3 to 0 1.5
  move V_3 to 3 1.5
end
point mark3 = 3 1.5 color black nolabel size 4 duration 150
text nummer3 = "3" at 1.5 0.5  tsz
hide zaehler2 duration 100
text zaehler3 = "3 Einheiten abgetragen" at 3 -2.5 color black tsz
pause 800

# Kopie 4: Länge 3, Verschiebung (9, 4).
point U_4 = copy U hilfspunkt
point V_4 = copy V hilfspunkt
segment mess4 = U_4 V_4 teil color brown duration 250
together
  move U_4 to 3 1.5
  move V_4 to 6 1.5
end
text nummer4 = "4" at 4.5 0.5 color black tsz
hide zaehler3 duration 100
text zaehler4 = "4 Einheiten abgetragen" at 3 -2.5 color black tsz

step Messergebnis
info Die Einheitsstrecke passt **genau viermal** in $bar(AB)$.
| Die Strecke $bar(AB)$ ist daher **vier Einheiten lang**.
| Messen heißt hier: vergleichen und zählen, wie oft die gewählte Einheit hineinpasst.

text ergebnis = "Länge von $bar(AB)$: **4 Einheiten**" at 0 4 tsz

