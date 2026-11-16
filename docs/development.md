# Kako Pixelkiln deluje in kako ga nadgradiš

Dokument je za nekoga, ki želi program sam spremeniti: dodati ukaz, orodje ali polje v datoteki, zamenjati barve, posodobiti knjižnice ali izdati novo različico. Najprej opiše, kako je program sestavljen in kaj se v njem dogaja, nato sledijo navodila za najpogostejše spremembe.

Kaj program zna in kako ga uporabljaš, je v [README.sl.md](README.sl.md).

## Na kratko
- Namizni program za Windows: C#, .NET 10, WPF. En proces, brez omrežja, brez baze.
- Dva projekta in testi. `Pixelkiln.Core` je vsa logika (modeli, risanje v pomnilniku, datoteke, izvoz), `Pixelkiln.App` so okna in gumbi.
- Brez ogrodij MVVM in vsebnikov DI. Vsako okno ima svojo kodo (`code-behind`), ki kliče Core.
- Edina zunanja knjižnica programa je NAudio.WinMM (predvajanje zvoka v izklopljenih zvočnih urejevalnikih).

## Kaj potrebuješ
- Windows 10 ali 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (ali novejši, ki zna graditi za `net10.0-windows`).
- Urejevalnik kode: Visual Studio, Rider ali VS Code z razširitvijo za C#. Odpreš `Pixelkiln.slnx`.

```bash
dotnet run --project src/Pixelkiln.App
```

```bash
dotnet test
```

## Zgradba map

```
src/Pixelkiln.Core     logika brez oken; vse, kar se da preveriti s testom
src/Pixelkiln.App      okna, urejevalniki, teme, ikone
tests/Pixelkiln.Core.Tests   testi za Core; Fixtures/ so shranjeni primerki vseh različic datotek
tools/smoke.ps1        preizkus vmesnika (klika po programu z UI Automation)
tools/publish.ps1      naredi paket za izdajo (zip z exe)
tools/make-icon.ps1    nariše ikono programa (app.ico)
samples/plugins        vzorčni vtičnik
docs/                  dokumentacija
Directory.Build.props  številka različice za vse projekte
```

### Core: kje je kaj

| Področje | Datoteke | Vsebina |
|---|---|---|
| Sprajt | `Sprite.cs`, `ProjectFile.cs` | model (plasti, sličice, oznake animacij) in datoteka `.pxf` |
| Risanje | `Tools.cs`, `PixelOps.cs`, `Compositor.cs`, `Selection.cs` | črte, liki, polnilo, zrcaljenje, vrtenje, združevanje plasti, čarobna palica |
| Barve | `ColorUtil.cs`, `PaletteTools.cs`, `PaletteIO.cs` | pretvorbe barv, barvne lestvice, branje palet |
| Slike | `ImageIO.cs`, `SpriteSheet.cs`, `GifWriter.cs`, `Atlas.cs` | PNG, sprite sheet, GIF, atlas |
| Mapa ploščic | `Tilemap.cs`, `TileStamp.cs`, `Autotile.cs`, `TilesetTools.cs` | model mape, datoteka `.pxm`, čopič, samodejni tereni |
| Lik | `Character.cs`, `CharacterExport.cs` | stanja, škatle, lastnosti, datoteka `.pxchar` |
| Projekt | `Project.cs`, `AssetKinds.cs`, `AssetLoader.cs`, `VersionStore.cs` | seznam sredstev z ID-ji, vrste sredstev, zgodovina različic |
| Izvoz | `Export.cs`, `GodotExport.cs`, `TiledExport.cs`, `TerrainExport.cs` | cevovod izvoza, Godot 4, Tiled |
| Skupno | `DocumentFile.cs`, `History.cs`, `Settings.cs` | varno branje in pisanje datotek, razveljavitev, nastavitve |
| Predloge in vtičniki | `Templates.cs`, `TemplateEngine.cs`, `Plugins.cs`, `Templates/*.template.json` | samo podatki, brez kode |
| Vzorčna igra | `SampleProject.cs`, `SampleArt.cs` | projekt, ki ga odpre Help › Open sample game |

Ostale datoteke (`Scene.cs`, `Song.cs`, `Synth.cs`, `Sfx.cs`, `Audio.cs`, `Paint.cs`, `Vector.cs`, `Texture.cs`, `UiKit.cs`, `Motion.cs`, `Logic.cs` in sorodne) pripadajo urejevalnikom, ki so v tej različici izklopljeni. Prevajajo se in imajo teste.

### App: kje je kaj

| Datoteke | Vsebina |
|---|---|
| `App.xaml`, `App.xaml.cs` | skupni slogi gumbov in plošč; zagon, nastavitve, odprti projekt, dogodki med deli programa, `crash.log` |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | glavno okno, seznam urejevalnikov, meni, samodejno shranjevanje |
| `MainWindow.Shell.cs` | zgornja vrstica, zavihki, povezave med zavihki, gumb Export |
| `MainWindow.Commands.cs` | paleta ukazov (Ctrl+Shift+P) |
| `IEditor.cs`, `ModeHost.cs` | kaj mora znati vsak urejevalnik; načini znotraj zavihka |
| `SpriteEditor.*` | zavihek Sprite: `.xaml` postavitev, `.xaml.cs` dokument in shranjevanje, `Drawing` miška in orodja, `Menus` meniji, `Tags` oznake animacij, `View` pogled, `Advanced` dodatna orodja |
| `TilemapEditor.*` | zavihek Tilemap: `Painting` čopič in tileset, `Objects` objekti, `Menus` meniji |
| `CharacterEditor.*` | zavihek Character |
| `HomeView.*`, `AssetBrowser.*` | začetni zaslon, brskalnik sredstev |
| `Theme.cs`, `PathIcon.cs`, `Ui.cs` | barve in pisave tem, ikone, pomožne metode za menije, pogovorna okna in sezname |
| `ExportWindow.cs`, `HistoryWindow.cs`, `TemplateStudio.cs`, `PreferencesWindow.cs` in druga okna | posamezna okna, zgrajena v kodi |

## Kako program teče

### Zagon
1. `App.OnStartup` naloži nastavitve (`%AppData%\Pixelkiln\settings.json`), vtičnike in temo.
2. `MainWindow` ustvari urejevalnike, jih razporedi v zavihke (`HostModes`), zgradi zgornjo vrstico (`BuildShell`) in meni.
3. Ko je okno naloženo, ponudi obnovitev neshranjenega dela (`OfferRecovery`) in odpre zadnji projekt.

Mapo nastavitev prestaviš s spremenljivko okolja `PIXELKILN_HOME`. Z `PIXELKILN_TIMING=1` program zapiše čase zagona v `startup.log`.

### Zavihki in urejevalniki
Vsak zavihek je en urejevalnik, ki izpolnjuje vmesnik `IEditor`: zna odpreti datoteko, povedati, ali ima neshranjene spremembe, dati svoje menije, zapisati in obnoviti samodejno shranjeno kopijo. Glavno okno o urejevalnikih ne ve nič drugega.

Kateri zavihki so vidni, določa seznam `Shown` v `MainWindow.Shell.cs`. Meni v zgornji vrstici je vedno meni trenutnega urejevalnika, ki mu okno doda Options in Help.

### Dokument, sprememba, razveljavitev
Urejevalnik drži en dokument iz Core (`Sprite`, `Tilemap` ali `Character`). Vsaka sprememba poteka v istem zaporedju:

1. `Snapshot()` shrani kopijo dokumenta v zgodovino (`History<T>`) in označi dokument kot spremenjen.
2. Koda spremeni dokument.
3. `Render()` ali ustrezen `Refresh…()` pokaže novo stanje.

Razveljavitev zamenja dokument s kopijo iz zgodovine. Zgodovina ima omejitev števila korakov in porabe pomnilnika, zato pri velikih slikah hrani manj korakov.

### Datoteke
Vse lastne datoteke so JSON s poljem `Version`. Bere in piše jih `DocumentFile`:

- pisanje gre najprej v `ime.tmp` in šele nato zamenja staro datoteko, zato ob napaki stara ostane cela;
- ko program prvič shrani datoteko starejše različice, pusti ob njej kopijo `ime.v<stara>.bak`;
- vsaka napaka pri branju postane ena `InvalidDataException` z razumljivim sporočilom, ki ga urejevalnik pokaže.

Opis vseh formatov in različic je v [formats.md](formats.md).

### Projekt in povezave med sredstvi
Projekt je mapa z datoteko `.pfproj`, ki našteva sredstva in vsakemu da stalen ID. Lik kaže na sprajt po ID-ju, mapa na nabor ploščic prav tako, zato preimenovanje in premikanje datotek povezav ne pokvari ([ADR-0001](adr/ADR-0001-project-and-asset-ids.md)). Kaj program ve o posamezni vrsti datoteke (končnice, opis, sličica, na kaj kaže), je na enem mestu: `AssetKinds.cs`.

Deli programa se obveščajo z dogodki v `App`: `ProjectChanged` (projekt odprt, zaprt ali spremenjen), `AssetSaved` (shranjena datoteka; mapa in lik po njem znova naložita sprajt), `SettingsChanged`.

### Samodejno shranjevanje in napake
- Glavno okno vsakih nekaj minut (Preferences) pokliče `WriteAutosave` vsakega urejevalnika z neshranjenimi spremembami. Kopije so v `%AppData%\Pixelkiln\autosave` in se ob običajnem zaprtju izbrišejo.
- Pričakovane napake (datoteke ni, napačen vnos) urejevalnik ujame sam in pokaže z `Ui.Error`.
- Nepričakovano napako ujame `App`: zapiše jo v `%AppData%\Pixelkiln\crash.log`, pokaže sporočilo in pusti program odprt, da lahko delo shraniš.

### Izvoz
`Exporter` (v `Export.cs`) najprej preveri projekt, nato vse izhodne datoteke zgradi v pomnilniku in šele na koncu v enem koraku zamenja izhodno mapo. Isti vhod da iste bajte. Oblike za Godot piše `GodotExport.cs`; navodila za uvoz so v [godot.md](godot.md).

### Videz
Barve so žetoni (`Bg0Brush`, `TextBrush`, `AccentBrush` …), ki jih za vsako temo določa tabela `Tokens` v `Theme.cs`. Skupni slogi (`IconButton`, `PrimaryButton`, `RailTool`, `LayerRow` …) so v `App.xaml`. Ozadje zasnove je v [design.md](design.md).

## Pravila, ki držijo program skupaj
1. **Logika gre v Core in dobi test.** App samo poveže gumb z metodo iz Core.
2. **Pred vsako spremembo dokumenta `Snapshot()`.** Brez njega spremembe ni mogoče razveljaviti.
3. **Stare datoteke se morajo vedno odpreti.** Novo polje je neobvezno in ima privzeto vrednost. Shranjenih primerkov v `Fixtures/` ne prepisuješ.
4. **Vtičniki in predloge so samo podatki.** Program tuje kode ne nalaga in ne izvaja ([ADR-0002](adr/ADR-0002-declarative-plugins.md)).
5. **Brez omrežja.** Program ne kliče ničesar na internetu.
6. **Imena za preizkus vmesnika ostanejo.** `tools/smoke.ps1` najde gumbe po `AutomationProperties.Name`, menije po naslovih in okna po naslovnih vrsticah. Če jih preimenuješ, popravi tudi skripto.
7. **Opozorila prevajalnika so napake.** Gradnja na GitHubu teče z `-warnaserror`.
8. Besedila v programu in imena v kodi so angleška, dokumentacija v `docs/` je slovenska.

## Pogoste spremembe

### Nov ukaz v meniju
1. Odpri datoteko `…Editor.Menus.cs` (pri liku `CharacterEditor.xaml.cs`) in v lastnosti `Menus` dodaj vrstico:

   ```csharp
   Ui.Item("_Invert colors", "Ctrl+I", InvertColors),
   ```

   Podčrtaj določa črko za tipkovnico v meniju, drugi argument je izpis bližnjice.
2. Napiši metodo. Najprej `Snapshot()`, nato sprememba, nato `Render()`.
3. Bližnjico dodaj v `HandleKey` istega urejevalnika (izpis v meniju je ne sproži sam).
4. Ukaz se v paleti ukazov pojavi sam, ker jo polni iz menijev.

### Novo orodje za risanje v zavihku Sprite
1. Algoritem napiši v Core (`Tools.cs`) kot metodo, ki dela nad poljem pik, in zanj dodaj test v `DrawingTests.cs`.
2. V `SpriteEditor.Drawing.cs` dodaj ime v `enum Tool`.
3. V `SpriteEditor.xaml` dodaj v `ToolsPanel` gumb po vzoru obstoječih. `Tag` mora biti enak imenu iz `enum Tool`:

   ```xml
   <RadioButton Style="{StaticResource RailTool}" Tag="Spray" ToolTip="Spray (A)"
                AutomationProperties.Name="Spray (A)" GroupName="tool" Click="OnToolClick">
       <local:PathIcon Kind="pencil" />
   </RadioButton>
   ```
4. Vedenje miške dodaj v `OnCanvasDown`, `OnCanvasMove` in `OnCanvasUp` (liki, ki se rišejo od točke do točke, gredo v `DrawShape`).
5. Tipko dodaj v `HandleKey` (`case Key.A when !ctrl: SetTool(Tool.Spray); break;`) in v seznam bližnjic v `HelpWindow.cs`.

### Nova ikona
Ikone so poti v okviru 24×24, zapisane v slovarju `Icons.Paths` v `PathIcon.cs`. Dodaj vnos z imenom in potjo, nato jo uporabiš z `<local:PathIcon Kind="ime" />`. Ikona prevzame barvo besedila, zato deluje v vseh temah.

### Novo polje v datoteki
Primer: mapi dodaš barvo ozadja.

1. Modelu v Core dodaj lastnost s privzeto vrednostjo.
2. V zapisu za datoteko (pri mapi `MapDto` v `Tilemap.cs`) dodaj **neobvezno** polje na konec in ga upoštevaj v `Serialize` in `Deserialize`. Stara datoteka polja nima in dobi privzeto vrednost.
3. Povečaj `Version` in v komentar ob njem zapiši, kaj je nova različica dodala.
4. Shrani primerek nove različice v `tests/Pixelkiln.Core.Tests/Fixtures/current/` (na primer `map.v7.pxm`) in dodaj test v `FormatRegressionTests.cs`, ki ga naloži in preveri novo polje. Starih primerkov ne spreminjaš; testi zanje dokazujejo, da se stare datoteke še odprejo.
5. Opiši spremembo v [formats.md](formats.md).
6. Če polje vpliva na izvoz, dopolni `Export.cs`, `GodotExport.cs` ali `TiledExport.cs`. Izvoza Tiled in Aseprite sta javni pogodbi: polja samo dodajaš, obstoječih ne spreminjaš.

`DamagedFileTests.cs` novo polje samodejno preizkusi z napačnimi vrednostmi. Če test pade, datoteka z napačno vrednostjo ni dobila razumljivega sporočila; dodaj preverjanje v `Deserialize`.

### Barve in teme
- Barvo zamenjaš v tabeli `Tokens` v `Theme.cs`: en stolpec za vsako temo (Modern, Dark, Light).
- V XAML vedno uporabi žeton (`{DynamicResource TextBrush}`), nikoli zapisane barve, sicer se tema Light pokvari.
- Po spremembi preglej vse tri zavihke in začetni zaslon v temah Modern in Light (Options › Preferences).

### Vklop izklopljenega urejevalnika
- Cel zavihek (Scene, Audio, Music, SFX): dodaj ime v `Shown` v `MainWindow.Shell.cs`.
- Način znotraj zavihka (Paint, Vector, Texture, UI, Rig, Motion, Logic): odstrani `//` pred ustrezno vrstico v `HostModes` v `MainWindow.xaml.cs`.
- Okna Optimize assets, Style guide in Device preview: odstrani `//` pred njihovimi vrsticami v menijih v `MainWindow.xaml.cs`.

Ti deli od poenostavitve niso bili več preizkušeni z roko. Po vklopu dopolni `tools/smoke.ps1` (koraki zanje so v `if ($false)`) in jih preglej.

### Nova predloga ali vtičnik
Kode ni treba spreminjati. Predloga je datoteka `template.json` ([templates.md](templates.md)), vtičnik `plugin.json` s paletami, prednastavitvami in makri ([plugins.md](plugins.md)). Vgrajene predloge so v `src/Pixelkiln.Core/Templates/`.

### Nova vrsta izvoza
1. Zapis izhodne datoteke napiši v Core kot metodo, ki vrne besedilo ali bajte, in dodaj test v `ExportTests.cs`.
2. Za izvoz ene datoteke dodaj ukaz v meni File urejevalnika. Če se naslov začne z `Export`, ga gumb Export v zgornji vrstici pokaže tudi brez projekta.
3. Za izvoz celega projekta dodaj datoteko v metodi `Export` razreda `Context` v `Export.cs`, v veji za ustrezno vrsto sredstva.

### Posodobitev knjižnic

```bash
dotnet list package --outdated
```

Različice so zapisane v `src/Pixelkiln.App/Pixelkiln.App.csproj` (NAudio.WinMM) in `tests/Pixelkiln.Core.Tests/Pixelkiln.Core.Tests.csproj` (xunit, Microsoft.NET.Test.Sdk, coverlet). Spremeni številko, nato poženi gradnjo in teste.

- Testne knjižnice: dovolj je, da testi uspejo in jih je enako število kot prej.
- NAudio uporablja samo `AudioOut.cs`. Ob večji različici se lahko spremeni vmesnik (prehod z 2 na 3 je zahteval `WaveOut` namesto `WaveOutEvent` in `Read(Span<float>)`). Zvok preizkusiš tako, da začasno vklopiš zavihek SFX.
- Nove knjižnice dodaj le z dobrim razlogom in ga zapiši kot ADR v `docs/adr/`. Preveri licenco in jo dopiši v README.
- Prehod na novejši .NET: spremeni `TargetFramework` v vseh treh datotekah `.csproj` ter `dotnet-version` v `.github/workflows/build.yml` in `release.yml`.

### Nova različica in izdaja
1. Povečaj `<Version>` v `Directory.Build.props`. Številko pokažeta začetni zaslon in Help › About.
2. Dopiši spremembe v [CHANGELOG.md](../CHANGELOG.md).
3. Poženi preverjanje iz naslednjega razdelka.
4. Naredi paket in ga preizkusi:

   ```bash
   powershell -File tools/publish.ps1
   ```

   V mapi `artifacts/` nastaneta mapa `Pixelkiln-<različica>-win-x64` z `Pixelkiln.App.exe` in zip iste vsebine. Paket vsebuje .NET, zato na ciljnem računalniku ni treba ničesar nameščati.
5. Za objavo na GitHubu potisni oznako, enako različici:

   ```bash
   git tag v0.2.0
   ```

   ```bash
   git push origin v0.2.0
   ```

   Delovni tok `release.yml` zgradi paket in odpre **osnutek** izdaje s pripetim zipom. Javna postane, ko jo na GitHubu potrdiš.

## Preverjanje pred objavo

```bash
dotnet build -c Debug -warnaserror
```

```bash
dotnet build -c Release -warnaserror
```

```bash
dotnet test
```

```bash
powershell -File tools/smoke.ps1
```

`smoke.ps1` vrne 0, če je vse v redu, 1 ob napaki in 2, če okno ni dobilo ospredja in nekateri koraki z meniji niso tekli (računalnik pusti pri miru in poženi znova).

Testi, ki rišejo z WPF (`VectorTests`, `UiTests`, del `MotionTests`), padejo, kadar je namizje zaklenjeno ali je oddaljena seja odklopljena, ker WPF takrat ne riše. To ni napaka v kodi; poženi jih ob odklenjenem zaslonu.

Ob vsakem `git push` na vejo `master` GitHub sam zgradi obe konfiguraciji in požene teste (`.github/workflows/build.yml`).

## Kje je še kaj zapisano
- [README.sl.md](README.sl.md): kaj program zna, prenos, zagon.
- [formats.md](formats.md): vse datoteke in njihove različice.
- [godot.md](godot.md): izvoz v Godot 4 in seznam za prvi uvoz.
- [design.md](design.md): barve, pisave in postavitev teme Modern.
- [plugins.md](plugins.md), [templates.md](templates.md): vtičniki in predloge.
- [logic.md](logic.md): vizualna logika (izklopljen del).
- [adr/](adr): arhitekturne odločitve in razlogi zanje.
- [CHANGELOG.md](../CHANGELOG.md): spremembe po različicah.
