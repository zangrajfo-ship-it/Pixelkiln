# Predloge (Template Studio)

Predloga ustvari eno ali več sredstev v odprtem projektu: like, stopnje, predmete ali glasbo. Odpreš jo s **Project → New from template…** ali s **Ctrl+T**.

## Uporaba
1. Izberi predlogo. Iščeš lahko po imenu, opisu in oznakah ter filtriraš po vrsti in viru.
2. Nastavi parametre: ime, velikost, seme, paleto ali tileset iz projekta, težavnost …
3. Izberi **Starting point** (glavno sredstvo in vse, kar nujno potrebuje) ali **Full kit** (vse iz predloge, npr. stopnja z junakom, kovanci in glasbo).
4. **Create** ustvari sredstva v izbrani mapi projekta in odpre glavno sredstvo v pravem urejevalniku.

Enaki parametri dajo enak rezultat; drugo seme da drugo različico.

## Viri
| Vir | Kje | Uporaba |
|---|---|---|
| Built-in | v aplikaciji | 22 predlog: liki (junak za platformer in pogled od zgoraj, sovražnik, NPC, trgovec, boss), stopnje (gozd, jama, grad, mesto, puščava, led, ječa, uganka), predmeti (vrata, lestev, stikalo, premična ploščad, kompleti predmetov in ikon), glasbena zanka |
| Project | `<projekt>/templates/<id>@<različica>/` | samo ta projekt |
| User | `%AppData%\PixelForge\templates\` (ali `PIXELFORGE_HOME`) | vsi tvoji projekti |

## Lastne predloge
V brskalniku sredstev izberi sredstva (Ctrl ali Shift + klik) in v Template Studiu klikni **Save selected assets as template…**. Datoteke se kopirajo v predlogo skupaj z vsem, kar uporabljajo (npr. sprajt lika). Ko predlogo uporabiš, se reference preusmerijo na nove kopije. Novo različico shraniš z višjo številko; stare različice ostanejo na voljo v izbirniku **Version**.

## Paketi
**Export pack…** zapiše izbrano predlogo v `.pxtplpack` (zip, ponovljiv). **Import pack…** paket najprej v celoti preveri: velikost, poti in vsako predlogo. Če kaj ni v redu, ne namesti ničesar. Izbrisana predloga gre v mapo `.trash` poleg nje.

## `template.json`
Predloga vsebuje samo podatke in ne izvaja kode. Koraki uporabljajo vgrajene generatorje:

| Korak (`type`) | Nastavitve (`settings`) |
|---|---|
| `sprite` | `width`, `height`, `seed`, `density`, `palette`; `states[]` (`name`, `frames`) postanejo oznake |
| `items` | `names` (z vejicami), `size`, `seed`, `palette` |
| `character` | `spriteStep`, `kind`, `facing`, `origin` (`feet`, `center`); `boxes[]` (`kind`, `rect` = `full`, `body`, `feet`, `head`, `front` ali štirje deleži `x y w h`, `state`, `category`); `properties{}` |
| `tileset` | `tileSize`, `pattern`, `baseColor`, `variants`, `seed`, `asset` (slika iz projekta namesto generiranja) |
| `tilemap` | `width`, `height`, `tilesetStep`, `layout` (`Platforms`, `Cave`, `Rooms`, `Flat`), `fill`, `seed`, `difficulty` |
| `level` | `mapStep`, `playerStep`, `collectibleStep`, `musicStep` (zadnji trije niso obvezni), `checkpoints`, `hazards`, `collectibles`, `seed`, `background`, `genre` (`platformer`, `top-down`, `puzzle`) |
| `song` | `seed`, `bpm`, `scale` (`major`, `minor`, `pentatonic`), `patterns` |
| `file` | `file` (kopija iz mape predloge), `originalId` (za preusmeritev referenc) |

Vrednosti lahko vsebujejo `{parameter}`. Neznan ključ, neznan korak, sklic na kasnejši korak ali neobstoječi parameter je napaka. Taka predloga se ne naloži, razlog pa je izpisan v oknu.
