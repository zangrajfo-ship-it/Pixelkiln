# Changelog

Changes that matter to someone using Pixelkiln, newest first. File formats keep opening across versions; a format change is listed here and described in [docs/formats.md](docs/formats.md).

## 0.1.0

The first version. An early release: the Godot export has tests but has not been tried in a real Godot project yet.

- **Sprite:** pixel art with layers, frames, animation tags, onion skin, palettes, selection and transforms. Exports PNG, sprite sheets, Aseprite JSON and GIF.
- **Character:** states mapped to animation tags, origin, hurt/hit/solid/trigger boxes, properties for the game.
- **Tilemap:** tile layers, flipped and rotated tiles, objects, auto-tile terrains, per-tile collisions, generators. Exports Tiled TMJ.
- **From sprite to map:** make a character from a sprite, use a sprite as a tileset, edit a tile in the Sprite tab; saving a sprite refreshes the map and the character that use it.
- **Projects:** stable asset ids, asset browser, version history, templates, project export for Godot 4 and a generic profile.
- **Safety:** autosave and recovery, atomic saves, damaged files are refused with a readable message, an unexpected error is logged to `crash.log` and the app stays open.
- **Formats:** `.pxf` 2, `.pxm` 6, `.pxchar` 2, `.pfproj` 1.
- **Package:** a self-contained zip for 64-bit Windows; nothing to install.
