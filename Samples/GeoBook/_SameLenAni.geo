
include _def.geo

title Animation gleiche Länge

input point A1 = 1 0
input point A2 = 4 0

input point B1 = 2 3
input point B2 = 5 3

step 
together
    segment l1t= A1 A2 opt_ani1
    segment l1u= B1 B2 opt_ani1
end
pause 100
hide l1t l1u

