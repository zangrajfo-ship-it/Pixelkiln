# Formati datotek

Vsi formati so JSON (UTF-8). Branje in pisanje gre skozi `DocumentFile`:
- poškodovana datoteka sproži `InvalidDataException` z imenom datoteke;
- datoteko novejše različice program zavrne;
- shranjevanje je atomično (`.tmp` + `File.Replace`);
- pred prvim prepisom starejše različice se naredi `<datoteka>.v<n>.bak`.

Slikovne pike so `uint` ARGB (`0xAARRGGBB`), vrstica za vrstico, kodirane v base64 kot little-endian bajti (= Bgra32). Zlati primerki: `tests/PixelForge.Core.Tests/Fixtures/`. Regresija: `FormatRegressionTests`.

## `.pxf`: sprajt (`ProjectFile`)
| Polje | Od | Opis |
|---|---|---|
| `Version` | v1 | trenutno 2 |
| `Width`, `Height` | v1 | 1–4096 |
| `Palette` | v1 | `uint[]` ARGB |
| `FrameDurations` | v1 | ms na sličico |
| `Layers[]` | v1 | `Name`, `Visible`, `Cels[]` (base64, ena na sličico) |
| `Layers[].Opacity` | v2 | 0–1, privzeto 1 (v2 datoteke pred commitom 9144477 ga nimajo) |
| `Tags[]` | v2 | `Name`, `From`, `To` (vključno); neveljavni razponi se preskočijo |

## `.pxm`: zemljevid (`Tilemap.Serialize`)
| Polje | Od | Opis |
|---|---|---|
| `Version` | v1 | trenutno 5 |
| `Width`, `Height`, `TileSize` | v1 | ploščice; 1–1024, velikost 1–256 |
| `TilesetSource` | v1 | absolutna pot vira ali `null` (samo za »Reload«) |
| `TilesetWidth`, `TilesetHeight`, `TilesetPixels` | v1 | vgrajena kopija tileseta (base64) ali `null` |
| `Layers[]` | v1 | `Name`, `Visible`, `Tiles[]` (indeks ploščice, −1 prazno) |
| `Objects[]` | v2 | `Id`, `Name`, `Type`, `X`, `Y`, `W`, `H` v pikah; `W=H=0` je točka |
| `Terrains[]` | v2 | indeks zgornje leve ploščice bloka 4×4 (`Autotile`) |
| `TilesetAssetId` | v3 | GUID sredstva tileseta v projektu ali `null`; vgrajene pike ostanejo kot rezerva |
| `TerrainRules[]` | v4 | `Base`, `Mode` (`Blob16`/`Blob47`), `Variants[]` (naključne različice polne ploščice); v2/v3 `Terrains` se prebere kot Blob16 |
| `TileCollisions[]` | v5 | `Tile`, `X`, `Y`, `W`, `H` (v ploščici), `OneWay`, `Category` (0–31); pravokotnik mora biti znotraj ploščice |

## `.pxchar`: lik (`Character`)
v1. `Kind` (Player, Enemy, Npc, Item), `SpriteAssetId`, `Animations{stanje → oznaka sprajta}`, `OriginX/Y`, `Facing`, `Boxes[]` (`Kind` Hurt/Hit/Solid/Trigger, `State`, `Frame` ali `null` za vse, `X`, `Y`, `W`, `H`, `Category`), `Properties{}`. Enumi kot besedilo.

## `.pxscene`: prizor (`Scene`)
| Polje | Od | Opis |
|---|---|---|
| `Version` | v1 | trenutno 1 |
| `Width`, `Height`, `Background` | v1 | velikost v pikah (16–65536), barva ARGB |
| `Level`, `Order` | v1 | ime stopnje in vrstni red v igri |
| `CameraX`, `CameraY` | v1 | začetek kamere (velikost pogleda = `TargetWidth/Height` projekta) |
| `MusicAssetId` | v1 | pesem ali `null` |
| `Layers[]` | v1 | `Id`, `Name`, `Kind` (Tilemap, Image, Instances), `AssetId`, `ParallaxX/Y`, `OffsetX/Y`, `RepeatX`, `Visible`; prva je zadaj, vsaj ena |
| `Instances[]` | v1 | `Id`, `Name`, `AssetId` (sprajt ali lik), `LayerId`, `X`, `Y` (izhodišče), `Animation`, `FlipX`, `Z`, `Track` (`KeyframeTrack`: `Keys[]` čas, položaj, merilo, rotacija, prosojnost, `Easing`; `Loop`), `Properties{}` |
| `Areas[]` | v1 | `Id`, `Kind` (Trigger, Checkpoint, Spawn, Camera, Kill), `Name`, `X`, `Y`, `W`, `H` (≥ 1), `Properties{}` |

Podvojeni ID-ji in prizor brez plasti se zavrnejo. Reference so samo ID-ji sredstev, zato preimenovanje in premik ne pokvarita prizora.

## `.pxs`: pesem (`Song.Serialize`)
| Polje | Od | Opis |
|---|---|---|
| `Version` | v1 | trenutno 2 |
| `Bpm` | v1 | 20–400 |
| `Steps` | v1 | koraki na vzorec (16-inke), 1–256 |
| `Order` | v1 | indeksi vzorcev za predvajanje |
| `Patterns` | samo v1 | `int[kanal][korak]`: MIDI nota ali −1 |
| `PatternData[]` | v2 | `Notes` (−1 = nič, −2 = Hold), `Volumes` (0–15) |
| `Instruments[4]` | v2 | `Duty`, `AttackMs`, `DecayMs`, `Sustain`, `ReleaseMs`, `Volume` |

## `.sfx`: zvočni učinek (`Sfx.Save`)
v1: objekt `Sfx` brez polja `Version`. v2 (trenutno): enaka polja + `"Version": 2` na začetku, brez `Duration`. Polja: `Wave` (0 Square, 1 Saw, 2 Sine, 3 Noise), `Freq`, `Slide`, `DeltaSlide`, `VibratoDepth`, `VibratoSpeed`, `Duty`, `PitchJump`, `JumpTime`, `Attack`, `Sustain`, `Decay`, `LowPass`, `Volume`. Vse vrednosti se ob branju omejijo (`Sfx.Sanitized`).

## `.pxmix`: zvočna mešanica (`Mix`)
| Polje | Od | Opis |
|---|---|---|
| `Version` | v1 | trenutno 1 |
| `MasterVolume` | v1 | 0–4 |
| `LoopStartMs`, `LoopEndMs` | v1 | zanka za glasbo v igri; oba `null` ali konec > začetek |
| `Tracks[]` | v1 | `Id`, `Name`, `Volume` (0–4), `Mute`, `Solo`, `Clips[]` |
| `Clips[]` | v1 | `Id`, `AssetId` (Song, Sound, Audio, Mix), `StartMs`, `OffsetMs` + `LengthMs` (rez; `null` = do konca), `Gain` (0–8), `FadeInMs`, `FadeOutMs`, `Repeat` (1–1000) |

Rez, pojemanje in združevanje so nedestruktivni: vir ostane nespremenjen. Mešanica, ki vsebuje samo sebe, se zavrne ob predvajanju.

## `.wav` (sredstvo `Audio`)
Bere se PCM 8/16/24/32 in float 32, poljubno število kanalov (povprečje v mono), pretvorba na 44.100 Hz, največ 10 minut. Izvoz: 16-bit PCM mono prek začasne datoteke.

## `.pfproj`: projekt (`Project`)
| Polje | Od | Opis |
|---|---|---|
| `Version` | v1 | shema manifesta |
| `Id`, `Name` | v1 | GUID projekta, ime |
| `Settings` | v1 | `TargetWidth`, `TargetHeight`, `TileSize`, `CollisionCategories{bit → ime}`, `CollisionMasks{bit → maska}` (manjka = trči z vsem; trk velja, ko ga dovolita obe strani), `Playlists[]` (`Name`, `Items[]` ID-ji, `Loop`, `Shuffle`), `ExportProfiles[]`, `HistoryMaxVersions`, `HistoryMaxMegabytes`, `Views{ID sredstva → GuidesX[], GuidesY[], Reference, ReferenceOpacity, ReferenceVisible}` (vodila in referenčna slika; pomoč pri urejanju, ne vsebina) |
| `Assets[]` | v1 | `Id` (GUID, stabilen), `Type` (besedilo enuma `AssetType`), `Path` (relativna, ne sme iz mape), `Tags[]`, `Favorite` |

Odstranjena sredstva gredo v `.trash/`. Mape `.history`, `.trash` in `export` niso sredstva.

## `settings.json` (`%AppData%\PixelForge`)
Nastavitve aplikacije (`Settings`); neveljavne vrednosti se omejijo, pokvarjena datoteka da privzete vrednosti.

## Izvozi (javne pogodbe)
- **Aseprite JSON (array)** iz `SpriteSheet.Json`: `frames[]` (`filename`, `frame`, `duration` …), `meta` (`image`, `size`, `frameTags[]`).
- **Tiled TMJ** iz `TiledExport.Json`: `tilelayer` na plast (`gid` = indeks + 1, 0 prazno), `objectgroup` »Objects«, en tileset (`firstgid` 1), od Faze 4 še `tiles[].objectgroup` s kolizijami ploščic.
- **GIF89a** (`GifWriter`): ena globalna paleta, največ 255 barv + prosojna, zanka.
- **WAV:** 16-bit PCM mono 44,1 kHz (`WavWriter`).

## Izvozni cevovod (`Exporter`, Faza 7)
Profili so v `.pfproj` (`Settings.ExportProfiles[]`): `Name`, `Target` (`Generic`, `Godot4`), `OutputFolder` (znotraj projekta, ne sme vsebovati sredstev), `ResFolder` (Godot: `res://<ResFolder>/…`), `Zip`, `Assets` (`null` = vse; sicer izbrana in vse, kar uporabljajo).

Potek: preverjanje (manjkajoča in neznana sredstva, napake `SceneCheck`, neveljavna mapa), gradnja vseh datotek v pomnilniku, zapis v `<mapa>.staging` in zamenjava mape v enem koraku. Ob napaki prejšnji izvoz ostane. Enak vhod da enake bajte (urejene poti, brez časovnih žigov, zip s fiksnim časom). Vsak izvoz ima `export-report.txt` (datoteke, velikost, SHA-256) in `project.json` (ločljivost, ploščica, kategorije in maske trkov, predvajalni seznami, zanke glasbe, seznam izvoženih sredstev).

Imena: mapa sredstva + varno ime (`[A-Za-z0-9_-]`); podvojena imena dobijo `_2`, a samo, če bi se izhodne datoteke res prekrivale.

| Sredstvo | Generic | Godot 4 (format=3, cilj 4.3+) |
|---|---|---|
| Sprite | `ime.png` (list) + `ime.json` (Aseprite) | `ime.png` + `ime.tres` (`SpriteFrames`: animacija na oznako, `speed` = 1000 / najkrajša sličica, `duration` = večkratnik) |
| Image | `ime.png` | `ime.png` |
| Tilemap | `ime.tmj` + `ime_tiles.png` (+ `ime_terrain.json`) | `ime_tiles.png` + `ime_tileset.tres` (`TileSetAtlasSource`, poligoni trkov, `one_way`, fizikalna plast na kategorijo z masko) + `ime.tscn` (`TileMap` format 2, plasti, objekti kot `Marker2D`/`Area2D`) |
| Character | `ime_character.json` | `ime_character.tscn` (`CharacterBody2D` za Player/Enemy, sicer `Area2D`; `AnimatedSprite2D` z izhodiščem, trdna škatla kot `CollisionShape2D`, ostale škatle in lastnosti kot metapodatki) |
| Song, Sound, Audio, Mix | `ime.wav` | `ime.wav` (zanko mešanice je treba nastaviti v Godotovem uvozu; navedeno v poročilu) |
| Scene | `ime_scene.json` (struktura prizora s potmi datotek) | `ime.tscn` (plasti po vrsti, `Parallax2D` za paralakso in ponavljanje, instance sprajtov in likov, sledi kot `AnimationPlayer` s položajem, merilom, rotacijo in prosojnostjo, območja kot `Area2D` v skupini po vrsti, `Camera2D` z mejami, `AudioStreamPlayer` za glasbo) |
| Palette | kopija | kopija |

Izvoz za Godot ni preverjen z dejanskim Godotom (ni nameščen); oblika sledi Godotovi besedilni shemi `format=3`.

## `template.json` in `.pxtplpack` (predloge, Faza 12)
`apiVersion` (1), `id`, `name`, `version` (1.0 ali 1.2.3), `kind` (Character, Level, Scene, Asset, Music, Pack), `primary` (ID koraka), `steps[]`, `tags[]`, `description`, `parameters[]` (`key`, `label`, `default`, `options[]` ali `source` = vrsta sredstva iz projekta). Vrste korakov in nastavitve: `docs/templates.md`. Mapa predloge: `<id>@<različica>/`. Paket `.pxtplpack` je zip takih map, največ 64 MB in 2000 datotek, brez poti iz paketa.

## `.pxpaint`: slika za digitalno slikanje (`PaintDoc`, Faza 13)
Zip: `manifest.json` (`Version` 1, `Width`, `Height` do 8192, `Layers[]`: `Id`, `Name`, `Visible`, `Opacity`, `Blend` = Normal, Multiply, Screen, Overlay, Add, Darken, Lighten, `AlphaLocked`, `HasMask`) in ploščice 256×256 kot PNG: `layers/<i>/<tx>_<ty>.png` (ARGB, prazne ploščice se ne zapišejo) in `layers/<i>/mask_<tx>_<ty>.png` (vrednost maske v alfa kanalu; manjkajoča ploščica maske pomeni vidno). Shranjevanje prek `.tmp`. Novejša različica se zavrne z razlogom.
