# Formati datotek

Vsi formati so JSON (UTF-8). Slikovne pike so `uint` ARGB (`0xAARRGGBB`), vrstica za vrstico, kodirane v base64 kot little-endian bajti (= Bgra32). Zlati primerki: `tests/PixelForge.Core.Tests/Fixtures/`. Regresija: `FormatRegressionTests`.

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
| `Version` | v1 | trenutno 2 |
| `Width`, `Height`, `TileSize` | v1 | ploščice; 1–1024, velikost 1–256 |
| `TilesetSource` | v1 | absolutna pot vira ali `null` (samo za »Reload«) |
| `TilesetWidth`, `TilesetHeight`, `TilesetPixels` | v1 | vgrajena kopija tileseta (base64) ali `null` |
| `Layers[]` | v1 | `Name`, `Visible`, `Tiles[]` (indeks ploščice, −1 prazno) |
| `Objects[]` | v2 | `Id`, `Name`, `Type`, `X`, `Y`, `W`, `H` v pikah; `W=H=0` je točka |
| `Terrains[]` | v2 | indeks zgornje leve ploščice bloka 4×4 (`Autotile`) |

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
Objekt `Sfx`, **brez polja `Version`** (velja kot v1). Polja: `Wave` (0 Square, 1 Saw, 2 Sine, 3 Noise), `Freq`, `Slide`, `DeltaSlide`, `VibratoDepth`, `VibratoSpeed`, `Duty`, `PitchJump`, `JumpTime`, `Attack`, `Sustain`, `Decay`, `LowPass`, `Volume`. Zapiše se tudi izračunani `Duration`, ki se pri branju ne uporabi. Vse vrednosti se ob branju omejijo (`Sfx.Sanitized`).
**Načrt Faze 1:** dodati `Version` (odsotnost = v1).

## `settings.json` (`%AppData%\PixelForge`)
Nastavitve aplikacije (`Settings`); neveljavne vrednosti se omejijo, pokvarjena datoteka da privzete vrednosti.

## Izvozi (javne pogodbe)
- **Aseprite JSON (array)** iz `SpriteSheet.Json`: `frames[]` (`filename`, `frame`, `duration` …), `meta` (`image`, `size`, `frameTags[]`).
- **Tiled TMJ** iz `TiledExport.Json`: `tilelayer` na plast (`gid` = indeks + 1, 0 prazno), `objectgroup` »Objects«, en tileset (`firstgid` 1).
- **GIF89a** (`GifWriter`): ena globalna paleta, največ 255 barv + prosojna, zanka.
- **WAV:** 16-bit PCM mono 44,1 kHz (`WavWriter`).
