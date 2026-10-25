using Path = System.IO.Path;

namespace WindowSill.ClipboardHistory.Core;

internal static class ClipboardDataMigration
{
    private static readonly object Gate = new();
    internal static string ForkFolder(string sdkDataFolder)
        => Path.Combine(Directory.GetParent(sdkDataFolder)!.FullName, "WindowSill.ClipboardHistory.NoLimits");

    internal static string Prepare(string currentFolder)
    {
        lock (Gate) return PrepareCore(currentFolder);
    }

    private static string PrepareCore(string currentFolder)
    {
        string? parent = Directory.GetParent(currentFolder)?.FullName;
        if (parent is null) return currentFolder;
        string legacy = Path.Combine(parent, "WindowSill.ClipboardHistory");
        if (Path.GetFullPath(legacy).Equals(Path.GetFullPath(currentFolder), StringComparison.OrdinalIgnoreCase))
            return currentFolder;
        Directory.CreateDirectory(currentFolder);
        string marker = Path.Combine(currentFolder, "legacy-migration.complete");
        if (File.Exists(marker)) return currentFolder;
        foreach (string folder in new[] { "History", "Pinned" })
        {
            string source = Path.Combine(legacy, folder);
            string destination = Path.Combine(currentFolder, folder);
            Directory.CreateDirectory(destination);
            if (!Directory.Exists(source)) continue;
            foreach (string file in Directory.GetFiles(source))
            {
                if (Path.GetExtension(file) is not (".dat" or ".img")) continue;
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (File.Exists(target)) continue;
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(file, temporary, overwrite: false);
                    File.Move(temporary, target, overwrite: false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        File.WriteAllText(marker, "Encrypted legacy data copied. Original files retained.");
        return currentFolder;
    }
}
