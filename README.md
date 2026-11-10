# Pixelkiln

A desktop tool for making 2D game art in one place: draw pixel sprites, turn them into animated characters, and build tilemaps from them.

Runs on Windows (.NET 10, WPF). No network, no accounts; everything stays in your own folders.

![Sprite editor](docs/screenshots/sprite.png)

> Version 0.1.0, an early release. The Godot export has not been tried in a real Godot project yet, so check the import the first time you use it.

## What it does

| Tab | For |
|---|---|
| **Sprite** | Pixel art with layers, frames, animation tags, onion skin, palettes, selection and transforms. Exports PNG, sprite sheets, Aseprite JSON and GIF. |
| **Character** | A character on top of a sprite: states mapped to animation tags, origin, hurt/hit/solid/trigger boxes, properties for the game. |
| **Tilemap** | Tile layers, flipped and rotated tiles, objects, auto-tile terrains (16 and 47 tiles), per-tile collisions, tileset and layout generators. Exports Tiled TMJ. |

Around them: a start screen with size presets and recent files, projects with an asset browser, version history, a command palette (Ctrl+Shift+P), templates (Ctrl+T), autosave and recovery, keyboard access everywhere (F1 lists the shortcuts).

| Start screen | Character | Tilemap |
|---|---|---|
| ![Start screen](docs/screenshots/home.png) | ![Character editor](docs/screenshots/character.png) | ![Tilemap editor](docs/screenshots/tilemap.png) |

## Build and run

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Pixelkiln.App
```

For development:

```bash
dotnet build
dotnet test
powershell -File tools/smoke.ps1
```

`tools/smoke.ps1` starts the built app in a temporary settings folder and clicks through it with UI Automation (0 = OK, 1 = failure, 2 = the window could not get the foreground, so some steps did not run).

## First steps

**Help › Getting started** has three short lessons (sprite, character, tilemap). Each one opens the matching file of the **sample game** (Help › Open sample game), which is created in `Documents\Pixelkiln samples`.

## Projects

**Project › New project** or **Make a project from a folder** creates a `.pfproj` file next to your assets. Assets get stable ids, so renaming and moving files does not break references between them. Deleted assets go to `.trash`, saved versions to `.history`, exports to `export/`.

## Export

- **Godot 4** (4.3 or newer): `SpriteFrames`, `TileSet` with collisions, `TileMap`, character scenes. Copy the export folder into your Godot project (`res://assets/…`).
- **Generic:** PNG (also at several scales), Aseprite JSON, Tiled TMJ, character JSON, `project.json`, optionally one atlas.
- The export is deterministic, checks the project first and never leaves half-written files. The list of written files is in `export-report.txt`.

## More in the code

The repository also contains editors that are switched off in this version: scenes and levels with a game preview, music and sound effects, an audio mixer, digital painting, vector drawing, textures, UI kits, cutout rigs, animation clips and visual logic. They compile and have tests. To bring one back, add its name to `Shown` in `src/Pixelkiln.App/MainWindow.Shell.cs` or remove the comment in `HostModes` in `MainWindow.xaml.cs`.

## Documentation

The documents in `docs/` are written in Slovenian:

- [docs/README.sl.md](docs/README.sl.md): this page in Slovenian, with the switched-off parts described
- [docs/formats.md](docs/formats.md): every file format and its versions; older files always open (golden files in `tests/Pixelkiln.Core.Tests/Fixtures`)
- [docs/plugins.md](docs/plugins.md), [docs/templates.md](docs/templates.md): data-only plugins and templates (no code is loaded)
- [docs/design.md](docs/design.md): colours, fonts and layout of the Modern theme
- [docs/adr](docs/adr): architecture decisions

## Licence

[MIT](LICENSE). The bundled fonts Inter and JetBrains Mono are under the SIL Open Font License 1.1 (`src/Pixelkiln.App/Fonts`). The only external dependency is NAudio.WinMM (MIT), used for sound playback.
