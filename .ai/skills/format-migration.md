# Sprememba formata datoteke

Uporabi pri vsaki spremembi `.pxf`, `.pxm`, `.pxs`, `.sfx` ali novega formata.

1. Povečaj `Version` v serializatorju (`ProjectFile`, `Tilemap`, `Song`, `Sfx`).
2. Novo polje v DTO naj bo neobvezno s privzeto vrednostjo (`Type? X = null`). Starejša datoteka se mora naložiti brez napake.
3. Če se pomen polja spremeni, napiši migracijo, ki iz stare različice izračuna novo. Pri tem ne zavrzi podatkov.
4. Neveljavne vrednosti iz datoteke omeji (`Math.Clamp`) ali zavrni z jasnim `InvalidDataException`.
5. Ustvari nov zlati primerek z `WriteV2Fixtures` (razširi metodo za novo različico). Starih primerkov ne prepisuj.
6. Dodaj test v `FormatRegressionTests` za novo različico; obstoječi testi starih različic morajo ostati zeleni.
7. Posodobi `.ai/context/formats.md`.
