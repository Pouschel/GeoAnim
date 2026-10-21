# Die Mittelsenkrechte einer Strecke mit Zirkel und Lineal
title "Mittelsenkrechte"
view x -1 .. 11, y -1 .. 7

point A = (3, 3)
point B = (7, 3)

step "Gegeben sind die Punkte A und B. Wir zeichnen die Strecke AB."
  segment AB = A B

step "Kreis um A durch B."
  circle k1 = center A through B color gray

step "Kreis um B durch A."
  circle k2 = center B through A color gray

step "Die beiden Kreise schneiden sich in S und T."
  point S, T = intersect k1 k2 color red

step "Die Gerade ST ist die Mittelsenkrechte von AB."
  line m = S T color red width 3

step "Sie halbiert die Strecke AB im Mittelpunkt M."
  point M = intersect m AB color red
  together
    hide k1 k2 duration 800
  end
