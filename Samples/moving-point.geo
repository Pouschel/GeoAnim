# Satz des Thales: der Winkel im Halbkreis ist ein rechter
title "Satz des Thales"
view x -6 .. 6, y -2 .. 6
axes

point A = (-4, 0)
point B = (4, 0)
point M = midpoint A B nolabel color gray

step "Halbkreis über AB: der Kreis um M durch A."
  segment AB = A B
  circle k = center M through A color gray
  point P = on k at 60 hidden

step "Ein Punkt P auf dem Kreis, verbunden mit A und B."
  show P
  together
    segment AP = A P color blue
    segment BP = B P color blue
  end
  polygon T = A B P fill orange opacity 0.15 width 0 duration 400
  text t = "90°" at P offset (-6, -40) color blue size 22

step "Wir bewegen P auf dem Kreis: der Winkel bei P bleibt ein rechter."
  move P to 150 duration 2000
  move P to 30 duration 2500
  move P to 60 duration 800
