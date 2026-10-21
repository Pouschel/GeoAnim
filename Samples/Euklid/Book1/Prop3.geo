include ../def.geo

title Streckensubtraktion

view x -10 .. 10 y -7 .. 7

speed 0.5

step Gegeben ist eine längere Strecke $bar(AB)$ ...
together
  point A = -2 -1 opt_gg
  point B = 8.6 -1 opt_gg
end
segment AB = A B opt_gg
pause 200

step ...und eine kürzere Strecke $c$
point C1 = -9 5 hidden
point C2 = -5 5 hidden
segment c = C1 C2 opt_gg2  label "$c$" offset 0 0.1

step Trage an $A$ eine Strecke $bar(AD)$ der Länge $c$ an (<Prop2.html>)
point D = use Prop2.geo A C1 C2 
segment g = use Prop2.geo A C1 C2

step Zeiche um $A$ einen Kreis durch $D$
circle kf = center A through D opt_h2

step Dieser Kreis schneide die Strecke $bar(AB)$ im Punkt $P$
point P = intersect kf AB  opt_r1 duration 400

step Die Strecke $bar(AP)$ ist genauso lang wie $c$
segment r1 = A P opt_r1 width 4


point C1x = copy A hidden
point C2x = copy P hidden
segment cx = C1x C2x opt_r1 width 5 duration 100
together duration 1300
  move C1x to C1
  move C2x to C2
end
together duration 1300
  move C1x to A
  move C2x to P
end
hide cx











