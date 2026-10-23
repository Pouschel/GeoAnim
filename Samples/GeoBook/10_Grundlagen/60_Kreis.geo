include ../_def.geo

title Kreis, Mittelpunkt und Radius
description Wie aus einem gleichbleibenden Abstand eine Kreislinie entsteht. Mittelpunkt, Radius und gleiche Abstände auf dem Kreis sowie die Arbeitsweise des Zirkels.

# Größen, Linienbreiten und allgemeines Tempo kommen aus def.geo.
var punktname offset 0.25 0.2
var kreispunkt opt_gg punktname
var radiuslinie opt_gg2
# Kreiszeichnung und Punktbewegung benötigen dieselbe Dauer und denselben
# Verlauf. Diese gemeinsame Einstellung hält die beiden Aktionen synchron.
var umlauf duration 1500 linear

step Ein fester Abstand
info Der Punkt $M$ bleibt fest. Wir verbinden ihn mit einem zweiten Punkt $P$.
| Im nächsten Schritt bewegt sich $P$, während die Länge der Strecke $bar(MP)$ gleich bleibt.

point M = 0 0 color black offset -0.65 -0.5
# Der noch unsichtbare Kreis hält den Abstand von P zu M fest.
circle k = center M radius 3.5 opt_gg hidden
point P = on k at 0 kreispunkt
segment MP = M P radiuslinie label "$r$" nolabel offset 0 -0.5

step Eine Kreislinie entsteht
keyword Kreis/e/en/es, Kreislinie/n
info $P$ bewegt sich um $M$ und behält dabei immer denselben Abstand zu $M$.
| Seine Spur bildet eine **Kreislinie**. Wir nennen diese Linie auch kurz einen **Kreis**.
| Gemeint ist hier die Linie, nicht die von ihr eingeschlossene Fläche.

together
  show k umlauf
  move P to 360 umlauf
end

step Mittelpunkt und Radius
keyword Kreismittelpunkt/e/en/es, Radius, Radien
info $M$ heißt **Mittelpunkt** des Kreises.
| Der Abstand von $M$ zu einem Punkt der Kreislinie heißt **Radius**.
| Wir bezeichnen ihn häufig mit $r$. Hier gilt $|MP| = r$.
|
| Auch eine Strecke vom Mittelpunkt zu einem Punkt der Kreislinie nennt man einen Radius.

show label MP
text radiusname = "$|MP| = r$" at 0 -5.3

step Alle Kreispunkte haben denselben Abstand
info Auch $A$, $B$ und $C$ liegen auf der Kreislinie.
| Ihre Abstände zum Mittelpunkt sind gleich groß: Alle gezeigten Radien haben dieselbe Länge $r$.

hide radiusname
together
  point A = on k at 60 kreispunkt
  point B = on k at 150 kreispunkt offset -0.6 0.2
  point C = on k at 250 kreispunkt offset -0.6 -0.5
end
together
  segment MA = M A radiuslinie
  segment MB = M B radiuslinie
  segment MC = M C radiuslinie
end
text gleichlang = "$|MP| = |MA| = |MB| = |MC| = r$" at 0 -5.3

step Näher am Mittelpunkt
info Nicht jeder Punkt liegt auf der Kreislinie.
| $Q$ ist näher an $M$ als die Kreispunkte: $|MQ| < |MP| = r$.
| Deshalb gehört $Q$ nicht zur Kreislinie.

together
  hide A B C MA MB MC gleichlang
end
point Q = 0 1.8 opt_h3 punktname
segment MQ = M Q opt_h3
text naeher = "$|MQ| < |MP| = r$" at 0 -5.3

step Weiter vom Mittelpunkt entfernt
info Nun liegt $Q$ weiter von $M$ entfernt als die Kreispunkte: 
| $|MQ| > |MP| = r$.
| Auch hier gehört $Q$ nicht zur Kreislinie.
|
| Zur Kreislinie gehören **alle und nur die Punkte**, deren Abstand von $M$ genau $r$ beträgt.

hide naeher
move Q to 0 5.2
text weiter = "$|MQ| > |MP| = r$" at 0 -5.3

#todo Zirkelbild einfügen
step Der Zirkel hält den Abstand fest
keyword Zirkel/s/n
info Mit einem **Zirkel** kann man einen Kreis konstruieren.
| Die Spitze bleibt im Mittelpunkt $M$.
| Der Zeichenstift bewegt sich um $M$. 
| Die eingestellte Zirkelöffnung hält seinen Abstand zu $M$ fest.
| So zeichnet der Stift einen Kreis mit dem Radius $r$.

together
  hide Q MQ weiter k
  hide label M P
end
# Schematische Darstellung der beiden Kontaktpunkte des Zirkels.
text spitze = "Spitze" at M offset -1.6 -0.65
text stift = "Zeichenstift" at P offset 0.35 0.3
# Eine neue, deckungsgleiche Kreislinie wird erneut gezeichnet.
# Der feste Radius vermeidet eine mit P mitwandernde Startposition des Bogens.
circle zirkelspur = center M radius 3.5 opt_gg hidden
together
  show zirkelspur umlauf
  move P to 360 umlauf
end
