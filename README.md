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
| **Sprite** | risanje po plasteh in sličicah, oznake animacij, onion skin, palete, izbor, preoblikovanje, ponavljajoče ploščice, uvoz in izvoz listov sprajtov (Aseprite JSON), GIF, generator sprajtov s semenom |
| **Character** | lik na sprajtu: stanja (idle, run …), izhodišče, smer, škatle Hurt/Hit/Solid/Trigger po sličicah, lastnosti, predogled, izvoz |
| **Tilemap** | plasti ploščic, objekti, samodejni tereni (16 in 47 ploščic, različice), kolizije ploščic (trdne, enosmerne, kategorije), podvojene ploščice, generator ploščic in postavitev (jame, platforme), izvoz Tiled TMJ |
| **Scene** | stopnje iz map, slik, sprajtov in likov: paralaksa, gibalne sledi, območja (spawn, checkpoint, trigger, camera, kill), kamera, glasba, preverjanje, **predogled igre (F5)** |
| **Audio** | mešalnik projektnih zvokov: sledi, rez, pojemanje, ponovitve, združevanje, zanka za glasbo, predvajalni seznami, izvoz WAV |
| **Music, SFX** | sekvencer v slogu čipovske glasbe, generator zvočnih učinkov |
| **Projekt** | brskalnik sredstev (iskanje, oznake, priljubljeni, predogledi, varno preimenovanje), preverjanje projekta, nastavitve in matrika trkov, Palette studio, **zgodovina različic** (Ctrl+H), **izvoz** (Ctrl+Shift+E) |
| **Povsod** | paleta ukazov (Ctrl+Shift+P), vtičniki s podatki, samodejno shranjevanje in obnovitev, temna/svetla tema, tipkovnica in bralniki zaslona (F1 = bližnjice) |

## Projekt
**Project → New project** ali **Make project from folder** ustvari `.pfproj` v mapi z vsebinami. Sredstva dobijo stalne ID-je, zato preimenovanje in premikanje ne pokvarita referenc. Izbrisana sredstva gredo v `.trash`, različice v `.history`, izvozi v `export/`.

## Izvoz
- **Godot 4** (4.3+): `SpriteFrames`, `TileSet` s kolizijami, `TileMap`, scene likov in stopenj z `Parallax2D`, `AnimationPlayer`, območji, kamero in glasbo. Izvozno mapo kopiraj v Godot projekt (`res://assets/…`).
- **Generic:** PNG, Aseprite JSON, Tiled TMJ, JSON likov in prizorov, WAV, `project.json`.
- Izvoz je determinističen, pred zapisom preveri projekt in nikoli ne pusti napol zapisanih datotek. Poročilo je v `export-report.txt`.

Izvoz za Godot še ni preizkušen v pravem Godotu, zato ob prvi rabi preveri uvoz; seznam vseh datotek je v `export-report.txt`.

## Vtičniki
Samo podatki (palete, prednastavitve generatorjev, izvozni profili, makri ukazov), brez kode. Glej [docs/plugins.md](docs/plugins.md) in vzorec v `samples/plugins/retro-pack`.

## Formati
Opis vseh datotek in različic: [.ai/context/formats.md](.ai/context/formats.md). Stari formati se vedno odprejo (zlati primerki v `tests/PixelForge.Core.Tests/Fixtures`).

## Za razvijalce
`CLAUDE.md` (pravila projekta), `AGENT.md`, `.ai/architecture/` (arhitektura in ADR), `.ai/sessions/` (dnevnik dela po fazah).

Edina zunanja odvisnost je NAudio.WinMM (MIT) za predvajanje zvoka.
