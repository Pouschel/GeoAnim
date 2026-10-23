include ../def.geo

title Parallele Geraden
description Sich schneidende, parallele und identische Geraden. Schreibweisen, Parallelverschiebung und das Zusammenfallen zweier Geraden.

# Größen, Linienbreiten, Tempo und Bildbereich kommen aus def.geo.
var hilfspunkt hidden nolabel
var geradenname labelpos 0.8 offset 0 -0.5


point A = -6 -4 hilfspunkt
point B = 6 2 hilfspunkt
point C = -6 1 hilfspunkt
point D = 6 -3 hilfspunkt

step Geraden mit einem Schnittpunkt
info Die Geraden $g$ und $h$ schneiden sich im Punkt $S$.
| Sie haben genau diesen einen gemeinsamen Punkt.

together
  line g = A B opt_gg label "$g$" geradenname
  line h = C D opt_gg2 label "$h$" geradenname labelpos 0.2 offset 0 0.25
end
point S = intersect g h opt_r1 offset 0.01 -0.65

step Ein Schnittpunkt außerhalb des Bildes
info Wir verändern die Richtung von $h$. Der Schnittpunkt wandert aus dem Bild.
| Die Geraden schneiden sich weiterhin: Ihr gemeinsamer Punkt liegt 
| jetzt außerhalb des sichtbaren Ausschnitts. **Kein sichtbarer Schnittpunkt** 
| bedeutet also noch nicht: **kein gemeinsamer Punkt**.

# Am Schrittende liegt S bei (24, 11), außerhalb des Bildbereichs.
move D to 6 5

step Parallele Geraden
keyword parallel/e/en/er/es, Parallelität, parallele/n Gerade/n
info Nun richten wir $h$ so aus, dass sie in derselben Richtung wie $g$ verläuft.
| Die beiden verschiedenen Geraden haben keinen gemeinsamen Punkt, auch außerhalb des Bildes nicht.
|
| Zwei verschiedene Geraden in der Ebene heißen **parallel**, wenn sie keinen gemeinsamen Punkt haben.
| Wir schreiben $g ∥ h$ und lesen: „$g$ ist parallel zu $h$“.

# D erreicht die Richtung von AB. Der Schnittpunkt existiert dann nicht mehr.
# Das Verbergen betrifft nur seine bereits außerhalb liegende Markierung.
hide S
move D to 6 7
text bezeichnung = "$g ∥ h$" at -1.35 0.4

step Parallel verschieben
keyword Parallelverschiebung/en
info Wir verschieben $h$, ohne ihre Richtung zu ändern.
| Eine solche Bewegung heißt **Parallelverschiebung**.
| In der gezeigten Bewegung bleibt $h$ von $g$ verschieden und zu $g$ parallel.

# Beide bestimmenden Punkte bewegen sich gleichzeitig um denselben Betrag.
# h nähert sich g, ohne mit g zusammenzufallen.
together
  move C to -6 -1
  move D to 6 5
end

step Parallelität definiert keine Richtung
info Wir verändern die Richtung beider Geraden gemeinsam. Sie bleiben parallel.
| Parallelität hängt nicht von einer bestimmten Ausrichtung im Bild ab.
| Entscheidend ist, dass die beiden verschiedenen Geraden keinen gemeinsamen Punkt haben.

# Gleiche Richtungsänderung bei stets getrennten Geraden.
together
  move A to -6 2
  move B to 6 -4
  move C to -6 5
  move D to 6 -1
end

step Identische Geraden
info Wir verschieben $h$ weiter, bis sie genau auf $g$ liegt.
| Nun stimmen beide in **allen Punkten** überein. Sie sind **identisch**.
| Wir schreiben $g = h$: Beide Namen bezeichnen dieselbe Gerade.
|
| Wie bei zwei zusammenfallenden Punkten bedeutet Gleichheit auch hier:
| Es handelt sich um dasselbe geometrische Objekt.
| Die Geraden haben jetzt alle Punkte gemeinsam.
# Die Parallelitätsanzeige verschwindet, bevor beide Geraden zusammenfallen.
hide bezeichnung
together
  move C to A
  move D to B
end
text gleichheit = "$g = h$" at -1.35 0.4
