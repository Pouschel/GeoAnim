include ../def.geo

title Streckensubtraktion

view x -10 .. 10 y -7 .. 7
grid 0.5
speed 0.5

step Gegeben ist eine längere Strecke $bar(AB)$ ...
together
  point A = -4 -4 opt_gg
  point B = 4 -4 opt_gg
end
segment AB = A B opt_gg
pause 200

step ...und eine Strecke $c$
point C1 = -6 2 hidden
point C2 = -2 2 hidden
segment c = C1 C2 opt_gg2  label "$c$"

step Wir tragen an $A$ eine Strecke der Länge $c$ an (Prop2.html)
#point D = use Prop2.geo C1 C2  






