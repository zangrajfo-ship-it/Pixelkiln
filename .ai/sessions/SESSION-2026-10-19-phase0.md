# Seja 19. 10. 2026: Faza 0 (analiza in tehnična ocena)

## Cilj
Analiza obstoječega PixelForga, dokumentacija za agenta, arhitekturni predlog in načrt faz 1–10 iz `../prompts/master-prompt.md`. Brez implementacije novih funkcij.

## Odločitve lastnika
- Vtičniki: samo podatki (ADR-0002).
- Prvi izvozni profil: Godot 4.
- Zgodovina: mapa `.history` (ADR-0003).
- Tempo: faza za fazo, z odobritvijo po vsaki fazi.

## Dokončano
- Osnovna meritev (commit `9144477`):
  - `dotnet build`: 0 napak, 0 opozoril (16 s);
  - `dotnet test`: 62/62;
  - `tools/smoke.ps1`: SMOKE OK (4 zavihki, meniji File/Options/Help povsod, izhod v 5 s).
- Zlati primerki formatov v `tests/PixelForge.Core.Tests/Fixtures/`:
  - `sprite.v1.pxf`, `sprite.v2.pxf`;
  - `map.v1.pxm`, `map.v2.pxm`;
  - `song.v1.pxs`, `song.v2.pxs`;
  - `sound.v1.sfx`.
- `FormatRegressionTests` (9 testov). Po dodajanju je `dotnet test` 71/71.
- `CLAUDE.md`, `AGENT.md`.
- Mapa `.ai/`: agents (5), skills (3), prompts, workflows, architecture (current, target, ADR 0001–0003), context/formats, sessions.
- `tools/smoke.ps1` (UI Automation; posnetki samo okna aplikacije).

## Delno ali ni narejeno
- CodeGraph: projekt ni indeksiran; indeksiranje je odločitev lastnika (`codegraph init`). Strežnik teče na 1.6.0, na voljo je 1.6.2.
- `codegraph/index.html` se v projektu ne uporablja.

## Spremenjene datoteke
- **Novo:**
  - `CLAUDE.md`, `AGENT.md`, `tools/smoke.ps1`;
  - `.ai/**`;
  - `tests/PixelForge.Core.Tests/FormatRegressionTests.cs`, `tests/PixelForge.Core.Tests/Fixtures/*`.
- **Spremenjeno:** `tests/PixelForge.Core.Tests/PixelForge.Core.Tests.csproj` (kopiranje `Fixtures`).
- `src/` ni spremenjen.

## Ugotovitve analize (tehnični dolg)
1. Ni projekta; `.pxm` kopira tileset in hrani absolutno pot.
2. `.sfx` nima `Version`.
3. Migracije razpršene po DTO-jih, brez skupnega vzorca in brez atomičnega shranjevanja.
4. Podvojeno stanje dokumenta v štirih urejevalnikih (pot, spremembe, naslov, potrditev, undo) in podvojena povečava.
5. `History<T>` = cele kopije (pomnilnik).
6. Polni izris ob vsakem premiku miške.
7. Statični singletoni `App.Settings`, `AudioOut.Shared`.
8. UI brez avtomatskih testov (zdaj smoke).
9. `SongStream` bere pesem brez zaklepanja (ujeto v `AudioOut`).
10. Ni README.

## Testi
| Ukaz | Rezultat |
|---|---|
| `dotnet build` | 0 napak, 0 opozoril |
| `dotnet test` (pred) | 62/62 |
| `dotnet test` (po) | 71/71 |
| `tools/smoke.ps1` | SMOKE OK |

## Naslednji koraki (Faza 1, čaka odobritev)
1. `IDocumentFormat<T>` in atomično shranjevanje (`.tmp` + `File.Replace`, `.bak` ob migraciji) za vse štiri formate.
2. `.sfx` v2 z `Version`.
3. Manifest `.pfproj`, `AssetEntry` z GUID, `ProjectService` (dodaj, odstrani, preimenuj, razreši pot).
4. `.pxm` v3 s `tilesetAssetId` in rezervo na vgrajene pike.
5. Testi: krožni testi, poškodovane datoteke, prekinjeno shranjevanje, podvojeni ID-ji, migracija ne uniči izvirnika, obremenitev (velik sprajt, 5.000 sredstev).

## Stanje
- **Build:** zelen.
- **CodeGraph:** ni indeksa.
