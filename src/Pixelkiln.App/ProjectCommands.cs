using System.Windows;
using Microsoft.Win32;
using Pixelkiln.Core;

namespace Pixelkiln.App;

/// <summary>Project menu actions, shared by the menu bar, the asset browser and the command palette.</summary>
static class ProjectCommands
{
    public static void New(Window owner)
    {
        var dlg = new OpenFolderDialog { Title = "Folder for the new project" };
        if (dlg.ShowDialog(owner) != true) return;
        if (Ui.Ask(owner, "New project", "Project name:", Path.GetFileName(dlg.FolderName)) is not string name) return;
        try
        {
            var p = Project.Create(dlg.FolderName, name.Trim());
            int loose = p.UnregisteredFiles().Count();
            if (loose > 0 && MessageBox.Show(owner, $"The folder already has {loose} Pixelkiln file(s). Add them to the project? (They are not changed.)",
                    "New project", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var f in p.UnregisteredFiles()) p.Add(f);
            }
            Opened(p);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    public static void Open(Window owner)
    {
        var dlg = new OpenFileDialog { Filter = "Pixelkiln project (*.pfproj)|*.pfproj" };
        if (dlg.ShowDialog(owner) == true) OpenPath(dlg.FileName);
    }

    public static void OpenPath(string path)
    {
        try { Opened(Project.Load(path)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    public static void FromFolder(Window owner)
    {
        var dlg = new OpenFolderDialog { Title = "Folder with your Pixelkiln files" };
        if (dlg.ShowDialog(owner) != true) return;
        try { Opened(Project.CreateFromFolder(dlg.FolderName, Path.GetFileName(dlg.FolderName))); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    public static void Close() => App.SetProject(null);

    /// <summary>Default home of the sample game (Documents\Pixelkiln samples\Sample game).</summary>
    public static string SampleFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Pixelkiln samples", SampleProject.Name);

    /// <summary>
    /// Opens the sample game: the existing one when it is there, otherwise it is created (in the default folder or one
    /// the user picks). Returns the project, or null when cancelled.
    /// </summary>
    public static Project? OpenSample(Window owner)
    {
        if (App.Project is { Name: SampleProject.Name } open) return open;
        string file = Path.Combine(SampleFolder, SampleProject.Name + Project.Extension);
        try
        {
            if (File.Exists(file)) { var existing = Project.Load(file); Opened(existing); return existing; }
            string folder = SampleFolder;
            var answer = MessageBox.Show(owner, $"Create the sample game in\n{folder}?\n\nNo = choose another folder.", "Sample game", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return null;
            if (answer == MessageBoxResult.No)
            {
                var dlg = new OpenFolderDialog { Title = "Folder for the sample game (a “Sample game” folder is made inside)" };
                if (dlg.ShowDialog(owner) != true) return null;
                folder = Path.Combine(dlg.FolderName, SampleProject.Name);
            }
            var p = SampleProject.Create(folder);
            Opened(p);
            return p;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Ui.Error(ex); return null; }
    }

    static void Opened(Project p)
    {
        App.SetProject(p);
        App.Settings.AddRecent(p.FilePath);
    }

    public static IEnumerable<string> RecentProjects() =>
        App.Settings.RecentFiles.Where(f => f.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(f));
}
