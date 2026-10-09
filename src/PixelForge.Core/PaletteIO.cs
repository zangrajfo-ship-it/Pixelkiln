using System.Globalization;

namespace PixelForge.Core;

/// <summary>Palette files: .hex (one RRGGBB per line, Lospec) and .gpl (GIMP). Colors are opaque ARGB.</summary>
public static class PaletteIO
{
    public static List<uint> Load(string path) =>
        Parse(File.ReadAllText(path), Path.GetExtension(path).Equals(".gpl", StringComparison.OrdinalIgnoreCase));

    public static void Save(string path, IEnumerable<uint> colors) =>
        File.WriteAllText(path, Path.GetExtension(path).Equals(".gpl", StringComparison.OrdinalIgnoreCase)
            ? ToGpl(colors, Path.GetFileNameWithoutExtension(path))
            : ToHex(colors));

    public static List<uint> Parse(string text, bool gpl)
    {
        var colors = new List<uint>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (gpl)
            {
                if (line.StartsWith('#') || !char.IsDigit(line[0])) continue;   // header, Name:, Columns:, comments
                var p = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) continue;
                colors.Add(0xFF000000 | Byte(p[0]) << 16 | Byte(p[1]) << 8 | Byte(p[2]));
            }
            else
            {
                var hex = line.TrimStart('#');
                if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, null, out uint rgb))
                    throw new InvalidDataException($"Not a hex color: '{line}'.");
                colors.Add(0xFF000000 | rgb);
            }
        }
        if (colors.Count == 0) throw new InvalidDataException("No colors found in palette file.");
        return colors;

        static uint Byte(string s) => byte.TryParse(s, out byte b) ? b : throw new InvalidDataException($"Not a color value: '{s}'.");
    }

    public static string ToHex(IEnumerable<uint> colors) =>
        string.Concat(colors.Select(c => $"{c & 0xFFFFFF:x6}\n"));

    public static string ToGpl(IEnumerable<uint> colors, string name) =>
        $"GIMP Palette\nName: {name}\nColumns: 8\n#\n" +
        string.Concat(colors.Select(c => $"{c >> 16 & 0xFF,3} {c >> 8 & 0xFF,3} {c & 0xFF,3}\t#{c & 0xFFFFFF:X6}\n"));
}
