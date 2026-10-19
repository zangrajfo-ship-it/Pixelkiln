# Seja 19. 10. 2026: Faze 1–10 (neprekinjeno, po navodilu lastnika »nadaljuj kar z vsemi fazami«)

Lastnik je dovolil izvedbo vseh faz brez ustavljanja. Po vsaki fazi: build, testi, smoke, commit in vnos v ta dnevnik.

## Faza 1: projektna osnova (končana)
- **Dodano:**
  - `DocumentFile`: enotno branje (berljive napake, zavrnitev novejših različic) in atomično shranjevanje z `.v<n>.bak` ob migraciji;
  - `Project` (`.pfproj`, stabilni GUID-i, relativne poti, dodaj, preimenuj, premakni, podvoji, odstrani v `.trash`, manjkajoča, neregistrirana, poškodovana sredstva, povratne in prekinjene reference, iskanje);
  - `AssetLoader` (preverjanje po tipu, slike, tileset iz sredstva).
- **Formati:**
  - `.pxm` v3 (`TilesetAssetId`);
  - `.sfx` v2 (`Version`, brez `Duration`);
  - vsi formati in `settings.json` se shranjujejo atomično.
- **Testi:** `ProjectTests` (9, vključno z obremenitvijo 5.000 sredstev), `DocumentFileTests` (8). Skupaj 88/88. Build 0/0, smoke OK.
- **Merila za sprejem:**
  - stari formati se odprejo (zlati primerki);
  - krožni testi;
  - poškodovane datoteke dajo berljivo napako;
  - neuspelo pisanje pusti izvirnik;
  - migracija naredi `.bak`;
  - moduli delujejo (smoke).

## Faza 2: brskalnik sredstev in upravljanje projektov (končana)
- **Dodano:**
  - meni Project (nov, odpri, nedavni, iz mape, zapri, preveri, prikaz brskalnika Ctrl+Shift+B);
  - `AssetBrowser` (levo):
    - iskanje po imenu, mapi in oznakah, filter tipa, samo priljubljeni;
    - virtualiziran seznam z lenimi sličicami v ozadju;
    - podrobnosti (`AssetInfo`), predogled animacije in zvoka, povratne reference »Used by«;
    - odpri, preimenuj (F2), premakni, podvoji, priljubljeni (Ctrl+D), oznake, odstrani v `.trash` (Del), uvoz s kopiranjem;
  - `HealthWindow`: manjkajoča, poškodovana in neregistrirana sredstva, prekinjene reference, neuporabljene slike in palete; popravki brez brisanja datotek;
  - command palette (Ctrl+Shift+P): ukazi iz vseh menijev, zavihki, sredstva, razširljivo prek `CommandRegistry.Sources`;
  - urejevalniki ob preimenovanju ali premiku sledijo novi poti (`IEditor.OnFileMoved`);
  - shranjevanje v mapo projekta sredstvo samodejno registrira;
  - tilemap si tileset zapomni po ID-ju in ga ob odprtju osveži iz sredstva;
  - zadnji projekt se odpre ob zagonu, projekt ali mapa tudi iz ukazne vrstice;
  - `PIXELFORGE_HOME` za ločene nastavitve (testi, prenosna raba).
- **Popravljena napaka iz prejšnjega kroga:** prvi zavihek po zagonu ni imel menijske vrstice (Fluent `Menu`, napolnjen šele v `Loaded`, je ostal velikosti 0).
- **Testi:**
  - 90/90 (`AssetInfoTests` 2);
  - smoke z vzorčnim projektom: 7/7 sredstev v brskalniku, meni Project na vseh zavihkih, SMOKE OK;
  - iskanje pri 5.000 sredstvih je pod 100 ms (test iz Faze 1).
- **Merila:**
  - vse vrste sredstev so prepoznane;
  - preimenovanje ohrani ID in reference (test);
  - iskanje je odzivno;
  - ponovno odpiranje ohrani projekt (zadnji projekt, manifest se shrani ob vsaki spremembi).

## Faza 3: grafika in animacije (končana)
- **Character Studio** (zavihek Character, `.pxchar` v1):
  - lik na sprajtu iz projekta;
  - stanja povezana z oznakami sprajta (časi in onion skin ostanejo v Sprite);
  - vrsta, smer, izhodišče (Alt+klik);
  - škatle Hurt/Hit/Solid/Trigger po stanju in sličici, s kolizijsko kategorijo;
  - lastnosti `key = value`;
  - predogled s hitrostjo in prehodom v drugo stanje;
  - predloga standardnih stanj (doda manjkajoče oznake v sprajt, s potrditvijo);
  - izvoz JSON + list.
- **Animation Studio:** `KeyframeTrack` (položaj, merilo, rotacija, prosojnost; Linear, EaseIn, EaseOut, EaseInOut, Step; zanka). Uporabljajo ga instance v prizorih (Faza 4), kot je predlagano v Fazi 0.
- **Tileset generator:**
  - autotile 47 (robovi + notranji vogali) poleg 16;
  - naključne variante polne ploščice (stabilne po celici);
  - `.pxm` v4 (`TerrainRules`; v2/v3 `Terrains` se preberejo kot Blob16);
  - iskanje in odstranjevanje podvojenih ploščic s preslikavo mape;
  - predogled ponavljanja 4×4;
  - predloga 16/47;
  - izvoz pravil JSON.
- **Palette studio** (Project): preverjanje sprajtov proti paleti, zamenjava ali poravnava na paleto za več sprajtov, predogled pred in po, razveljavitev zadnje uporabe. V Sprite še senčilna lestvica, poravnava na paleto, barve izven palete.
- **Testi:** 100/100 (Character 2, Keyframe 1, Palette 1, Tileset 2, Autotile47 3, CharacterExport 1); smoke OK s 5 zavihki.
- **Ni ročno preizkušeno:** risanje škatel in predloga stanj v Character Studiu, Palette studio na pravih sprajtih (logika je pokrita s testi Core).
- **Odloženo:** ločeni deli telesa (potreben skeletni model).

## Faza 4: kolizije, prizori in stopnje (končana)
- **Kolizije:**
  - `.pxm` v5: kolizija po ploščici tileseta (pravokotnik, enosmerna platforma, kategorija);
  - Tilemap → Tileset: polna, enosmerna, poljubna škatla, brisanje; oznake na tilesetu (rdeča polna, modra enosmerna);
  - Project → Project settings: ločljivost igre, velikost ploščice, imena kategorij 0–7 in simetrična matrika trkov (`CollisionMasks`);
  - izvoz Tiled TMJ doda `tiles[].objectgroup` (oblika, kot jo zapiše Tilednov urejevalnik kolizij); obstoječa polja nespremenjena.
- **Scene & Level Studio** (zavihek Scene, `.pxscene` v1):
  - plasti Tilemap, Image (paralaksa, odmik, ponavljanje), Objects; vrstni red, skrivanje, brisanje;
  - postavljanje sprajtov in likov (izhodišče lika, animacija, zrcaljenje, Z, lastnosti), poravnava na ploščice (Shift izklopi);
  - gibalne sledi (`KeyframeTrack`) za premikajoče se objekte, predvajanje animacij na platnu;
  - območja Trigger, Checkpoint, Spawn, Camera, Kill; začetek kamere v velikosti igre;
  - ime stopnje, vrstni red, glasba, ozadje, velikost (objekti ostanejo);
  - Check scene (`SceneCheck`): manjkajoča sredstva, objekti zunaj prizora, brez spawna ali igralca, spawn/checkpoint v trdni ploščici, trdna škatla lika v steni;
  - povratne reference: prizor je med uporabniki svojih sredstev.
- **Testi:** 107/107 (Scene 3, Collision 2, SceneAssets 2); smoke OK s 6 zavihki.
- **Ni ročno preizkušeno:** delo z miško na platnu prizora, okno nastavitev projekta (logika trkov je pokrita s testi Core).
- **Odstopanje:** zlatih primerkov `.pxm` v4/v5 ni (tudi Faza 3 ga ni dodala); regresijo pokrivata krožni test in test različice. Dodam v Fazi 10.
- **Odloženo v Fazo 6:** predogled igre (F5) iz prizora.

## Faza 5: Audio Studio (končana)
- **Knjižnica:** vsa zvočna sredstva projekta (pesmi, zvoki, WAV, mešanice); nova tipa sredstev `Audio` (`.wav`) in `Mix` (`.pxmix` v1). Predogled tudi v brskalniku sredstev.
- **Mešalnik** (zavihek Audio): sledi z glasnostjo, mute, solo; posnetki z rezom (preskoči, obdrži), ojačitvijo, fade in/out, ponovitvami; »Join to previous« s prečnim pojemanjem; razdelitev pri predvajalni glavi; valovna oblika na posnetku; pripenjanje na robove.
- **Zanke:** območje zanke v mešanici (za glasbo v igri), predvajanje z zanko.
- **Predvajalni seznami:** v nastavitvah projekta (vrstni red, zanka, naključni vrstni red v igri), predvajanje seznama.
- **Sinhroniziran predogled:** predvajalna glava sledi dejanskemu položaju v `AudioOut` (nova pot `PlayClip`/`ClipPosition`), skok med predvajanjem.
- **Izvoz WAV:** prek `.tmp`, dolžina natančna, opozorilo ob prekoračitvi vrha.
- **Viri:** `WavReader` zapre datoteko z `using`; predvajanje se ustavi ob menjavi zavihka.
- **Testi:** 112/112 (Audio 5: WAV krožno in stereo/22 kHz, urejanje, mešanje z mute/solo/zanko, mešanica v sebi, predvajalni seznam v projektu); smoke OK s 7 zavihki.
- **Ni ročno preizkušeno:** delo z miško na časovnici in poslušanje (zvok v smoke testu ni preverljiv).

## Faza 6: Game Preview & Sandbox (končana)
- **`SceneRenderer`** (Core): programski izris prizora skozi kamero v ločljivosti igre; paralaksa, ponavljanje slik, Z-vrstni red, animacije sprajtov, gibalne sledi (premik in prosojnost), zrcaljenje; prikaz hitboxov (trdne in enosmerne ploščice, območja, škatle likov).
- **`Sandbox`** (Core): dela na kopiji prizora, vir ostane nespremenjen (test). Igralec je prvi lik vrste Player, začne na Spawn območju; gravitacija, hoja, skok (lastnosti lika `speed`, `jump`, `gravity`), trdne in enosmerne ploščice, checkpoint, Kill območje in padec iz prizora vrneta na checkpoint, Trigger sproži dogodek, kamera sledi in ostane v prizoru ali v Camera območju. Brez igralca puščice premikajo kamero.
- **Okno predogleda** (Scene → Game preview, F5): ločljivost iz nastavitev projekta, celoštevilsko povečanje, glasba prizora (zanka iz mešanice), zvok sprožilca (lastnost `sound` = pot sredstva), R ponastavi, H hitboxi, M utišaj, P premor, Esc zapre. Stanje se sproti izpisuje (dostopnost: polite live region).
- **Ponastavitev** vrne stanje ob odprtju predogleda (kopija ob zagonu), tudi če se prizor medtem ureja.
- **Testi:** 116/116 (Sandbox 4: pristanek in hoja, checkpoint, Kill in ponastavitev, Spawn in enosmerne platforme, izris s paralakso in mešanjem). Smoke odpre in zapre predogled.
- **Popravek med delom:** zaokroževanje telesa je skrivalo majhen pogrez, zato igralec ni ostal na tleh; telo je zdaj v realnih pikslih.
- **Poenostavitev:** v predogledu sledi ne skalirajo in ne vrtijo (`ponytail:` v `SceneRenderer`); urejevalnik prizora to prikaže.
- **Ni ročno preizkušeno:** igranje s tipkovnico in zvok.

## Faza 7: izvozni cevovod (končana)
- **Profili** v projektu (privzeta Godot 4 in Generic), okno Project → Export (Ctrl+Shift+E): urejanje profila, izbor sredstev, Check (poročilo brez zapisa), Export, Open folder; opozorilo za neshranjene spremembe (izvoz bere z diska).
- **Preverjanje pred izvozom** ustavi izvoz z razlogom; prejšnji izvoz ostane.
- **Godot 4:** `SpriteFrames`, `TileSet` s kolizijami po kategorijah, `TileMap`, scene likov in stopenj (podrobno v `formats.md`).
- **Generic:** obstoječi javni formati (Aseprite JSON, TMJ, lik JSON, WAV) in novi `ime_scene.json`, `project.json`.
- **Determinizem in brez delnih datotek:** staging mapa + zamenjava, zip s fiksnim časom; testa primerjata dva izvoza bajt za bajtom.
- **Testi:** 121/121 (Export 5: Godot vsebina, Generic, determinizem in zamenjava mape, napake in varovanje prejšnjega izvoza, odvisnosti izbranih sredstev). Smoke izvozi projekt iz zlatih primerkov prek menija.
- **Popravka med delom:** sprajt in lik z istim imenom sta dobila `hero_2`, čeprav se datoteke ne prekrivajo (zdaj skupine izhodov); pričakovanja testa za vrstni red kategorij.
- **Ni preverjeno:** uvoz v pravi Godot (ni nameščen). Priporočam ročni preizkus: kopiraj `export/godot` v `res://assets` Godot 4.3+ projekta in odpri `level1.tscn`.

## Faza 8: zgodovina različic in generatorji (končana)
- **`VersionStore`** po ADR-0003: `.history/objects/<sha256>` (gzip, ena kopija na vsebino), `.history/index.json`, meje na sredstvo in skupna velikost (privzeto 50 in 500 MB, nastavljivo v oknu zgodovine in v `.pfproj`). Prečiščevanje briše najstarejše vnose in nato objekte brez reference. Branje preveri zgoščenko; pokvarjen indeks izgubi samo zgodovino.
- **Beleženje:** vsako odpiranje in shranjevanje sredstva v projektu (`App.AddRecent` → `RecordVersion`), Palette studio pred in po spremembi. Enaka vsebina ne doda različice.
- **Okno zgodovine** (brskalnik → History…, Project → History, Ctrl+H): seznam različic, primerjava ene z dejansko datoteko ali dveh med sabo, slike prej, potem in razlika (magenta) za sprajte, slike in mape, spremenjena polja za vse JSON formate, obnova (trenutna datoteka gre najprej v zgodovino; odprti zavihek se ponovno naloži, z vprašanjem ob neshranjenih spremembah).
- **Generatorji s semenom** (`Rng` SplitMix64; seme je število ali beseda): sprajt (zrcaljen, iz palete, obroba), ploščice (Noise, Bricks, Grass, Stones, brezšivne, različice), postavitev mape (jama s celičnim avtomatom, platforme s prepadi in lebdečimi ploščadmi; z izbranim terenom se robovi samodejno zapolnijo). Meniji: Sprite → Edit → Generate sprite, Tilemap → Tileset → Generate tileset, Edit → Generate layout. Vse z undo.
- **Testi:** 126/126 (History 3: beleženje, deduplikacija, obnova, ponovno nalaganje, meje, pokvarjen objekt, razlike; Procedural 2: ponovljivost in oblika). Smoke odpre zgodovino in preveri `.history/index.json`.
- **Ni ročno preizkušeno:** videz generatorjev (kakovost je okus; logika in ponovljivost sta testirani).

## Faza 9: paleta ukazov in deklarativni vtičniki (končana)
- **Paleta ukazov** vsebuje ukaze vseh zavihkov z enotnimi naslovi (»Sprite › Edit › Flip horizontally«; ukaz najprej preklopi zavihek), skupne menije, zavihke, sredstva in prispevke vtičnikov. Nazadnje uporabljeni ukazi so na vrhu (`Settings.RecentCommands`).
- **Popravek dostopnosti:** vrstice palete so bralnikom zaslona sporočale »System.Windows.Controls.DockPanel«; ime je zdaj na samem elementu seznama (ugotovil smoke test).
- **Vtičniki** po ADR-0002: `PluginLoader` (strogo preverjanje sheme, neznani ključi so napaka, `apiVersion` = 1, `id` = ime mape, omejitev 1 MB, podvojeni `id`), prispevki: palete, prednastavitve generatorjev, izvozni profili, makri (preverjeni naslovi, brez gnezdenja). Okno Options → Plugins (naloženi, zavrnjeni z razlogom, namestitev iz mape, ponovno nalaganje, odpiranje mape).
- **Vzorec:** `samples/plugins/retro-pack` (16-barvna paleta, trije generatorji, Godot profil, makro). **Dokumentacija:** `docs/plugins.md`.
- **Testi:** 128/128 (Plugin 2: vzorec z vsemi prispevki in namestitvijo, osem vrst zavrnitev). Smoke namesti vzorec v začasni dom in v paleti najde makro, ukaz drugega zavihka in generator.
- **Smoke skripta:** pomožna funkcija `Find-MenuItem` s ponovnimi poskusi (meniji se odpirajo asinhrono).

## Faza 10: stabilizacija in dokumentacija (končana)
- **Zlati primerki** za formate iz Faz 3–5 (`Fixtures/current`: `.pxm` v5, `.pxchar` v1, `.pxscene` v1, `.pxmix` v1) s testi branja; zapiše jih `WriteCurrentFixtures` (samo namerno). Odstopanje iz Faze 4 je s tem zaprto. Smoke kopira samo zgornjo raven primerkov.
- **Obremenitveni testi** (`Category=Load`): sprajt 1024² s štirimi plastmi, undo velikega sprajta, projekt s 5000 sredstvi (nalaganje < 1 s, iskanje < 100 ms), pogled igre s 500 objekti.
- **Najdeno in popravljeno:**
  - undo je hranil cele kopije brez omejitve pomnilnika (sprajt 512×512 z 32 sličicami = 32 MB na korak); `History` ima zdaj proračun (512 MB za sprajte in mape, najnovejši korak vedno ostane);
  - pogled igre s 500 objekti je trajal 157 ms na sličico (preverjanje časa datoteke in sestavljanje sličice za vsak objekt); zdaj spomin na sličico in izpust objektov zunaj pogleda, pod 100 ms.
- **Dokumentacija:** `README.md`, `current.md` (končno stanje), opomba o odstopanjih v `target.md`, statusa ADR-0002 in ADR-0003.
- **Končno preverjanje:** Debug in Release build brez opozoril, 137/137 testov, smoke OK (7 zavihkov, predogled igre, paleta ukazov z vtičnikom, zgodovina, izvoz). Koda nima omrežnih klicev, nalaganja kode ali uporabniških identitet.

## Odprto po vseh fazah
- Izvoz Godot 4 ni preizkušen v pravem Godotu (ni nameščen).
- Ročno nepreizkušeno: delo z miško v urejevalnikih prizora, likov in časovnici zvoka, poslušanje, igranje predogleda s tipkovnico.
- Odloženo: ločeni deli telesa likov (skeletni model), skaliranje in vrtenje sledi v predogledu igre, zlivanje korakov undo pri drsnikih.
