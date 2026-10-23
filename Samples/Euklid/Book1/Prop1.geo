
include ../_def.geo

title Auf einer gegebenen Strecke ein gleichseitiges Dreieck errichten.

view x -10 .. 10 y -7 .. 7
grid 0.5
speed 0.7

step Gegeben ist die Verbindungsstrecke $s$ durch die Punkte $A$ und $B$
input point A = -3 0 offset -0.4 0
input point B = 3 0
segment s = A B color blue

step Wir zeichnen um $A$ einen Kreis mit dem Radius $|AB|$
circle k_1 = center A through B color cyan

step Wir zeichnen um $B$ einen Kreis mit dem Radius $|AB|$
circle k_2 = center B through A color cyan

step $C$ sein ein Schnittpunkt der beiden Kreise
point C = intersect k_1 k_2 offset 0 0.2

step $C$ mit $A$ und $B$ verbinden
segment ca = C A color blue
segment cb = C B color blue

step $ABC$ ist das gesuchte gleichseitige Dreieck
polygon D = A B C  color lightblue 
output C

step Beweis
set t1c=Nach Konstruktion ist
|
| $bar(AC) = bar (AB)$
text t1= t1c at -5.5 3
use ../_SameLenAni.geo A C A B

set t1c=Nach Konstruktion ist
|
| $bar(BC) = bar (AB)$
text t2= t1c at 4.7 3
use ../_SameLenAni.geo B C B A



