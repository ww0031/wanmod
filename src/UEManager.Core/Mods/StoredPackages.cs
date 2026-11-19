namespace UEManager.Core.Mods;

/// <summary>
/// The downloads of installed mods that have parts to choose from. One stays after the install, so the player can
/// choose its parts again without a new download, and goes once no installed mod came from it.
/// </summary>
public static class StoredPackages
{
    /// <summary>Moves a download into <paramref name="folder"/>; one that is there already stays where it is.</summary>
    /// <exception cref="IOException">The download could not be moved.</exception>
    /// <exception cref="UnauthorizedAccessException">The download could not be moved.</exception>
    public static void Store(string download, string folder)
    {
        var stored = Path.Combine(folder, Path.GetFileName(download));
        if (string.Equals(Path.GetFullPath(download), Path.GetFullPath(stored), StringComparison.OrdinalIgnoreCase))
            return;
        Directory.CreateDirectory(folder);
        File.Move(download, stored, overwrite: true);
    }

    /// <summary>Deletes the stored downloads none of <paramref name="mods"/> came from: their mods were uninstalled or updated.</summary>
    public static void Sweep(string folder, IEnumerable<ModRecord> mods)
    {
        if (!Directory.Exists(folder))
            return;
        var used = mods.Select(m => m.SourceFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!used.Contains(Path.GetFileName(file)))
                FileSystem.TryDeleteFile(file);
        }
    }
}
