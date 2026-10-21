title An einem Punkt eine Strecke vorgegebener Länge konstruieren

view x -10 .. 10 y -7 .. 7
grid 0.5

step Gegeben ist der Punkt $A$
point A = -2 2 color green

step Gegeben ist die Verbindungsstrecke durch die Punkte $B$ und $C$
point B = 0 0 offset -0.4 0
point C = 3 0
segment s = B C color blue

step Zeichne die Strecke $AB$
segment AB = A B color grey

step Zeichne auf $AB$ ein gleichseitiges Dreieck $ABD$ (Prop1.html)
together
  circle k1 = center A through B
  circle k2= center B through A
end
point D = intersect k1 k2 offset 0 0.2
polygon ABD = A B D
hide k1 k2

step Erweitere $DA$ und $DB$ zu Strahlen
#together

#end
