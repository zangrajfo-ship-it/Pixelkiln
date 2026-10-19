# Nov izvozni profil

Uporabi za izvoz v igralni pogon ali nov format (prvi cilj: Godot 4).

1. Shemo ciljnega formata preveri v uradni dokumentaciji pogona in povezavo zapiši v kodo kot komentar.
2. Izvoznik je čista funkcija v Core: model → besedilo ali bajti. Brez UI in brez pisanja datotek.
3. Pisanje datotek: najprej vse v začasno mapo, nato premik, da napaka ne pusti delnih izhodov.
4. Izhod mora biti determinističen: urejeni seznami, fiksen format števil (`CultureInfo.InvariantCulture`).
5. Test: strukturna primerjava (JSON/tekst) na majhnem modelu in preverjanje obveznih polj ciljne sheme.
6. Obstoječe izvoze (Aseprite JSON, Tiled TMJ) pokriva regresija; ne spreminjaj njihovih obstoječih polj.
