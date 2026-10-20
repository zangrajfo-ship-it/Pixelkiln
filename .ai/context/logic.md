# Vizualna logika (`.pxlogic`): specifikacija semantike

Velja za interpreter v PixelForge (predogled igre) in za izvoz v GDScript. Kjer se Godot obnaša drugače, je to navedeno.

## Graf
- **Vozlišče:** `Id` (edinstven niz), `Kind`, `X`, `Y` (položaj v urejevalniku), `Params` (niz → niz).
- **Povezava:** `From` (vozlišče), `Port`, `To` (vozlišče). Povezave nosijo samo potek izvajanja, podatkov ne. Podatki so v spremenljivkah.
- **Vrata:** dogodki in dejanja imajo izhod `out`, pogoji `true` in `false`. Dogodek ne more biti cilj povezave.
- **Prizor** kaže na graf z `Scene.LogicAssetId` (`.pxscene` v5).

## Vrste vozlišč
| Skupina | `Kind` | Parametri | Pomen |
|---|---|---|---|
| Dogodek | `Start` | – | ob prvi sličici |
| | `EnterArea` | `area` | igralec vstopi v območje s tem imenom |
| | `Touch` | `object` | igralčevo telo se začne dotikati objekta s tem imenom |
| | `Timer` | `ms` (≥ 1), `repeat` (`yes`/`no`) | po `ms` od začetka, pri `repeat` vsakih `ms` |
| | `StateChanged` | `object`, `state` | dejanje `SetState` je objektu nastavilo to stanje |
| Pogoj | `Compare` | `variable`, `op` (`==`, `!=`, `<`, `<=`, `>`, `>=`), `value` (število) | primerja spremenljivko (privzeto 0) |
| | `HasItem` | `item`, `count` (privzeto 1) | v inventarju je vsaj `count` predmetov |
| Dejanje | `SetVariable` | `variable`, `op` (`=`, `+=`, `-=`), `value` | spremeni spremenljivko |
| | `GiveItem` | `item`, `count` (lahko negativen) | spremeni inventar (ne pod 0) |
| | `PlaySound` | `sound` (ID sredstva) | predvaja zvok, pesem ali WAV |
| | `Show` | `object`, `visible` (`yes`/`no`) | pokaže ali skrije objekt |
| | `SetState` | `object`, `state` | nastavi stanje lika ali oznako sprajta, sproži `StateChanged` |
| | `PlayClip` | `object`, `library` (ID `.pxanim`), `clip` | objekt začne predvajati posnetek |
| | `Wait` | `ms` (≥ 0) | nadaljuje po `ms` |
| | `LoadScene` | `scene` (ID prizora) | zahteva menjavo prizora in ustavi to verigo |

## Izvajanje
1. **Vrstni red v sličici:** najprej se nadaljujejo čakanja, katerih čas je potekel (po vrstnem redu, v katerem so bila zahtevana), nato dogodki v tem vrstnem redu: `Start`, `Timer`, `EnterArea`, `Touch`, `StateChanged`. Dogodki iste vrste gredo po vrstnem redu vozlišč v grafu.
2. **Ena izvedba na dogodek na sličico:** vozlišče dogodka se v isti sličici sproži največ enkrat, tudi če igralec isto sličico vstopi v dve območji z enakim imenom. `StateChanged`, ki ga sproži dejanje v tej sličici, se izvede v isti sličici, a vsako vozlišče največ enkrat.
3. **Veriga:** iz vozlišča se izvajajo povezave njegovih vrat v vrstnem redu povezav, vsaka do konca (najprej v globino). Pogoj izbere `true` ali `false`. Dejanje učinkuje takoj.
4. **Čakanje:** `Wait` ustavi vejo, njeni nasledniki se izvedejo v prvi sličici, ko čas doseže `zdaj + ms`. `Wait 0` se nadaljuje v naslednji sličici.
5. **Menjava prizora:** po `LoadScene` se v tej sličici ne izvede nič več. Predogled igre naloži zahtevani prizor.
6. **Meja:** največ 1000 izvedenih vozlišč na sličico. Ob preseku se sličica konča, v dnevnik gre opozorilo.
7. **Časovniki** štejejo od začetka prizora (ponastavitev v predogledu začne znova).
8. **Determinizem:** isti vhod (dogodki in čas) da vedno enake spremenljivke, inventar in učinke.

## Preverjanje grafa (pred shranjevanjem, izvozom in zagonom)
- neznana vrsta vozlišča, podvojen `Id`, manjkajoč ali neveljaven obvezen parameter;
- povezava iz vrat, ki jih vozlišče nima, v neobstoječe vozlišče ali v dogodek;
- **cikel brez `Wait`** (vsak krog mora vsebovati čakanje, sicer bi se vrtel v isti sličici);
- opozorilo: vozlišče, ki ni dosegljivo iz nobenega dogodka.

## Izvoz
- **Godot:** `ime_logic.gd` (`extends Node`), vozlišče `Logic` s to skripto v sceni stopnje. Vsako vozlišče je funkcija, `Wait` je `await get_tree().create_timer()`. `EnterArea` uporablja signal `body_entered` območja, `Touch` primerja razdaljo do igralca v `_physics_process` (polmer 12 px), `Timer` je vozlišče `Timer`. Igralec je prvi objekt z likom vrste Player. Koda je ustvarjena za igro; PixelForge je ne izvaja.
- **Generično:** `ime_logic.json` (enaka shema kot `.pxlogic`).
