using System.Windows;
using Microsoft.Win32;
using PixelForge.Core;

namespace PixelForge.App;

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
            if (loose > 0 && MessageBox.Show(owner, $"The folder already has {loose} PixelForge file(s). Add them to the project? (They are not changed.)",
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
        var dlg = new OpenFileDialog { Filter = "PixelForge project (*.pfproj)|*.pfproj" };
        if (dlg.ShowDialog(owner) == true) OpenPath(dlg.FileName);
    }

    public static void OpenPath(string path)
    {
        try { Opened(Project.Load(path)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    public static void FromFolder(Window owner)
    {
        var dlg = new OpenFolderDialog { Title = "Folder with your PixelForge files" };
        if (dlg.ShowDialog(owner) != true) return;
        try { Opened(Project.CreateFromFolder(dlg.FolderName, Path.GetFileName(dlg.FolderName))); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { Ui.Error(ex); }
    }

    public static void Close() => App.SetProject(null);

    static void Opened(Project p)
    {
        App.SetProject(p);
        App.Settings.AddRecent(p.FilePath);
    }

    public static IEnumerable<string> RecentProjects() =>
        App.Settings.RecentFiles.Where(f => f.EndsWith(Project.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(f));
}
