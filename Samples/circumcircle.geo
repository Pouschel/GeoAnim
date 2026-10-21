# Der Umkreis eines Dreiecks
title Umkreis eines Dreiecks
view x 0 .. 12 y 0 .. 8
grid
var mittel color teal width 2

point A = 2 1.5
point B = 9.5 2
point C = 5 6.5
polygon D = A B C fill lightblue opacity 0.25 width 2.5

step Mittelsenkrechte der Seite AB.
  point Mc = midpoint A B hidden
  circle a1 = center A through B hidden
  circle b1 = center B through A hidden
  point P1 Q1 = intersect a1 b1 hidden
  line mc = P1 Q1 mittel

step Mittelsenkrechte der Seite BC.
  circle b2 = center B through C hidden
  circle c2 = center C through B hidden
  point P2 Q2 = intersect b2 c2 hidden
  line ma = P2 Q2 mittel

step Die Mittelsenkrechten schneiden sich im Umkreismittelpunkt U.
  point U = intersect mc ma color red

step Der Kreis um U durch A geht auch durch B und C.
  circle u = center U through A color red width 3 duration 1600

step Das gilt für jedes Dreieck: wir verschieben C.
  move C to 8 6.8 duration 2000
  pause 300
  move C to 3 5 duration 2000
