using System;
using System.Collections.Generic;
using System.IO;

namespace Maple2.Tools;

/// <summary>
/// Copies what versions up to 1.2.2 uploaded into the program folder over to a save's UGC folder, once per save (BT-15).
/// The old folder keeps its files and a file already in the save stays. The marker comes last, so after a crash or a failed
/// file the next start goes on where this one stopped. The old folder knows no saves, so each save gets all of it; a save
/// reads only the files its database names.
/// </summary>
public static class UgcFolder {
    public const string MARKER = ".copied-from-program-folder";
    // The release's own shop banner: SystemController reads it from the program folder.
    private const string SHIPPED = "system";

    /// <summary>The files copied, and a line for each one that could not be. A failed file never throws: the server must start.</summary>
    public static (int Copied, List<string> Failed) CopyOnce(string oldDir, string ugcDir) {
        int copied = 0;
        var failed = new List<string>();
        string marker = Path.Combine(ugcDir, MARKER);
        if (Path.GetFullPath(oldDir) == Path.GetFullPath(ugcDir) || File.Exists(marker)) {
            return (copied, failed);
        }
        try {
            Directory.CreateDirectory(ugcDir);
            foreach (string file in Directory.Exists(oldDir) ? Directory.EnumerateFiles(oldDir, "*", SearchOption.AllDirectories) : []) {
                string relative = Path.GetRelativePath(oldDir, file);
                if (relative.StartsWith(SHIPPED + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                try {
                    if (CopyIfMissing(file, Path.Combine(ugcDir, relative))) {
                        copied++;
                    }
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                    failed.Add($"{relative}: {ex.Message}");
                }
            }
            if (failed.Count == 0) {
                File.WriteAllText(marker, $"{copied} files\n");
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            failed.Add(ex.Message);
        }
        return (copied, failed);
    }

    private static bool CopyIfMissing(string source, string target) {
        if (File.Exists(target)) {
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        // Through a .part file, and to the disk first: a file under its real name is whole, so the next start may skip it.
        string part = target + ".part";
        using (FileStream from = File.OpenRead(source), to = File.Create(part)) {
            from.CopyTo(to);
            to.Flush(true);
        }
        File.Move(part, target); // throws if the target appeared: it is never overwritten
        return true;
    }
}
