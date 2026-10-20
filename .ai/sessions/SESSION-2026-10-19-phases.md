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

## Faza 11: temelji za nove načine (končana)
Začetek načrta Faz 11–21 (razširitev po predlogu uporabnika; plan v `~/.claude/plans/yeah-make-a-plan-bright-lynx.md`).
- **Popravek J:** predogled igre zdaj skalira in vrti po gibalni sledi (`SceneRenderer.BlitTransformed`, pivot = izhodišče, enako kot urejevalnik), izpust zunaj pogleda upošteva doseg.
- **`AssetKinds`** (ADR-0004): register vrst nadomesti `switch` stavke v Core; `Project.ReplaceReferences` prek `Remap` (prizori, liki, mape, mešanice, predvajalni seznami, izvozni profili).
- **`ModeHost`:** Sprite (Pixel), Character (States) in Scene (Layout) so gostitelji načinov; Ctrl+1…9; ukazi drugih načinov v paleti s predpono.
- **Asinhrono:** `Ui.RunLong` (napredek, preklic, lastnik onemogočen); izvoz in preverjanje izvoza tečeta v ozadju, preklic pred zamenjavo mape ne pusti sledi.
- **Brskalnik:** večkratni izbor, paketno preimenovanje z vzorcem (`{name}`, `{n}`, zamenjave prek začasnih imen), oznake, skaliranje sprajtov, pretvorba slika ↔ sprajt, zamenjava sredstva povsod (z zapisom različic pred spremembo).
- **Ravnila, vodila, referenčna slika** (`ViewAids`) v Pixel in Scene načinu; v prizoru pripenjanje na vodila pred mrežo ploščic; shranjeno v `.pfproj` po ID-ju sredstva.
- **Kopiranje sloga** Ctrl+Alt+C/V: Pixel (paleta, barvi), Scene (lastnosti objekta ali območja), Character (vrsta, smer, lastnosti).
- **Delovni profili** (Options → Workspace): vgrajeni Pixel art, Painting, Animation, Levels in lastni (zavihek, način, brskalnik, orodje).
- **Testi:** 144/144 (novi: transformirani izris, register in zamenjava povsod, paketne operacije, preklic izvoza, profili v nastavitvah). Obremenitveni testi tečejo zaporedno (vzporedno so dajali lažne napake). Smoke preveri še delovni profil.
- **Ni ročno preizkušeno:** vlečenje in pripenjanje na vodila z miško, paketne operacije v brskalniku, kopiranje sloga s tipkovnico.
- **Opomba:** profil Painting do Faze 13 odpre prvi način (Pixel).

## Faza 12: Template Studio (končana)
- **Format predlog** (`Templates.cs`): strogo preverjen JSON kot pri vtičnikih (neznani ključi, koraki, parametri in sklici so napake z razlogom); viri Built-in (vgrajeni viri v Core), Project in User; različice v mapah `<id>@<različica>`.
- **`TemplateEngine`:** parametri, »Starting point« (jedro in obvezne odvisnosti) ali »Full kit«; generatorji s semenom (sprajt s stanji, predmeti, lik z deleži škatel, tileset ali obstoječa slika iz projekta, mapa Platforms, Cave, Rooms ali Flat, stopnja, pesem); kopije shranjenih predlog s preusmeritvijo referenc prek `AssetKinds.Remap`.
- **`LevelBuilder`:** spawn, checkpointi enakomerno od leve proti desni, nevarnosti v prepadih ali na prostih celicah, zbirateljski predmeti; **`Procedural.Rooms`** in **`Procedural.Song`** (bas, arpeggio, melodija, bobni).
- **22 vgrajenih predlog** (liki, stopnje, predmeti, kompleti, glasba); slike vseh preverjene (`WriteTemplatePreviews`).
- **Okno Template Studio** (Ctrl+T): iskanje, filtri, predogled (izdelan v začasnem projektu), različice, obrazec parametrov, ustvarjanje v ozadju, Save selected assets as template (z odvisnostmi), uvoz in izvoz paketa, brisanje v `.trash`.
- **Liki in prizori** imajo zdaj sliko tudi v brskalniku sredstev (lik prek sprajta, prizor upodobljen).
- **Popravki med delom:** kopije odvisnosti niso šle v »Starting point«; neobvezni sklici stopnje (junak, kovanci, glasba) so napačno vlekli vse v izhodišče.
- **Testi:** 156/156 (Template 11: vse vgrajene kot komplet in izhodišče brez napak, ponovljivost, lik s paleto iz projekta, shranjena predloga in preusmeritev, paketi in zavrnitve, 6 vrst neveljavnih manifestov). Smoke ustvari lik iz predloge.
- **Ni ročno preizkušeno:** obrazec parametrov s tipkovnico, uvoz in izvoz paketa v oknu.
- **Predloge za UI zaslone, materiale in luči** pridejo s fazami 15, 16 in 18.

## Faza 13: Digital Painting Studio (končana)
- **Način Paint v zavihku Sprite** (Ctrl+2), prvi dodatni način v `ModeHost`.
- **`PaintDoc` (`.pxpaint` v1):** platno do 8192×8192, ploščice 256×256 samo tam, kjer je barva; plasti z mešalnimi načini (Normal, Multiply, Screen, Overlay, Add, Darken, Lighten, sestavljanje po W3C), prosojnost, maske, zaklepanje prosojnosti; zip s PNG ploščicami, atomično shranjevanje.
- **Pogon čopičev** (`PaintStroke`):
  - orodja čopič, svinček (trd rob brez glajenja), radirka, airbrush (pretok, nabira se tudi na mestu), razmazovanje, kapalka, gradient;
  - medpomnilnik pokritosti poteze: prekrivanje ne preseže prosojnosti;
  - stabilizator (povprečje točk), zrno, kvadratna konica;
  - pritisk pisala za velikost in prosojnost, nasprotni konec pisala je radirka; dogodki miške, ki jih WPF ustvari iz pisala, se ne upoštevajo dvakrat.
- **Undo po ploščicah** (`PaintUndo`): poteza hrani samo dotaknjene ploščice, spremembe plasti samo nastavitve; proračun pomnilnika.
- **Izris:** po vsakem premiku se sestavi in prikaže samo spremenjeni pravokotnik.
- **Ostalo:** uvoz slike kot plasti, izvoz PNG, »To pixel art« (pomanjšanje s povprečenjem po območju v Pixel način), ravnila, vodila in referenčna slika, kopiranje sloga (čopič in barve), vrsta sredstva `Paint` s sliko v brskalniku (gradi se po ploščicah).
- **Testi:** 162/162 (Paint 5 in obremenitveni: mešalni načini, prosojnost brez nalaganja in airbrush, radirka, zaklepanje, maska in undo/redo čez več ploščic, datoteka, pritisk, gladilnik, gradient, pomanjšanje; poteza 64 px čez 4096² v mejah). Smoke preklopi v Paint in preveri menije in naslov.
- **Popravki med delom:** zaklepanje prosojnosti je v prazne piksle pisalo nevidno barvo; drsniki so ob nalaganju XAML sprožili dogodek pred obstojem oznak (aplikacija se ni zagnala); prileganje pogledu pred postavitvijo je dalo 6 %.
- **Ni ročno preizkušeno:** risanje z miško in pisalom (pritisk), občutek tekočnosti na 4096² v aplikaciji. Jedro je izmerjeno s testom; smoke ne more vleči miške, ne da bi premikal uporabnikov kazalec.
- **Odstopanje od načrta:** nagib pisala se še ne uporablja (WPF ga sporoča le pri nekaterih gonilnikih).

## Faza 14: Vector Studio (končana)
- **Način Vector v zavihku Sprite** (Ctrl+3).
- **`VectorDoc` (`.pxvec` v1):** poti iz Bezierjevih vozlišč z ročicami in več obrisi (luknje, even-odd), pravokotniki z zaobljenimi robovi, elipse, skupine; polnila in obrobe enobarvna, linearna ali radialna, prosojnost.
- **`VectorGeometry`:** pretvorba v WPF geometrijo in nazaj (kvadratne krivulje v kubične, loki v daljice), logične operacije z `Geometry.Combine` (unija, presek, razlika, izključitev), poravnava in razporejanje, premik in skaliranje (z gradienti), mnogokotniki in zvezde, rasterizacija na STA niti (gladki ali ostri robovi za pixel art).
- **`Svg`:** uvoz podmnožice s poročilom o izpuščenem (brez DTD in zunanjih entitet), izvoz.
- **Urejevalnik:** izbor z okvirjem in Shift, premik in skaliranje z ročicami (Shift ohrani razmerje), vozlišča (vlečenje, ročice zrcaljene ali z Alt ločene, dvoklik gladko ali ostro, brisanje), pero (klik ogel, vlečenje krivulja, zapiranje na prvi točki), pravokotnik, elipsa, mnogokotnik ali zvezda, črta (Shift 45°), polnila in obrobe, prosojnost, spredaj ali zadaj, združevanje in razdruževanje, pretvorba v pot, podvajanje, ravnila in vodila (pripenjanje pri premikanju), kopiranje sloga.
- **Izvoz:** SVG, PNG v več velikostih (posamezne datoteke ali en list), »To pixel art« v Pixel način; v izvoznem cevovodu PNG in SVG.
- **Slike povsod:** `AssetLoader.LoadImage` bere tudi `.pxpaint` in `.pxvec`, zato delujejo kot sloj slike v prizoru, tileset in pri izvozu (Godot in Generic).
- **Testi:** 169/169 (Vector 7: krožno shranjevanje, logične operacije po ploščini, poravnava, razporejanje, premik, skaliranje, SVG uvoz s transformacijami, gradienti, slogom in zavrnitvami, izvoz in ponovni uvoz, rasterizacija, risbe in slike v prizoru in izvozu). Smoke preveri način Vector.
- **Popravki med delom:** SVG transformacije se niso zapekle v točke; »ostri robovi« so imeli polprosojne piksle (RenderTargetBitmap ne upošteva EdgeMode, zdaj prag); preslikava generičnega izvoza prizora bi slikam iz Paint in Vector dala končnico `.wav`; šahovnica prosojnosti se je večala s povečavo (tudi v Paint).
- **Ni ročno preizkušeno:** risanje s peresom in urejanje vozlišč z miško, ročice skaliranja.

## Faza 15: Texture & Material Studio (končana, 20. 10. 2026)
- **Način Texture v zavihku Sprite** (Ctrl+4).
- **`TextureDoc` (`.pxtex` v1)** in **`Textures`:**
  - brezšivni materiali s semenom (kamen, opeka, trava, les, voda, pesek, kovina, zemlja) iz ponavljajočega se šuma z ločenima periodama po oseh in celičnega šuma;
  - viri: recept, slika iz Paint načina ali poljubna slika projekta, s »Seam blend« za slike, ki se ne ponavljajo;
  - izpeljane mape: višina, normale (Sobel s prehodom čez rob, po želji zelena navzdol), okluzija; predogled s svetlobo.
- **Urejevalnik:** vir, material, seme in naključno seme, velikost, gostota, podrobnost, barvi, moč normal in okluzije; pogledi barva, višina, normale, okluzija, osvetljeno; ponovitev 4×4; gradnja v ozadju z zakasnitvijo; »Paint it…« ustvari ali odpre povezano sliko v Paint načinu z vklopljenim risanjem čez rob (nova možnost Wrap v Paint, tipka W).
- **Povsod:** tekstura deluje kot slika (sloji prizorov, tileset), izvoz z normalami in Godot `CanvasTexture`, 8 predlog materialov (korak `texture`), reference iz teksture na sliko se preusmerijo s »Replace everywhere«.
- **Testi:** 174/174 (Texture 5: vsi materiali brez šiva in ponovljivi, smer normal in zelena, okluzija v jamici, odprava šiva na sliki, risanje čez rob, naslikana tekstura in izvoz, predloge materialov). Smoke preveri način Texture in stanje »brez vidnega šiva«.
- **Popravki med delom:** prvotne formule so lomile ponavljanje (šum, raztegnjen za neceli faktor; liho število vrst opek); smoke je po zaprtju predogleda igre odpiral meni, preden je bilo glavno okno spet aktivno.
- **Ni ročno preizkušeno:** risanje teksture v Paint načinu z miško, uvoz `CanvasTexture` v Godot.

## Faza 16: UI & Icon Studio (končana, 20. 10. 2026)
- **Način UI v zavihku Sprite** (Ctrl+5).
- **`UiKit` (`.pxui` v1):**
  - slog s petimi barvami, polmerom, robom, pisavo in ostrimi robovi za pixel art;
  - elementi gumb, panel, dialog, drsnik, vrstica zdravja, polje inventarja, ikona, napis, v štirih stanjih (normal, hover, pressed, disabled);
  - umetnina iz sloga ali iz slik projekta, nine-slice raztezanje.
- **Zasloni:** postavitev s sidri (devet točk in raztezanje), predogled pri drugi ločljivosti; začetni kompleti s slogi Fantasy, Sci-fi, Minimal, Candy in zasloni naslovni, glavni meni, nastavitve, izbira stopnje, zmaga, konec igre, dialog, trgovina, inventar, HUD (4 predloge `ui-*`, korak `ui`).
- **Prizor v2:** UI zaslon nad igro v predogledu igre (narisan enkrat, ne premika se s kamero), v Check scene (manjkajoči komplet ali zaslon) in v izvozu za Godot.
- **Izvoz:** PNG stanj, atlas in JSON; Godot Theme (StyleBoxTexture z robovi nine-slice) in scena `Control` za vsak zaslon. Izvozni profil ima dodatne velikosti (`@2x` …, polje »Extra sizes« v oknu Export).
- **Predogled naprave** (Options → Device preview, Ctrl+Shift+D, za vse načine s sliko): monitorji, prenosnik, telefona, tablica, ročna konzola; celoštevilsko povečanje, velikost v pikslih naprave in centimetrih.
- **Zlati primerki** za `.pxpaint`, `.pxvec`, `.pxtex`, `.pxui` in `.pxscene` v2; pisalec primerkov ne prepiše nobene obstoječe datoteke več.
- **Testi:** 183/183 (UI 5: začetni komplet in zavrnitve, različna stanja in ostri robovi, nine-slice in sidra, zasloni, prekrivni sloj v igri, Godot tema in scene, dodatne velikosti; regresija 4 novi primerki).
- **Smoke:** način UI preverjen. Koraki, ki odpirajo menije (Device preview, paleta ukazov, delovni profil, predloge, zgodovina, izvoz), v tej seji niso tekli: okno aplikacije ni dobilo ospredja, ker je uporabnik delal na računalniku (sonda je potrdila, da se brez ospredja ne odpre noben meni v nobenem načinu). Skripta zdaj to sporoči kot »SMOKE INCOMPLETE« (izhod 2) namesto kot napake. Pritiskov tipk ne simulira, ker bi motili uporabnika.
- **Ni ročno preizkušeno:** vlečenje elementov na zaslonu, okno predogleda naprave, uvoz teme in scen v Godot.

## Faza 17: Animation & Motion Studio in deli telesa (končana, 20. 10. 2026)
- **`.pxanim` v1 (`AnimLibrary`):** posnetki s sledmi za X, Y, vrtenje, merilo in prosojnost; krivulje Linear, EaseIn, EaseOut, EaseInOut, Step in Bezier (CSS); zanka, enkrat, ping-pong, obratno.
- **Način Scene › Motion** (Ctrl+2): časovnica z vrsticami sledi, dodajanje ključev z dvoklikom, vlečenje po času, urejanje vrednosti, krivulje in ročic Bezier, graf vrednosti, predogled, posnetki (nov, preimenuj, podvoji, izbriši), undo.
- **Uporaba posnetkov:**
  - objekt v prizoru predvaja posnetek namesto svoje sledi (urejevalnik, predogled igre, izvoz); gumb »Save track as clip…« prenese obstoječo sled v knjižnico;
  - kamera prizora (tresenje, premiki);
  - UI elementi na zaslonu (`.pxui` v2), tudi v prekrivnem sloju igre.
- **Način Character › Rig** (Ctrl+2, odprta točka C): izrez delov iz sprajta z vlečenjem, pivot z desnim klikom, starševski del, odmik, Z; knjižnica animacij in posnetek za vsako stanje; predogled; preverjanje (neznan starš, zanka, del zunaj sprajta). `.pxchar` v2.
- **Izris:** `Rig.Pose` (hierarhija: vrtenje rame nese roko in dlan) in `Rig.Draw`; predogled igre riše lik iz delov.
- **Izvoz Godot:** `AnimationPlayer` z vzorčenimi posnetki za objekte in kamero; cutout rig (`Sprite2D` z `AtlasTexture` v hierarhiji). Generični JSON: knjižnica, posnetki objektov in kamere, deli lika.
- **Testi:** 192 (Motion 8: načini predvajanja in krivulje, knjižnica in zavrnitve, sled → posnetek, hierarhija riga, lik v2, prizor s posnetkom in rigom, izvoz Godot in generični, UI in kamera; regresija: 4 novi primerki). Pred zaklepom delovne postaje so vsi testi Core uspeli (191 pred zadnjim testom izvoza, ki je uspel posamič). Ob zadnjem zagonu je bila postaja zaklenjena in 5 testov, ki rišejo z WPF (`RenderTargetBitmap`: UI 2, Vector 2, Motion 1), je dobilo prazno sliko; enako se zgodi na commitu a643f92, torej ne gre za regresijo. Ponovi `dotnet test` in smoke ob odklenjeni seji.
- **Smoke:** ni tekel (zaklenjena postaja).
- **Poenostavitve (`ponytail`):** rig v prizoru sledi premiku in zrcaljenju objekta, merila in vrtenja celotnega riga ne upošteva; Godot dobi vzorčene ključe namesto lastnih krivulj; posnetki UI elementov v Godot sceni `Control` niso izvoženi; urejevalnik prizora prikaže lik z deli kot sličico sprajta.
- **Ni ročno preizkušeno:** risanje delov z miško, časovnica z miško, uvoz v Godot.

## Faza 18: 2D Lighting & VFX (končana, 20. 10. 2026)
- **Core:** `Lighting` (ambient, točkovne luči in žarometi z mehkim robom stožca, kvadratni upad, sence z metanjem žarkov skozi mrežo trdnih ploščic in poligonov, sij, utrip) in `Particles` (brez stanja, s semenom, šest prednastavitev). `SceneRenderer` nariše delce, nato svetlobo, nato aditivne delce, da ogenj in iskre svetijo v temi. `SceneAssets.Solids` je skupen za trke v predogledu igre in za sence.
- **`.pxscene` v4** z zlatim primerkom; starejše različice ostanejo neosvetljene.
- **Urejevalnik prizora:** razdelek »Lighting and effects« (ambient, sence ploščic, seznam luči, delcev in okluderjev, dodajanje iz prednastavitev Torch, Lamp, Spotlight, Moonlight, Magic in Smoke, Fire, Rain, Snow, Sparks, Magic, urejanje v pogovornem oknu, brisanje), oznake na platnu z vlečenjem, osvetljen predogled, ki se predvaja s preslednico. Predogled igre je osvetljen samodejno.
- **Predloge:** stopnja dobi neobvezne nastavitve `ambient`, `torches`, `weather`; jama, grad in ječa (različica 1.1) imajo baklje, led ima sneg.
- **Izvoz:** Godot `CanvasModulate`, `PointLight2D`, `LightOccluder2D`, `CPUParticles2D`; generični JSON.
- **Testi:** 199 (osvetlitev 6: upad, stožec, sence ploščic in poligonov, enosmerne ploščice, sij in utrip, delci s semenom, ogreti in omejeni, prizor v4, izvoz Godot in generični, hitrost: 4 luči, okluder in 2000 delcev pod 100 ms na sličico 320×180; predloge: baklje in sneg; regresija: v4). 194 uspešnih; 5 testov z izrisom WPF še vedno pade zaradi zaklenjene postaje (glej Fazo 17).
- **Poenostavitve (`ponytail`):** senčenje teče na procesorju na vsako sličico (za velike zaslone z veliko lučmi bi bil potreben predpomnilnik svetlobne mape); predogled v urejevalniku izriše ves prizor do 4096×4096; sij in utrip v Godot sta samo metapodatka.
- **Ni ročno preizkušeno:** vlečenje oznak, uvoz v Godot. Smoke ni tekel (zaklenjena postaja).

## Faza 19: Asset Optimization in Style Guide (končana, 20. 10. 2026)
- **Core:** `Atlas` (MaxRects z najboljšim ujemanjem krajše stranice, obrezovanje praznih robov, enake slike po SHA-256 shranjene enkrat, več strani, potence števila 2, JSON), `Optimizer` (velikost datotek in pomnilnika, neuporabljena sredstva, enake datoteke, prevelike slike in teksture, ki niso potenca števila 2, prihranek atlasa), `StyleGuide` in `StyleCheck` (mreža velikosti, število barv, dovoljene palete, barva obrobe, debelina vektorskih črt, največja velikost).
- **Okno Project › Optimize assets:** poročilo v ozadju s preklicem, »Point identical files to one copy« (prek `ReplaceReferences`, z zgodovino različic, datotek ne briše), »Write atlas…«.
- **Okno Project › Style guide:** pravila projekta, shranjena v `.pfproj`, in preverjanje vseh sredstev. Check project prikaže kršitve, nov sprajt začne z velikostjo mreže in z barvami dovoljenih palet.
- **Izvoz:** možnost »pack every picture into an atlas« v izvoznem profilu.
- **Testi:** 204 (optimizacija 5: MaxRects brez prekrivanja, obrez, ponovitve, prazne sličice, več strani, 1000 sprajtov v manj kot 10 s brez prekrivanja, poročilo, atlas v izvozu, slogovna pravila in stari projekt brez vodnika). 5 testov z izrisom WPF še pade zaradi zaklenjene postaje.
- **Poenostavitve:** poročilo neuporabljenih sredstev šteje kot uporabljeno vse, na kar kaže drugo sredstvo ali seznam predvajanja, prizori in palete niso nikoli neuporabljeni; atlas za Godot je dodatna datoteka JSON, sprajti še naprej uporabljajo svoje liste.
- **Ni ročno preizkušeno:** okni Optimize in Style guide. Smoke ni tekel (zaklenjena postaja).
