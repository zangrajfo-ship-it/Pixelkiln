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
