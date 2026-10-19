# PixelForge: navodila za delo v projektu

Namizno orodje za izdelavo vsebin za 2D igre: Sprite, Character, Tilemap, Scene, Audio, Music, SFX, s projektom in brskalnikom sredstev.
Ob začetku dela preberi še `AGENT.md`, zadnji zapis v `.ai/sessions/` in `.ai/architecture/`.

## Sklad
- .NET 10, C#, WPF (`net10.0-windows`), tema Fluent (`Application.ThemeMode`, opozorilo WPF0001 je namerno utišano).
- Edina zunanja odvisnost: `NAudio.WinMM` 2.2.1 (samo `PixelForge.App`). Nove odvisnosti samo z utemeljitvijo v ADR.
- Testi: xUnit (`tests/PixelForge.Core.Tests`).

## Struktura
```
src/PixelForge.Core   domena: modeli, algoritmi, formati, izvoz. Brez UI (WPF samo za kodiranje slik).
src/PixelForge.App    WPF: urejevalniki (UserControl + code-behind, delne datoteke *.Drawing.cs, *.Menus.cs …)
tests/…Core.Tests     enotski in regresijski testi; Fixtures/ = zlati primerki formatov
tools/smoke.ps1       UI smoke test (UI Automation)
.ai/                  arhitektura, ADR, seje, vloge agentov
```

## Konvencije
- Logika gre v Core in dobi test; App le povezuje UI z domeno.
- Urejevalnik implementira `IEditor` (`src/PixelForge.App/IEditor.cs`); ukazi v menijih prek `Ui.Item`/`Ui.Toggle`.
- Vsaka sprememba dokumenta: najprej `Snapshot()` (undo), nato sprememba.
- Imena v kodi so angleška; besedila za uporabnika v aplikaciji so angleška, dokumentacija v `.ai/` in tu je slovenska.
- Kratki komentarji pojasnjujejo *zakaj*; zavestne poenostavitve označi s `// ponytail:` in mejo.
- Brez MVVM ogrodja, brez vsebnika DI, brez storitev: aplikacija ostane en proces.

## Ukazi
```bash
dotnet build
dotnet test
powershell -File tools/smoke.ps1
dotnet run --project src/PixelForge.App
```
Zlate primerke v2 zapiše samo `PF_WRITE_FIXTURES=1 PF_FIXTURES_DIR=<pot do Fixtures> dotnet test --filter WriteV2Fixtures`, in to le ob namerni spremembi formata.

## Združljivost formatov (obvezno)
- Formati: `.pfproj`, `.pxf`, `.pxm`, `.pxs`, `.sfx`, `.pxchar`, `.pxscene`, `.pxmix` (opis v `.ai/context/formats.md`). Vsaka datoteka ima `Version`.
- Nova polja so neobvezna s privzeto vrednostjo; stare različice se morajo naložiti.
- Sprememba sheme: nova različica, migracija, zlati primerek v `Fixtures/` in test v `FormatRegressionTests`.
- Obstoječih zlatih primerkov ne prepisuj.
- Izvoz Aseprite JSON in Tiled TMJ je javna pogodba: spremembe samo dodajajo polja.

## Varnost
- Ne beri in ne piši zunaj repozitorija, razen v `%AppData%\PixelForge` (to počne aplikacija sama).
- Brez poverilnic, brez omrežnih klicev iz aplikacije.
- Vtičniki so samo podatki (JSON); tuje kode ne nalagamo in ne izvajamo (ADR-0002).
- Posnetke zaslona delaj samo z oknom aplikacije (`PrintWindow`), nikoli celega zaslona.

## Spreminjanje arhitekture
- Pred večjo spremembo napiši ADR v `.ai/architecture/adr/` in posodobi `target.md`.
- Ne prepisuj stabilnih modulov brez dokazane potrebe; raje razširi.
- CodeGraph v projektu ni indeksiran; indeksiranje (`codegraph init`) je odločitev lastnika.
