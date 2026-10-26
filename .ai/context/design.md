# Prenova videza (tema »Modern«), Faze 23–26

Vir: `E:\CLAUDE\test\LibreSprite\design-handoff` (DESIGN_HANDOFF.md in `mockups/*.dc.html`). Napisan je za LibreSprite; uporabnik je 21. 10. 2026 izbral prenos na PixelForge, vse zaslone po fazah, pisave Inter in JetBrains Mono (prenesene, OFL, `src/PixelForge.App/Fonts`).

## Žetoni
| Žeton | Barva | Raba |
|---|---|---|
| bg-0 | `#141518` | okolica platna, polja za vnos |
| bg-1 | `#1c1d21` | paneli, vrstice, meniji, okna |
| bg-2 | `#24262b` | dvignjeni gumbi, neaktivni zavihki |
| hover | `#2a2c32` | lebdenje |
| selected | `#2e3036` | izbran segment ali gumb |
| border | `#2b2d33` | ločila, robovi polj |
| text / text-2 / text-3 / disabled | `#e6e7ea` / `#a3a6ad` / `#8a8d94` / `#6b6e75` | |
| accent / accent-tint / on-accent | `#6aa1ff` / `#2b3a55` / `#0f1115` | |
| success / danger | `#5ad18a` / `#e8505b` | |
| dodatno | `#c9cbd1` meni in seznami, `#33353b` obroba orisnih gumbov, `#4a4d55` lebdeča obroba, `#22242a` lebdenje vrstice, `#26282e` trenutni stolpec, `#101114` ozadje valovne oblike in piano rolla, `#18191d` levi stolpec domačega zaslona, `#8fb8ff` povezava | |

Accent z alfo: 12 % `1F`, 14 % `24`, 18 % `2E`, 25 % `40`, 40 % `66`.

Pisava Inter 13 px (oznake 12, glave razdelkov 12 px 600 velike črke z razmikom 0.06em), JetBrains Mono za številke (zoom, velikosti, koordinate, vrstica stanja 11 px). Polmeri: gumbi 6, polja in kartice 8, plavajoče vrstice 10, orodja na tirnici 9. Klikalna površina vsaj 32 px, orodja 44 px.

## Skupni okvir
- **Zgornja vrstica** 48 px + rob: logotip 32×32 (accent, 2×2 kvadratki), meniji (prosojni, `#c9cbd1`, višina 32), ločilo, **zavihki načinov** kot segmentni gumbi (okvir bg-0 z robom, polmer 9; aktivni `#2e3036`), čip dokumenta (ime + velikost v Mono 11 `#8a8d94`, pika za neshranjeno), razmik, undo/redo 36×36, orisni sekundarni gumb, **primarni gumb** (accent, besedilo `#0f1115` 600).
- **Vrstica stanja** 28 px + rob: Mono 11 text-2, razmik 18; desno zelena pika 6 px + »Autosaved …«.
- **Desni panel** 296 px (Music 280), razdelki s paddingom 14 in robom spodaj.
- **Platno** bg-0, risba z 1 px črnim robom in senco; **plavajoči vrstici** spodaj levo (roka, −, zoom v Mono, +, Fit) in desno (mreža, onion skin; vklopljeno = accent na 14 %).

## Sprite (Main)
Tirnica orodij 60 px s skupinami (risanje, oblike, izbor, besedilo), gumbi 44×44, aktivno: accent 18 %, ikona accent, notranji obroč accent 40 %; spodaj barvni kvadrata 28×28 in gumb za zamenjavo. Kontekstna vrstica 48 px: ikona, ime in tipka orodja, nato samo nastavitve tega orodja (čopič: oblika, velikost, prosojnost, pixel-perfect, zrcaljenje; polnilo: toleranca, sosednje, vzorec; izbor: način). Časovnica: vrstice 30 px, številke sličic kot gumbi 36×28 (trenutna accent), barvne oznake, oko in ključavnica pri plasteh. Desni panel: COLOR (SV kvadrat, odtenek, hex, alfa), PALETTE (mreža 8 stolpcev), PREVIEW (4× in 2×).

## Domači zaslon (Home)
Levi stolpec 460 px (`#18191d`): znamka, »New sprite« s karticami prednastavitev (16, 32, 64, 128, 320×180, 256), širina, zamenjava, višina, barvni način in ozadje kot segmenti, gumb Create; Open, Recover (števec), Import sprite sheet. Desno: »Recent files« z iskanjem in mrežo kartic s sličicami, »Recent folders«.

## Drugi načini
- **Tilemap:** tirnica (Paint, Rectangle fill, Fill, Erase, Pick, Select) in ploščica čopiča; kontekstna vrstica (zrcaljenje, vrtenje, Auto-tile, naključne različice, velikost); desno TILESET, MAP LAYERS, TILE PROPERTIES; plavajoči gumb Collision (vklopljen `#ffb3b9` na rdeči).
- **Scene:** levo HIERARCHY (kamera, mape, entitete s položajem) in ASSETS; vrstica orodij (Select, Place, Pan; Snap, Show colliders); desno INSPECTOR (animacija, X Y Z, Flip, Visible), COLLIDER, PROPERTIES; gumb Play scene F5 (med igro rdeč Stop).
- **SFX:** levo START FROM (prednastavitve z ikonami, Randomize, Mutate), sredina: okrogel gumb Play 52 px, valovna oblika, oblika vala kot segmenti, kartice parametrov; desno PROJECT SOUNDS in USED IN.
- **Music:** transportna vrstica (na začetek, Play 44×40, zanka, položaj v Mono 20, BPM, tonaliteta, takt); levo CHANNELS (barvni čip, ime, instrument, M in S); piano roll (tipke 56 px, celice 24 px, takti in dobe z različnim ozadjem, bobni kot vrstice z imeni); spodaj SONG razporeditev; desno INSTRUMENT.

## Stanje
Prenesena so okno, Sprite, Tilemap, Character (brez vzorca, v istem slogu) in domači zaslon. Scene, SFX in Music so v poenostavljeni aplikaciji izklopljeni (Faza 24).

## Pravila prenosa
- Teme Dark in Light ostanejo; »Modern« je nova izbira v Preferences in privzeta za nove namestitve.
- Kontrast besedila vsaj 4.5:1, viden fokus tipkovnice (2 px accent), tooltipi s tipkami.
- Vsaka faza en commit; build, testi in smoke po vsaki.
