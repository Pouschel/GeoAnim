title An einem Punkt eine Strecke vorgegebener Länge konstruieren

view x -10 .. 10 y -7 .. 7
grid 0.5
speed 0.6

var opt_gg color blue  # gegebene Objekte
var opt_gg2 color green 
var opt_h1 color grey

step Gegeben ist der Punkt $A$
point A = -2 2 opt_gg2

step Gegeben ist die Verbindungsstrecke durch die Punkte $B$ und $C$
point B = 0 0 offset -0.4 0 opt_gg
point C = 3 0 opt_gg
segment s = B C opt_gg

step Zeichne die Strecke $AB$
segment AB = A B color grey

step Zeichne auf $AB$ ein gleichseitiges Dreieck $ABD$ (Prop1.html)
together
  circle k1 = center A through B
  circle k2 = center B through A
end
point D = intersect k1 k2 offset 0 0.2
polygon ABD = A B D
hide k1 k2

step Erweitere $DA$ und $DB$ zu Strahlen
together
  ray dar = D A
  ray dbr = D B
end

step Zeichne um $B$ einen Kreis mir dem Radius $bar(BC)$ ... 
circle kb = center B through C

step Sei $G$ der Schnittpunkt dieses Kreises mit dem Strahl $vec(BD)$
point G = intersect dbr kb
pause 300

step Zeichne um $D$ einen Kreis mit dem Radius $bar(DG)$
circle kd = center D through G

step Sei $L$ der Schnittpunkt dieses Kreises mit dem Strahl $vec(DA)$
point L = intersect dar kd
pause 300

