include ../_def.geo

title An einem Punkt eine Strecke vorgegebener Länge konstruieren

view x -10 .. 10 y -7 .. 7
speed 0.5

step Gegeben ist der Punkt $A$
input point A = -1.5 2 opt_gg2 offset -0.3 0.1

step Gegeben ist die Verbindungsstrecke durch die Punkte $B$ und $C$
input point B = 0 0 offset -0.4 -0.3 opt_gg
input point C = 3 0 offset 0 -0.3 opt_gg

segment s = B C opt_gg

step Zeichne die Strecke $AB$
segment AB = A B opt_h1

step Konstruiere auf $AB$ ein gleichseitiges Dreieck $Delta ABD$ (<Prop1.html>)
point D = use Prop1.geo A B offset 0.1 0
polygon ABD = A B D

step Erweitere $bar(DA)$ und $bar(DB)$ zu Strahlen
together
  ray dar = D A
  ray dbr = D B
end

step Zeichne um $B$ einen Kreis mir dem Radius $bar(BC)$
circle kb = center B through C opt_h2

step Sei $G$ der Schnittpunkt dieses Kreises mit dem Strahl $vec(BD)$
point G = intersect dbr kb
pause 300

step Zeichne um $D$ einen Kreis mit dem Radius $bar(DG)$
circle kd = center D through G   opt_h3

step Sei $L$ der Schnittpunkt dieses Kreises mit dem Strahl $vec(DA)$
output point L = intersect dar kd opt_r1 offset -0 0.1
pause 300

step Die Strecke $bar(AL)$ ist genauso lang 
|wie die Strecke $bar(BC)$
output segment res = A L opt_r1 # gibt mit L die konstruierte Strecke zurück

step Beweis

set b0=$bar(DL)=bar(DG)$, da $L$ und $G$ 
|
|auf einem Kreis um $D$ liegen
text tb0=b0 at -7 5 
use ../_SameLenAni.geo D L D G

set b1=Da $Delta ABD$  gleichseitig ist,
|
| gilt $bar(DA)=bar(DB)$
text tb1=b1 at -1 4 
use ../_SameLenAni.geo D A D B

set b2=Da $bar(DL)=bar(DG)$ war,  
|
|gilt auch $bar(AL)=bar(BC)$
text tb2=b2 at -6.5 -1
use ../_SameLenAni.geo A L B C
