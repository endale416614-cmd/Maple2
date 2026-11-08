// ReSharper disable InconsistentNaming
using System;
using System.IO;

namespace Maple2.Tools;

public static class Paths {
    public static readonly string SOLUTION_DIR = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));

    public static readonly string DEBUG_TRIGGERS_DIR = Path.Combine(SOLUTION_DIR, "Maple2.Server.Game", "DebugTriggers");

    public static readonly string WEB_DATA_DIR = Path.Combine(SOLUTION_DIR, "Maple2.Server.Web", "Data");

    /// <summary>Where the web server keeps uploaded files (portraits, designs, banners, guild marks, blueprints): in the save, because
    /// updates replace the program folder (BT-15). Read at each use: DotEnv.Load sets SQLITE_DIR after this class has started.</summary>
    public static string UGC_DIR => UgcDirFor(Environment.GetEnvironmentVariable("SQLITE_DIR"));

    public static string UgcDirFor(string? sqliteDir) => string.IsNullOrEmpty(sqliteDir) ? WEB_DATA_DIR : Path.Combine(sqliteDir, "ugc");

    public static readonly string NAVMESH_DIR = Path.Combine(SOLUTION_DIR, "Maple2.Server.Game", "Navmeshes");
    public static readonly string NAVMESH_HASH_DIR = Path.Combine(SOLUTION_DIR, "Maple2.Server.Game", "Navmeshes", "Hashes");

}
