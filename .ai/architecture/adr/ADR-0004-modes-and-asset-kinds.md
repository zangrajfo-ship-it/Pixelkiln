# ADR-0004: Načini urejanja v zavihkih in register vrst sredstev

**Status:** sprejeto, izvedeno v Fazi 11 (19. 10. 2026)

## Kontekst
Faze 11–21 dodajo pet novih vrst dokumentov (slikanje, vektorji, teksture, UI, animacije, logika). Uporabnik želi ohraniti sedem zavihkov. Do zdaj je vsaka vrsta sredstva zahtevala nov `case` v več stavkih `switch`: `AssetLoader.Probe`, `AssetInfo.Describe`, `Project.ReferencesOf`, sličice, `TypesByExtension`.

## Odločitev
- **`AssetKinds` (Core):** en zapis na vrsto sredstva s končnicami, preverjanjem, opisom, sliko, referencami in prevezavo referenc (`Remap`). Nove vrste se registrirajo z `AssetKinds.Register`. `Project.TypesByExtension` je pogled na register.
- **`ModeHost` (App):** zavihek z več načini (Sprite: Pixel, Paint …). Vrstica načinov se pokaže šele pri dveh ali več načinih. Gostitelj posreduje `IEditor` aktivnemu načinu in datoteko odpre v načinu, ki ima njeno končnico. Ukazi prvega načina obdržijo naslove brez predpone, zato makri vtičnikov delujejo naprej.
- **Stanje pogleda** (vodila, referenčna slika) je v `.pfproj` (`Settings.Views`), ne v datotekah sredstev: to je pomoč pri urejanju, ne vsebina, zato formatom sredstev ni treba povečati različice.

## Posledice
- Nova vrsta dokumenta pomeni: en `AssetKinds.Register`, en urejevalnik in en `host.Add`.
- Brez projekta se vodila ohranijo samo do zaprtja aplikacije.
