# Izvoz v Godot 4

Navodilo opiše, kako vsebine iz Pixelkilna prideš v Godot 4.3 ali novejši in kaj po prvem uvozu preveriti. Izvoz ima teste v kodi (vsebina datotek je preverjena), v pravem Godotu pa ga še nihče ni preizkusil. Prvi uvoz zato vzemi kot preizkus in napake prijavi.

## Kaj potrebuješ
- Projekt v Pixelkilnu (**Project › New project** ali **Make a project from a folder**). Izvoz za Godot deluje samo s projektom, ker liki in mape kažejo na sprajte po ID-jih.
- Projekt v Godotu 4.3 ali novejšem.

## Koraki
1. V Pixelkilnu odpri **Export** (gumb zgoraj desno ali Ctrl+Shift+E).
2. Izberi profil **Godot 4**. Privzeto piše v mapo `export/godot` v projektu, datoteke pa pričakuje v Godotu pod `res://assets/`. Če jih boš dal drugam, spremeni polje z mapo v Godotu (`assets`), sicer povezave med datotekami ne bodo držale.
3. Klikni **Export**. Program najprej preveri projekt. Če najde napako (manjkajoča datoteka, lik brez sprajta, mapa brez nabora ploščic), ne zapiše ničesar in pove, kaj je narobe.
4. Vsebino mape `export/godot` kopiraj v mapo `assets` v Godotovem projektu. Kopiraj vsebino, ne same mape `godot`.
5. Preklopi v Godot in počakaj, da uvozi slike.

## Kaj nastane
Imena sledijo poti sredstva v projektu, na primer `art/hero.pxf` da `art/hero.png`.

| Sredstvo | Datoteke | V Godotu |
|---|---|---|
| Sprajt | `ime.png`, `ime.tres` | slika z vsemi sličicami in `SpriteFrames`: ena animacija za vsako oznako, brez oznak animacija `default` |
| Mapa ploščic | `ime_tiles.png`, `ime_tileset.tres`, `ime.tscn` | `TileSet` s kolizijami (ena fizikalna plast za vsako kategorijo) in scena z vozliščem `TileMap`; točke na mapi so `Marker2D`, pravokotniki `Area2D` |
| Lik | `ime_character.tscn` | `CharacterBody2D` (igralec, sovražnik) ali `Area2D` (NPC, predmet) z `AnimatedSprite2D`; škatla Solid je `CollisionShape2D`, škatle Hurt, Hit in Trigger ter lastnosti so v metapodatkih vozlišča |
| Projekt | `project.json`, `export-report.txt` | nastavitve igre in kategorije kolizij; seznam zapisanih datotek |

## Preveri po prvem uvozu
Odkljukaj vsako točko. Kar ne drži, je napaka izvoza.

- [ ] Godot uvozi vse datoteke brez rdečih napak v zavihku Output.
- [ ] `ime.tres` sprajta se odpre v urejevalniku SpriteFrames; animacije imajo imena oznak in pravo število sličic.
- [ ] Animacija teče enako hitro kot v Pixelkilnu (trajanje sličic).
- [ ] `ime.tscn` mape pokaže enako sliko kot zavihek Tilemap, tudi zrcaljene in zavrtene ploščice.
- [ ] Godot 4.3 označi vozlišče `TileMap` kot zastarelo in ponudi pretvorbo v `TileMapLayer`. To je pričakovano; pretvorba mora uspeti.
- [ ] Trdne ploščice imajo v naboru ploščic obliko kolizije, enosmerne so označene kot enosmerne.
- [ ] Točke in pravokotniki z mape so na pravih mestih (`Marker2D`, `Area2D`), vrsta objekta je v metapodatku `type`.
- [ ] `ime_character.tscn` pokaže lik, animacija stanja `idle` (ali prvega stanja) se predvaja sama.
- [ ] Izhodišče lika je v točki (0, 0) vozlišča, škatla Solid je na pravem mestu.
- [ ] V Inspectorju so pod Metadata `kind`, `facing`, `states`, `boxes` in lastnosti lika.
- [ ] Slike so ostre. Če so zamegljene, v Godotu nastavi **Project Settings › Rendering › Textures › Default Texture Filter** na **Nearest**. To je nastavitev Godota, ne napaka izvoza.

## Ponovni izvoz
Izvoz vsakič zamenja celo izhodno mapo. Isti vhod da iste bajte, zato Godot po ponovnem kopiranju uvozi samo spremenjene datoteke. Sprememb, ki jih narediš v Godotu v izvoženih datotekah, naslednji izvoz ne ohrani; lastne scene zato gradi v ločenih datotekah, ki izvožene samo uporabljajo.

## Brez Godota
Profil **Generic** zapiše PNG, Aseprite JSON, Tiled TMJ in JSON likov, ki jih bere večina pogonov. Brez projekta gumb **Export** ponudi izvoze odprte datoteke.
