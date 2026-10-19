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
