using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pixelkiln.Core;

/// <summary>
/// Loading and saving of every Pixelkiln JSON document:
/// clear errors for damaged files, refusal of files from newer versions, atomic writes
/// (a failed save never damages the existing file) and a one-time backup before a migrated
/// older file is overwritten.
/// </summary>
public static class DocumentFile
{
    /// <summary>Indented JSON for exports and reports; one shared instance, so System.Text.Json can cache its metadata.</summary>
    internal static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>"Version" of a JSON document; missing = 1 (the first formats had none), unreadable = 0.</summary>
    public static int PeekVersion(string json)
    {
        try { return JsonNode.Parse(json)?["Version"]?.GetValue<int>() ?? 1; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return 0; }
    }

    /// <summary>Reads and parses a document; any parse problem becomes one readable <see cref="InvalidDataException"/>.</summary>
    public static T Load<T>(string path, string kind, int currentVersion, Func<string, T> parse)
    {
        string text = File.ReadAllText(path);   // missing file: FileNotFoundException with the path, as is
        int version = PeekVersion(text);
        if (version > currentVersion)
            throw new InvalidDataException($"“{Path.GetFileName(path)}” was saved by a newer Pixelkiln ({kind} format {version}; this version reads up to {currentVersion}).");
        try { return parse(text); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or ArgumentException
                                       or IndexOutOfRangeException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidDataException($"“{Path.GetFileName(path)}” is not a valid {kind} file or is damaged. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves atomically. If the file on disk is an older format version, it is first copied to
    /// "&lt;file&gt;.v&lt;old&gt;.bak" (once), so a migration can always be undone by hand.
    /// </summary>
    public static void Save(string path, string text, int currentVersion)
    {
        if (File.Exists(path))
        {
            int old = PeekVersion(File.ReadAllText(path));
            string bak = $"{path}.v{old}.bak";
            if (old > 0 && old < currentVersion && !File.Exists(bak)) File.Copy(path, bak);
        }
        WriteAtomic(path, text);
    }

    /// <summary>Writes to "&lt;file&gt;.tmp", then swaps it in; on any failure the old file stays untouched.</summary>
    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (IOException) { }   // leftover .tmp is harmless; the real file is what matters
        }
    }

    /// <summary>Binary files (zips, WAVs): written to "&lt;file&gt;.tmp", then moved over the target, so a failure never leaves half a file.</summary>
    public static void WriteViaTemp(string path, Action<Stream> write)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var f = File.Create(tmp)) write(f);
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
