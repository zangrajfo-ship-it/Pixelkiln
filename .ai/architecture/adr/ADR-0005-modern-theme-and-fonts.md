# ADR-0005: tema Modern in pisavi Inter ter JetBrains Mono

**Stanje:** sprejeto, 21. 10. 2026

## Kontekst
Uporabnik je predal prenovo videza (`.ai/context/design.md`, izvirnik v `E:\CLAUDE\test\LibreSprite\design-handoff`), napisano za LibreSprite, in izbral prenos na PixelForge. Zasnova zahteva lastne barvne žetone in pisavi Inter ter JetBrains Mono.

## Odločitev
- Nova tema **Modern** (privzeta za nove namestitve) temelji na Fluent Dark. `Theme.Apply` ob zagonu prebere vse barve Fluentovih slovarjev in jih preslika na žetone (accent `#6aa1ff`, ozadja `#141518`/`#1c1d21`/`#24262b`, besedilo `#e6e7ea`/`#a3a6ad`). Lastnih predlog kontrolnikov ni, zato Fluentovo vedenje (fokus, dostopnost, stanja) ostane.
- Vse teme objavijo iste žetone (`Bg0Brush` … `DangerBrush`, `UiFont`, `MonoFont`); urejevalniki uporabljajo samo te, nikoli trdo kodiranih barv.
- Pisavi sta vgrajeni kot vira (`src/PixelForge.App/Fonts`, SIL Open Font License 1.1, licenci se kopirata ob izvedljivo datoteko). To nista knjižnici, ampak sredstvi; omrežja aplikacija ne uporablja.
- Teme Dark, Light in System ostanejo.

## Posledice
- Velikost aplikacije +1,6 MB.
- Ob novi različici Fluenta je treba preveriti preslikavo (neznana barva ostane Fluentova, kar je varno).
