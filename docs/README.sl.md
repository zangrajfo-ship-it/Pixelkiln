# Pixelkiln

Namizno orodje za izdelavo grafike za 2D igre v enem programu: narišeš sprajte, iz njih narediš animirane like in zgradiš mape ploščic.

Teče na Windows (.NET 10, WPF). Brez omrežja, brez računov; vse ostane v tvojih mapah.

Različica 0.1.0 je zgodnja izdaja. Izvoz za Godot še ni preizkušen v pravem Godotu, zato ob prvi rabi preveri uvoz.

## Zagon
Potrebuješ Windows in [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Pixelkiln.App
```

Za razvoj: `dotnet build`, `dotnet test`, `powershell -File tools/smoke.ps1` (preizkus vmesnika v ločeni začasni mapi nastavitev; 0 = v redu, 1 = napaka, 2 = okno ni dobilo ospredja, zato nekateri koraki niso tekli).

Pri preizkušanju pomagata spremenljivki okolja: `PIXELKILN_HOME` prestavi mapo nastavitev iz `%AppData%\Pixelkiln`, `PIXELKILN_TIMING=1` pa zapiše korake zagona in njihove čase v `startup.log` v tej mapi.

## Kaj zna
Ta različica kaže zavihke **Sprite**, **Character** in **Tilemap** ter začetni zaslon.

| Zavihek ali okno | Za kaj |
|---|---|
| **Sprite** | pikselna grafika: plasti, sličice, oznake animacij, onion skin, palete, izbor, preoblikovanje; izvoz PNG, sprite sheet, Aseprite JSON, GIF |
| **Character** | lik na sprajtu: stanja, vezana na oznake animacij, izhodišče, škatle Hurt/Hit/Solid/Trigger, lastnosti za igro |
| **Tilemap** | plasti ploščic, zrcaljene in zavrtene ploščice, objekti, samodejni tereni (16 in 47 ploščic, različice), kolizije ploščic (trdne, enosmerne, kategorije), generator ploščic in postavitev, izvoz Tiled TMJ |
| **Projekt** | Template Studio (Ctrl+T, 18 predlog za vklopljene dele), brskalnik sredstev (iskanje, oznake, priljubljeni, predogledi, varno preimenovanje), preverjanje projekta, zgodovina različic (Ctrl+H), izvoz (Ctrl+Shift+E) |
| **Povsod** | začetni zaslon s prednastavitvami velikosti, zadnjimi datotekami ter novim sprajtom, mapo ali likom, ravnila, vodila in referenčna slika, paleta ukazov (Ctrl+Shift+P), vtičniki s podatki, samodejno shranjevanje in obnovitev, teme Modern, Dark, Light in System, tipkovnica in bralniki zaslona (F1 = bližnjice) |

## Od sprajta do mape
Vsi trije zavihki delajo z istimi datotekami, zato je med njimi en klik:

- **Sprite › Make a character** začne lik na odprtem sprajtu, z enim stanjem za vsako oznako animacije.
- **Sprite › Use as tileset** preda sprajt zavihku Tilemap kot nabor ploščic.
- **Tilemap › Edit tile in Sprite** odpre nabor ploščic v zavihku Sprite in izbere ploščico.
- Ko shraniš sprajt, mapa in lik, ki ga uporabljata, takoj pokažeta spremembo.

Delaš lahko tudi brez projekta. Ob prvem shranjevanju v mapo program vpraša, ali naj iz nje naredi projekt; če odgovoriš z ne, za to mapo ne vpraša več.

## Izklopljeni deli
V kodi so še urejevalniki, ki so v tej različici izklopljeni; prevajajo se in imajo teste. Vklopiš jih tako, da ime zavihka dodaš v `Shown` v `src/Pixelkiln.App/MainWindow.Shell.cs` ali odstraniš komentar v `HostModes` v `MainWindow.xaml.cs`.

| Del | Za kaj |
|---|---|
| Sprite: **Paint**, **Vector**, **Texture**, **UI** | digitalno slikanje (čopiči, pritisk pisala, maske), poti Bezier in SVG, brezšivni materiali z normalno in višinsko mapo, gumbi in zasloni z nine-slice |
| Character: **Rig** | deli telesa iz sprajta s pivotom in starševskim delom, posnetek za vsako stanje |
| **Scene** | stopnje iz map, slik, sprajtov in likov, paralaksa, območja, kamera, luči, sence in delci, predogled igre (F5); knjižnice animacij (Motion); vizualna logika (Logic) |
| **Audio**, **Music**, **SFX** | mešalnik zvokov, sekvencer v slogu čipovske glasbe z boben kompletom in generatorjem pesmi, generator zvočnih učinkov |
| Okna | Optimize assets, Style guide, predogled naprave, delovni profili |

## Za začetek
**Help › Getting started** ima tri kratke lekcije (sprajt, lik, mapa ploščic). Vsaka odpre ustrezno datoteko **vzorčne igre** (Help › Open sample game), ki nastane v `Dokumenti\Pixelkiln samples`.

## Projekt
**Project › New project** ali **Make a project from a folder** ustvari `.pfproj` v mapi z vsebinami. Sredstva dobijo stalne ID-je, zato preimenovanje in premikanje ne pokvarita referenc. Izbrisana sredstva gredo v `.trash`, različice v `.history`, izvozi v `export/`.

Brskalnik sredstev na levi se pokaže, ko je odprt projekt; gumb z mapo v zgornji vrstici (Ctrl+Shift+B) ga pokaže ali skrije.

## Izvoz
- **Godot 4** (4.3 ali novejši): `SpriteFrames`, `TileSet` s kolizijami, `TileMap`, scene likov. Izvozno mapo kopiraj v Godot projekt (`res://assets/…`).
- **Generic:** PNG (tudi v več velikostih), Aseprite JSON, Tiled TMJ, JSON likov, `project.json`, po izbiri atlas.
- Brez projekta gumb **Export** ponudi izvoze odprte datoteke (PNG, GIF, sprite sheet, JSON lika, Tiled).
- Izvoz projekta je determinističen, pred zapisom preveri projekt in nikoli ne pusti napol zapisanih datotek. Seznam zapisanih datotek je v `export-report.txt`.

## Vtičniki in predloge
Samo podatki (palete, prednastavitve generatorjev, izvozni profili, makri ukazov), brez kode. Glej [plugins.md](plugins.md), [templates.md](templates.md) in vzorec v `samples/plugins/retro-pack`.

## Formati
Opis vseh datotek in različic je v [formats.md](formats.md). Stari formati se vedno odprejo (zlati primerki v `tests/Pixelkiln.Core.Tests/Fixtures`).

## Licenca
[MIT](../LICENSE). Priloženi pisavi Inter in JetBrains Mono imata licenco SIL Open Font License 1.1 (`src/Pixelkiln.App/Fonts`). Edina zunanja odvisnost je NAudio.WinMM (MIT) za predvajanje zvoka.
