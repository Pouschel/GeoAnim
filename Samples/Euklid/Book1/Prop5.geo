include ../def.geo

title Gleiche Basiswinkel im gleichschenkligen Dreieck

view x -10 .. 10 y -7 .. 7

step Gegeben ist ein gleichschenkliges Dreieck $Delta ABC$
point B = -3 0 opt_gg offset -0.4 -0.2
point C = 3 0 opt_gg offset 0.1 -0.2
point A = 0 6 opt_gg 
polygon dr= B C A opt_gg opacity 0.1

step Verlängere die Seite $bar(AB)$ über $B$ hinaus
ray AB = A B

step Sei $D$ ein Punkt auf dem Strahl $vec(AB)$ außerhalb der Strecke $bar(AB)$
point D = on AB at 2

step Verlängere die Seite $bar(AC)$ über $C$ hinaus
ray AC = A C

step Sei $F$ ein Punkt auf dem Strahl $vec(AB)$ außerhalb der Strecke $bar(AB)$
point F = on AB at 1.5

step Konstruiere die Strecke $bar(FC)$
segment FC=F C




