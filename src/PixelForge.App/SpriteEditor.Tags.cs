using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using PixelForge.Core;

namespace PixelForge.App;

/// <summary>Animation tags: named frame ranges; the chosen one limits playback and GIF export.</summary>
public partial class SpriteEditor
{
    int? _activeTag;

    FrameTag? ActiveTag => _activeTag is int i && i < _sprite.Tags.Count ? _sprite.Tags[i] : null;

    /// <summary>Frames that play / export: the chosen tag, or all frames.</summary>
    (int From, int To) PlayRange => ActiveTag is { } t ? (t.From, t.To) : (0, _sprite.Frames.Count - 1);

    /// <summary>Frames selected in the timeline (Shift/Ctrl+click), as one range.</summary>
    (int From, int To) SelectedFrames()
    {
        var idx = FramesList.SelectedItems.Cast<object>().Select(FramesList.Items.IndexOf).Where(i => i >= 0).ToList();
        return idx.Count == 0 ? (_frame, _frame) : (idx.Min(), idx.Max());
    }

    void RefreshTags()
    {
        if (ActiveTag is null) _activeTag = null;
        TagsPanel.Children.Clear();
        for (int i = 0; i < _sprite.Tags.Count; i++)
        {
            var t = _sprite.Tags[i];
            int index = i;
            var chip = new ToggleButton
            {
                Content = $"{t.Name}  {t.From + 1}–{t.To + 1}", IsChecked = i == _activeTag, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Play and export only these frames. Click again to use all frames.",
            };
            AutomationProperties.SetName(chip, $"Tag {t.Name}, frames {t.From + 1} to {t.To + 1}");
            chip.Click += (_, _) =>
            {
                _activeTag = _activeTag == index ? null : index;
                RefreshTags();
                if (ActiveTag is { } a) { StopPlaying(); SelectFrame(a.From); }
            };
            TagsPanel.Children.Add(chip);
        }
    }

    void OnAddTag(object sender, RoutedEventArgs e)
    {
        var (from, to) = SelectedFrames();
        if (Ui.Ask(this, "New tag", $"Name for frames {from + 1}–{to + 1} (e.g. walk, idle, jump):", "walk") is not string name || name.Trim().Length == 0) return;
        Snapshot();
        _sprite.Tags.Add(new FrameTag { Name = name.Trim(), From = from, To = to });
        _activeTag = _sprite.Tags.Count - 1;
        RefreshTags();
    }

    void OnSetTagRange(object sender, RoutedEventArgs e)
    {
        if (ActiveTag is not { } t) { Ui.Error(new InvalidOperationException("Choose a tag first (click it in the Tags row).")); return; }
        Snapshot();
        (t.From, t.To) = SelectedFrames();
        RefreshTags();
    }

    void OnRenameTag(object sender, RoutedEventArgs e)
    {
        if (ActiveTag is not { } t) { Ui.Error(new InvalidOperationException("Choose a tag first (click it in the Tags row).")); return; }
        if (Ui.Ask(this, "Rename tag", "Name:", t.Name) is not string name || name.Trim().Length == 0 || name.Trim() == t.Name) return;
        Snapshot();
        t.Name = name.Trim();
        RefreshTags();
    }

    void OnDeleteTag(object sender, RoutedEventArgs e)
    {
        if (_activeTag is not int i || ActiveTag is null) { Ui.Error(new InvalidOperationException("Choose a tag first (click it in the Tags row).")); return; }
        Snapshot();
        _sprite.Tags.RemoveAt(i);
        _activeTag = null;
        RefreshTags();
    }
}
