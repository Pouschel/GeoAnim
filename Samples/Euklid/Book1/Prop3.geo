include ../def.geo

title Streckensubtraktion

view x -10 .. 10 y -7 .. 7
speed 0.5

step Gegeben ist eine **längere** Strecke $bar(AB)$ ...
together
  input point A = -2 -1 opt_gg # gegebene Länge
  input point B = 8.6 -1 opt_gg
end
segment AB = A B opt_gg
pause 200

step ...und eine **kürzere** Strecke $c$
input point C1 = -9 5 hidden # Länge die Subtrahiert werden soll
input point C2 = -5 5 hidden
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
output P # Punkt auf AB, sodass AP die kürzere Strecke ist

step Beweis
set t1=Nach <Prop2.html> ist $|c|=|bar(AD)|$ 
text t1t=t1 at 1.5 4.5
use ../SameLenAni.geo A D C1 C2

set t2=$|bar(AD)|=|bar(AP)|$, da Radien 
|
|des selben Kreises
text t2t=t2 at -1.5 -2.18
use ../SameLenAni.geo A D A P

text t35="also: $|c|=|bar(AD)|=|bar(AP)|$" at 4.35 1.93
use ../SameLenAni.geo A P C1 C2













