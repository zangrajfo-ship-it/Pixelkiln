# Vtičniki za PixelForge

Vtičnik je mapa s datoteko `plugin.json`. Vsebuje samo podatke: palete, prednastavitve generatorjev, izvozne profile in makre ukazov. PixelForge kode iz vtičnikov ne nalaga in ne izvaja (ADR-0002), zato vtičnik ne more brati datotek, dostopati do omrežja ali spreminjati sistema.

## Namestitev
- **Options → Plugins… → Install from folder…** preveri vtičnik in ga kopira v mapo vtičnikov.
- Ročno: mapo kopiraj v `%AppData%\PixelForge\plugins\` (ali `<PIXELFORGE_HOME>\plugins\`) in klikni **Reload**.
- Ime mape mora biti enako `id` vtičnika.

Ukazi vtičnikov so v paleti ukazov (Ctrl+Shift+P) pod »Plugin › ime vtičnika ›«.

## `plugin.json`
```jsonc
{
  "apiVersion": 1,                 // obvezno; drugačna različica se zavrne
  "id": "retro-pack",              // 2–64 znakov: male črke, števke, . - _ ; enako imenu mape
  "name": "Retro pack",            // do 80 znakov
  "version": "1.0.0",              // 1.0 ali 1.2.3 (dovoljen je tudi -beta)
  "description": "…",              // neobvezno
  "author": "…",                   // neobvezno
  "contributions": {
    "palettes": [ { "name": "…", "colors": ["#1D2B53", "#80FF0000"] } ],
    "generatorPresets": [
      { "name": "…", "generator": "sprite", "settings": { "seed": "critter", "density": "0.52" } },
      { "name": "…", "generator": "tiles",  "settings": { "seed": "castle", "pattern": "Bricks", "baseColor": "#8A6F5A", "variants": "4" } },
      { "name": "…", "generator": "layout", "settings": { "seed": "deep", "layout": "Cave", "fill": "0.5" } }
    ],
    "exportProfiles": [ { "name": "…", "target": "Godot4", "outputFolder": "export/godot-retro", "resFolder": "retro", "zip": true } ],
    "macros": [ { "name": "…", "commands": ["Sprite › Edit › Flip horizontally", "Sprite › Edit › Flip vertically"] } ]
  }
}
```

## Pravila preverjanja
- Neznan ključ je napaka (tipkarske napake se ne prezrejo). Komentarji `//` in vejica na koncu so dovoljeni.
- Palete: 1–256 barv, `#RRGGBB` ali `#AARRGGBB`.
- Generatorji: `sprite` (`seed`, `density` med 0 in 1), `tiles` (`seed`, `pattern` = Noise, Bricks, Grass, Stones, `baseColor`, `variants` 1–16), `layout` (`seed`, `layout` = Cave ali Platforms, `fill` med 0 in 1). Seme je število ali beseda; enako seme da enak rezultat.
- Izvozni profili: `target` = `Generic` ali `Godot4`; `outputFolder` je relativna mapa znotraj projekta (brez `..`). Ukaz profil doda v odprti projekt.
- Makri: 1–50 naslovov ukazov, natanko tako, kot jih kaže paleta ukazov (brez bližnjice). Pred zagonom se preveri, ali vsi obstajajo. Makro se ustavi pri prvem ukazu, ki trenutno ni na voljo. Makri ne morejo klicati drugih ukazov vtičnikov.
- `plugin.json` je lahko velik največ 1 MB. Dva vtičnika z istim `id` se ne naložita oba.

Zavrnjeni vtičniki so z razlogom navedeni v oknu **Options → Plugins…**; aplikacija teče naprej.

Primer: [`samples/plugins/retro-pack/plugin.json`](../samples/plugins/retro-pack/plugin.json).
