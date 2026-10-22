include ../def.geo

title Strahl und Gerade

# Größen, Linienbreiten, Tempo und Bildbereich kommen aus def.geo.
# Die lokalen VARs bündeln nur die zusätzlich benötigten Optionen.
var punkt_links opt_gg offset -0.5 0.35
var punkt_rechts opt_gg offset 0.25 0.35
var namen_unten offset 0 -1
var geradenname namen_unten labelpos 1.5

step Von der Strecke zum Strahl
info Wir setzen die Strecke $bar(AB)$ über $B$ hinaus unbegrenzt fort.
| So entsteht ein **Strahl**, auch **Halbgerade** genannt.
| $A$ bleibt sein Anfangspunkt. $B$ liegt auf dem Strahl und ist kein Endpunkt mehr.

together
  point A = -4 1 punkt_links
  point B = 3 1 punkt_rechts
end
segment AB = A B opt_gg

# Die Strecke bleibt während des Zeichnens sichtbar. Erst der fertige
# Strahl ersetzt sie, sodass die vorhandene Verbindung nicht verschwindet.
ray r = A B opt_gg label "$vec(AB)$" nolabel namen_unten
hide AB

step Bezeichnung des Strahls
info Wir bezeichnen den Strahl mit $vec(AB)$.
| Der **erste Buchstabe** nennt den Anfangspunkt $A$.
| Der zweite nennt einen weiteren Punkt $B$, durch den der Strahl verläuft.

show label r
text nameAB = "$vec(AB)$: von $A$ durch $B$" at 0 4

step Die Reihenfolge ist wichtig
info Der Strahl $vec(BA)$ beginnt dagegen bei $B$ und verläuft durch $A$.
| Er ist ein anderer Strahl: $vec(AB) != vec(BA)$.

hide r nameAB
ray rueck = B A opt_gg2 label "$vec(BA)$" namen_unten
text nameBA = "$vec(BA)$: von $B$ durch $A$" at 0 4

# Wechsel zwischen beiden Strahlen, ohne die Punkte A und B zu bewegen.
together
  hide rueck nameBA
  show r nameAB
end
pause 1800
together
  hide r nameAB
  show rueck nameBA
end

step Vom Strahl zur Geraden
info Wir gehen wieder vom Strahl $vec(AB)$ aus und setzen ihn auch über $A$ hinaus fort.
| Die entstehende **Gerade** erstreckt sich in beiden Richtungen unbegrenzt.
| Sie hat **keine Endpunkte**. Der Bildrand begrenzt nur unsere Darstellung.

hide label r
together
  hide rueck nameBA
  show r
end
pause 1000
line g = A B opt_gg label "$g$" nolabel geradenname offset 0 -0.5
hide r

step Bezeichnung der Geraden
info Geraden bezeichnen wir häufig mit **Kleinbuchstaben**, zum Beispiel $g$, $h$ oder $k$.
| Unsere Gerade heißt $g$. Sie verläuft durch die beiden Punkte $A$ und $B$.

show label g
text nameG = "Die Gerade $g$ durch $A$ und $B$" at 0 4

step Eine Gerade durch zwei Punkte
info Durch zwei verschiedene Punkte verläuft **genau eine Gerade**.
| Mit einem Doppelpfeil über den Buchstaben schreiben wir dafür auch $overset(harr)(AB)$.
| Die Reihenfolge ist hier gleichgültig: $overset(harr)(AB) = overset(harr)(BA)$.

hide nameG
text punktname = "$g = overset(harr)(AB) = overset(harr)(BA)$" at 0 4

step Strecke, Strahl und Gerade
info Eine **Strecke** hat zwei Endpunkte. 
| 
| Ein **Strahl** hat einen Anfangspunkt und keinen Endpunkt.
| 
| Eine **Gerade** hat weder Anfangspunkt noch Endpunkte.
| 
| Die Punkte $A$ und $B$ sind in allen drei Bildern markiert; ihre Rolle ist jeweils verschieden.

hide A B g punktname

# Drei voneinander unabhängige Vergleichsbilder, mit denselben Punktnamen.
# Die intern unterschiedlichen Namen verhindern gekoppelte Bewegungen.
together
  point A_s = -4 4 punkt_links label "$A$"
  point B_s = 3 4 punkt_rechts label "$B$"
end
segment s_bild = A_s B_s opt_gg
text streckentext = "Strecke: **zwei Endpunkte**" at 0 5.8

together
  point A_r = -4 0 punkt_links label "$A$"
  point B_r = 3 0 punkt_rechts label "$B$"
end
ray r_bild = A_r B_r opt_gg
text strahltext = "Strahl: **ein Anfangspunkt**" at 0 1.8

together
  point A_g = -4 -4 punkt_links label "$A$"
  point B_g = 3 -4 punkt_rechts label "$B$"
end
line g_bild = A_g B_g opt_gg label "$g$" geradenname
text geradentext = "Gerade: **keine Endpunkte**" at 0 -2.2

