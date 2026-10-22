include ../def.geo

title title Punktlage, Halbebenen und Schnittpunkte

# Größen, Tempo und Bildbereich kommen aus def.geo.
var hilfspunkt hidden nolabel
var punktname offset 0.3 0.15
var flaeche hidden width 0 opacity 0.16

# Die Flächen reichen über den Bildrand hinaus. Ihre äußeren Kanten
# sind keine geometrischen Grenzen der Halbebenen.
# Koordinaten passend zum Bildbereich aus def.geo: x +/-10, y +/-7.
point L = -12 -2 hilfspunkt
point R = 12 2 hilfspunkt
point OL = -12 9 hilfspunkt
point OR = 12 9 hilfspunkt
point UL = -12 -9 hilfspunkt
point UR = 12 -9 hilfspunkt
polygon oben = L R OR OL flaeche fill blue
polygon unten = L UL UR R flaeche fill green

point A = -6 -1 hilfspunkt
point B = 6 1 hilfspunkt

step Auf einer Seite der Geraden
info Der Punkt $P$ liegt auf einer Seite der Geraden $g$.
| Er liegt nicht auf der Geraden.

line g = A B opt_gg label "$g$" labelpos 1.1 offset 0 -0.45
point P = -3 3 opt_ani1 punktname

step Auf der Geraden
info Wir bewegen $P$ auf die Gerade $g$.
| Nun ist $P$ ein Punkt der Geraden.

move P to 0 0

step Auf der anderen Seite
info Wir bewegen $P$ weiter auf die andere Seite der Geraden.
| Damit haben wir drei mögliche Lagen gesehen: auf der einen Seite,
| auf der Geraden oder auf der anderen Seite.

move P to -3 -4

step Zwei Halbebenen
info Eine Gerade teilt die Ebene in zwei **Halbebenen**.
| Die Gerade bildet ihre gemeinsame Grenze.
|
| Hier zählen wir die Grenzgerade nicht zu den Halbebenen.
| Punkte auf ihr liegen auf der Grenze zwischen beiden.
| Die farbigen Flächen zeigen nur Ausschnitte der unbegrenzten Halbebenen.

together
  show oben unten
  text obenname = "eine Halbebene" at 3 4
  text untenname = "die andere Halbebene" at 3 -4
end

step Von einer Halbebene in die andere
info Beim Wechsel von einer Halbebene in die andere muss $P$ die Gerade überqueren.
| Das gilt auch für einen Umweg: Die Gerade setzt sich in beiden Richtungen unbegrenzt fort.

move P to -3 3

step Schnittpunkt zweier Geraden
info Die Geraden $g$ und $h$ haben einen gemeinsamen Punkt $S$.
| Man sagt, sie schneiden sich in diesem Punkt.
| $S$ liegt auf beiden Geraden und heißt ihr **Schnittpunkt**.
|
| Zwei verschiedene Geraden können höchstens einen gemeinsamen Punkt haben.
| Durch zwei verschiedene Punkte verläuft genau eine Gerade.

together
  hide P oben unten obenname untenname
end
point C = -3 -4 hilfspunkt
point D = 3 4 hilfspunkt
line h = C D opt_gg2 label "$h$" labelpos 1.2 offset 0.65 0
point S = intersect g h opt_r1 punktname offset 0 -0.6
