using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PixelForge.App;

/// <summary>Help › Getting started: short lessons, each with a button that opens the matching part of the sample game.</summary>
static class GettingStarted
{
    sealed record Lesson(string Title, string Example, string[] Steps);

    static readonly Lesson[] Lessons =
    [
        new("1. Draw a sprite", "art/hero.pxf",
        [
            "The Sprite tab in Pixel mode is for pixel art. Pick a colour in the palette on the left, draw with B (pencil), erase with E, fill with G.",
            "A sprite has frames (see the frame list; ← → step through them). Animations are tags: a name and a range of frames, e.g. run = frames 3–6.",
            "Turn on the onion skin to see the previous frame faintly while you draw the next one. Space plays the selected tag.",
            "The example hero has idle (with a blink), run and jump. Change a colour or a frame and watch the animation.",
        ]),
        new("2. Make a character", "characters/hero.pxchar",
        [
            "A character (Character tab) is a sprite plus game data: its kind (Player, Enemy …), states (idle, run, jump) and the frames each state uses.",
            "The origin is the point that stands on the ground (usually the feet).",
            "Boxes: Hurt (where it can be hit), Hit (where it hits), Solid (what collides with walls). Drag on the preview to draw one.",
            "Properties such as speed and jump are kept with the character and exported with it.",
        ]),
        new("3. Paint a tilemap", "levels/meadow.pxm",
        [
            "A tilemap is a grid of tiles from a tileset picture (here art/tiles.pxf: grass, dirt, stone, brick, ladder, water, plank, spikes).",
            "Pick a tile in the tileset panel and paint with B, erase with E, fill with G. Hold Shift to pick several tiles.",
            "Tiles can collide: select tiles in the tileset, then Tileset > Make selected tiles solid (ground, walls) or one-way platforms (planks you jump through from below).",
            "Auto-tile (A) places the right edge and corner tiles for you once a terrain is set up.",
        ]),
        /* simplified app: lessons for the switched-off parts
        new("4. Build a level", "levels/meadow.pxscene",
        [
            "A scene (Scene tab, Layout mode) stacks layers: tilemaps, pictures and objects. A layer with parallax below 1 moves slower (the clouds).",
            "I places the selected sprite or character, A draws areas: Spawn (start), Checkpoint, Kill (water, spikes), Trigger (the exit).",
            "Press F5 for the game preview: arrows walk, Space jumps, H shows hitboxes, R restarts.",
            "Logic mode (Ctrl+3) shows the rules: touching a coin adds 1 to coins and hides it; the exit needs five coins.",
        ]),
        new("5. Make a beat", "music/sunny.pxs",
        [
            "The Music tab has four channels: 1 and 2 are square waves (melody, chords), 3 is a triangle (bass), 4 is the drum kit.",
            "Press 4: every row of the grid is a drum (kick, snare, hi-hat, open hat, toms, clap, crash). Click a step to add a hit, click it again to remove it.",
            "Pattern > Drum beat writes a whole beat (rock, hip-hop, four on the floor …) into the current pattern. Press Space to hear it.",
            "The bars under the grid are volumes: lower the hi-hats, keep kick and snare loud. A pattern is 16 steps = one bar.",
        ]),
        new("6. Write a song", "music/sunny.pxs",
        [
            "Channel 3 (triangle) plays the bass: long notes on the root of each chord. Drag right to make a note longer.",
            "Channels 1 and 2 play the melody and the chords. Duty changes the tone of a square wave (12.5 % is thin, 50 % is round).",
            "Make several patterns (Pattern > New or Duplicate) and write the play order, e.g. 1 1 2 3.",
            "Pattern > Generate song… writes a whole song from a seed to start from. File > Export WAV saves it for any game engine.",
        ]),
        new("7. Sounds, light and more", "levels/cave.pxscene",
        [
            "The SFX tab makes sound effects: click a preset (Coin, Jump, Hit …), Mutate for variations.",
            "The cave level is dark (ambient light) with torches, fire particles and a glowing chest: see Lighting and effects in Layout mode.",
            "The Audio tab mixes songs and sounds on a timeline (music/soundtrack.pxmix).",
            "Project > Export writes everything for Godot 4 or as PNG, JSON and WAV files.",
        ]),
        */
    ];

    /// <param name="openExample">Opens a file of the sample game by its path inside the sample (creating the sample first if needed).</param>
    public static void Show(Window owner, Action<string> openExample)
    {
        var list = new ListBox { Width = 210, ItemsSource = Lessons.Select(l => l.Title).ToList() };
        AutomationProperties.SetName(list, "Lessons");
        var text = new FlowDocumentScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(text, "Lesson");
        var open = new Button { Content = "Open example", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 8, 0) };
        var next = new Button { Content = "Next lesson", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 8, 0) };
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 8, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { open, next, close } };
        DockPanel.SetDock(list, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var win = new Window
        {
            Title = "Getting started", Width = 760, Height = 480, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(12), Children = { list, buttons, new Border { Margin = new Thickness(12, 0, 0, 0), Child = text } } },
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var l = Lessons[list.SelectedIndex];
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = win.FontFamily, FontSize = 14 };
            doc.Blocks.Add(new Paragraph(new Run(l.Title)) { FontSize = 20, FontWeight = FontWeights.SemiBold });
            var steps = new List { MarkerStyle = TextMarkerStyle.Decimal };
            foreach (var s in l.Steps) steps.ListItems.Add(new ListItem(new Paragraph(new Run(s)) { Margin = new Thickness(0, 0, 0, 8) }));
            doc.Blocks.Add(steps);
            doc.Blocks.Add(new Paragraph(new Run($"Example: {l.Example} in the sample game.")) { Foreground = System.Windows.Media.Brushes.Gray });
            text.Document = doc;
            next.IsEnabled = list.SelectedIndex < Lessons.Length - 1;
        };
        open.Click += (_, _) => { if (list.SelectedIndex >= 0) openExample(Lessons[list.SelectedIndex].Example); };
        next.Click += (_, _) => list.SelectedIndex = Math.Min(Lessons.Length - 1, list.SelectedIndex + 1);
        list.SelectedIndex = 0;
        win.Show();
    }
}
