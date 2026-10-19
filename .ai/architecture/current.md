# Trenutna arhitektura (stanje 19. 10. 2026, commit 9144477 + Faza 0)

Diagram vsebuje samo dejanske tipe iz `src/`. Puščica pomeni »uporablja«.

```mermaid
graph LR
  subgraph App[PixelForge.App - WPF]
    MainWindow -->|IEditor| SpriteEditor
    MainWindow -->|IEditor| TilemapEditor
    MainWindow -->|IEditor| MusicEditor
    MainWindow -->|IEditor| SfxEditor
    MainWindow --> HelpWindow
    MainWindow --> PreferencesWindow
    MainWindow --> AudioOut
    SpriteEditor --> ColorPicker
    SpriteEditor --> PreviewWindow
    MusicEditor --> StepGrid
    MusicEditor --> AudioOut
    SfxEditor --> AudioOut
    Ui[Ui helpers]
  end
  subgraph Core[PixelForge.Core]
    Sprite --- Layer & Frame & FrameTag
    Compositor --> Sprite
    Tools
    PixelOps --> Sprite
    Selection --> Tools
    ProjectFile --> Sprite
    SpriteSheet --> Compositor
    GifWriter
    PaletteIO
    ColorUtil
    ImageIO
    Tilemap --- Tileset & MapLayer & MapObject
    Autotile --> Tilemap
    TiledExport --> Tilemap
    Song --- Pattern & Instrument
    SongStream --> Song
    Synth --> SongStream
    WavWriter
    Sfx
    Settings
    History
  end
  SpriteEditor --> Sprite & Compositor & Tools & PixelOps & Selection & ProjectFile & SpriteSheet & GifWriter & PaletteIO & ImageIO & History
  TilemapEditor --> Tilemap & Autotile & TiledExport & Tools & ImageIO & History
  MusicEditor --> Song & SongStream & Synth & WavWriter & History
  SfxEditor --> Sfx & WavWriter
  MainWindow --> Settings
  PreferencesWindow --> Settings
  ColorPicker --> ColorUtil
  AudioOut --> SongStream
```

## Ključne lastnosti
- **En dokument na zavihek.** Projekta in referenc med datotekami ni; tileset je v `.pxm` kopiran.
- **Undo:** `History<T>` hrani cele kopije dokumenta (meja `Settings.UndoLimit`).
- **Zvok:** en `AudioOut` (NAudio `WaveOutEvent`) meša pesem (`SongStream`) in kratke zvoke.
- **Nastavitve:** statični `App.Settings`, sprememba sproži `App.SettingsChanged`.
- **Samodejno shranjevanje:** `MainWindow` vsakih N minut piše `%AppData%\PixelForge\autosave\<indeks><končnica>`. Ob zagonu ponudi obnovitev.
- **Bližnjice:** `MainWindow.OnPreviewKeyDown` → `IEditor.HandleKey`, razen kadar tipko potrebuje kontrolnik (`ControlOwnsKey`).

## Znan tehnični dolg
Glej poročilo Faze 0 v `../sessions/SESSION-2026-10-19-phase0.md`.
