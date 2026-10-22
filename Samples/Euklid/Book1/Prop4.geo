include ../def.geo

view x -10 .. 10 y -1 .. 8

title Der Kongruenzsatz SWS

step Gegeben ist ein Dreieck $Delta ABC$ ...
point B = -8 0 opt_gg offset -0.5 -0.3
point C = -2 0 opt_gg offset 0.2 -0.3
point A = -6 6 opt_gg

polygon d1= B A C opt_gg color blue

step ... und ein Dreieck $Delta DEF$

point E = 2 0 opt_gg2 offset -0.5 -0.3
point F = 8 0 opt_gg2 offset 0.2 -0.3
point D = 4 6 opt_gg2

polygon d2= D E F opt_gg2 color green

step Es sei $|bar(AB)|=|bar(DE)|$ ...
together
  segment AB=A B color brown  width 5
  segment DE = D E color brown   width 5
end

step ... $|bar(AC)|=|bar(DF)|$
together
  segment AC=A C opt_h3 width 5
  segment DF = D F opt_h3 width 5
end

step sowie $angle BAC= angle EDF$
together
  angle a1 = B A C radius 1 opacity 0.6 opt_h4
  angle a2 = E D F radius 1 opacity 0.6 opt_h4
end  

step Beweis durch Überlagerung
set t1t = Euklid macht es sich hier einfach.
|
| Seine Überlegungen sind anschaulich.

text t1=t1t at -0.7 7.24
point D1 = copy D hidden
point E1 = copy E hidden
point F1 = copy F hidden

together
    move E to B
    move D to A
    move F to C
end
pause 500
together
    move E to E1
    move D to D1
    move F to F1
end




