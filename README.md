# PixelForge

Namizno orodje za izdelavo vsebin za 2D igre v enem programu: pikselna grafika, liki, ploščice in mape, prizori in stopnje, glasba, zvočni učinki, predogled igre in izvoz za Godot 4.

Teče na Windows (.NET 10, WPF). Brez omrežja, brez računov; vse ostane v tvojih mapah.

## Zagon
```bash
dotnet run --project src/PixelForge.App
```
Za razvoj: `dotnet build`, `dotnet test`, `powershell -File tools/smoke.ps1` (UI smoke test v ločenem začasnem domu).

## Kaj zna
| Zavihek ali okno | Za kaj |
|---|---|
| **Sprite** | načini (Ctrl+1 … 5): **Pixel** (plasti, sličice, oznake animacij, onion skin, palete, izbor, preoblikovanje, Aseprite JSON, GIF, generator s semenom), **Paint** (digitalno slikanje: čopiči, pritisk pisala, načini mešanja, maske, platna do 8192²), **Vector** (poti Bezier, oblike, gradienti, združevanje oblik, SVG), **Texture** (brezšivni materiali z normalno, višinsko in okluzijsko mapo), **UI** (gumbi, okna, vrstice v štirih stanjih, nine-slice, zasloni s sidri) |
| **Character** | **States**: lik na sprajtu, stanja, izhodišče, škatle Hurt/Hit/Solid/Trigger, lastnosti; **Rig**: deli telesa iz sprajta s pivotom, starševskim delom in Z, posnetek za vsako stanje |
| **Tilemap** | plasti ploščic, objekti, samodejni tereni (16 in 47 ploščic, različice), kolizije ploščic (trdne, enosmerne, kategorije), podvojene ploščice, generator ploščic in postavitev (jame, platforme), izvoz Tiled TMJ |
| **Scene** | **Layout**: stopnje iz map, slik, sprajtov in likov, paralaksa, gibalne sledi in posnetki, območja, kamera in njeni premiki, glasba, UI nad igro, **luči, sence in delci**, preverjanje, **predogled igre (F5)**; **Motion**: knjižnice animacij s časovnico in krivuljami Bezier; **Logic**: vizualna logika (dogodki, pogoji, dejanja), ki teče v predogledu igre |
| **Audio** | mešalnik projektnih zvokov: sledi, rez, pojemanje, ponovitve, združevanje, zanka za glasbo, predvajalni seznami, izvoz WAV |
| **Music, SFX** | sekvencer v slogu čipovske glasbe: dva pravokotna kanala, trikotni bas in **boben komplet** (kick, snare, hi-hat, toms, clap, crash) s pripravljenimi ritmi in generatorjem pesmi; generator zvočnih učinkov |
| **Projekt** | Template Studio (Ctrl+T, 34 vgrajenih predlog za like, stopnje, sredstva, materiale, UI in glasbo), Optimize assets (velikosti, neuporabljena in enaka sredstva, atlas), Style guide (pravila sloga in preverjanje), brskalnik sredstev (iskanje, oznake, priljubljeni, predogledi, varno preimenovanje), preverjanje projekta, nastavitve in matrika trkov, Palette studio, **zgodovina različic** (Ctrl+H), **izvoz** (Ctrl+Shift+E) |
| **Povsod** | predogled naprave (Ctrl+Shift+D), ravnila, vodila in referenčna slika, kopiranje sloga (Ctrl+Alt+C/V), delovni profili, paketna obdelava v brskalniku, paleta ukazov (Ctrl+Shift+P), vtičniki s podatki, samodejno shranjevanje in obnovitev, temna/svetla tema, tipkovnica in bralniki zaslona (F1 = bližnjice) |

## Za začetek
**Help › Getting started** ima sedem kratkih lekcij (sprajt, lik, mapa ploščic, stopnja, ritem, pesem, zvoki in svetloba). Vsaka odpre pravi del **vzorčne igre** (Help › Open sample game ali Project › Open sample game): dve stopnji z ročno narisano grafiko, kovanci, logiko, glasbo z bobni in zvoki, v `Dokumenti\PixelForge samples`. Template Studio (Ctrl+T) ima še predloge »Sample: …« z narisanimi liki, kovancem, predmeti in ploščicami ter »Drum beat«.

## Projekt
**Project → New project** ali **Make project from folder** ustvari `.pfproj` v mapi z vsebinami. Sredstva dobijo stalne ID-je, zato preimenovanje in premikanje ne pokvarita referenc. Izbrisana sredstva gredo v `.trash`, različice v `.history`, izvozi v `export/`.

## Izvoz
- **Godot 4** (4.3+): `SpriteFrames`, `TileSet` s kolizijami, `TileMap`, scene likov (tudi cutout rig) in stopenj z `Parallax2D`, `AnimationPlayer`, območji, kamero, glasbo, UI (`Theme`, scene `Control`), lučmi (`PointLight2D`, `LightOccluder2D`), delci (`CPUParticles2D`) in logiko (ustvarjen GDScript). Izvozno mapo kopiraj v Godot projekt (`res://assets/…`).
- **Generic:** PNG (tudi v več velikostih), Aseprite JSON, Tiled TMJ, JSON likov, prizorov, animacij, UI in logike, WAV, `project.json`, po izbiri atlas (`atlas/atlas.json`).
- Izvoz je determinističen, pred zapisom preveri projekt in nikoli ne pusti napol zapisanih datotek. Poročilo je v `export-report.txt`.

Izvoz za Godot še ni preizkušen v pravem Godotu, zato ob prvi rabi preveri uvoz; seznam vseh datotek je v `export-report.txt`.

## Vtičniki
Samo podatki (palete, prednastavitve generatorjev, izvozni profili, makri ukazov), brez kode. Glej [docs/plugins.md](docs/plugins.md) in vzorec v `samples/plugins/retro-pack`.

## Formati
Opis vseh datotek in različic: [.ai/context/formats.md](.ai/context/formats.md). Stari formati se vedno odprejo (zlati primerki v `tests/PixelForge.Core.Tests/Fixtures`).

## Za razvijalce
`CLAUDE.md` (pravila projekta), `AGENT.md`, `.ai/architecture/` (arhitektura in ADR), `.ai/sessions/` (dnevnik dela po fazah).

Edina zunanja odvisnost je NAudio.WinMM (MIT) za predvajanje zvoka.
