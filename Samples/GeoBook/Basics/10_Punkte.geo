include ../def.geo

title Punkte und ihre Lage

step Definition 
info Ein **Punkt** beschreibt eine genaue Lage. Er hat keine Ausdehnung. Damit wir ihn sehen können, stellen wir ihn durch einen kleinen Kreis dar.

var pdef  nolabel color blue size 5
point P= 0 0 pdef color black
point Q= 4 6 pdef
point A= -8 4 pdef color red
point B= 7 -2 pdef color green
point M= -9 -6 pdef color red

step Bezeichnung 
info Punkte werden in der Regel mit Großbuchstaben 
|wie $A$, $P$ oder $Q$ bezeichnet.
show label A B P Q M
pause 1000
step Lagebeziehung
info Die Punkte $M$ und $Q$ liegen an verschiedenen Orten. 
| Sie sind verschiedene Punkte. Schreibweise: $M != Q$.
hide A B P
pause 500
together
  move M to P
  move Q to P
end
info Fallen $M$ und $Q$ zusammen, bezeichnen beide Buchstaben 
|denselben Punkt. Man schreibt $M = Q$.













