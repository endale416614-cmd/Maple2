using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Database.Model.Metadata;
using Maple2.File.Ingest;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Ingest.Mapper;
using Maple2.File.IO;
using Maple2.File.IO.Nif;
using Maple2.File.Parser.Flat;
using Maple2.File.Parser.MapXBlock;
using Maple2.File.Parser.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;

const string locale = "NA";
string language = "en";
const string env = "Live";

Console.OutputEncoding = System.Text.Encoding.UTF8;

bool runNavmesh = false;
bool dropData = false;
bool copyGameFromMySql = false;
string? copyGameFromSqlite = null;
string? rebuildDataFromSqlite = null;
string? dumpXml = null;
string? probeXBlock = null;
string? probeAll = null;
string? exportedOverride = null;
bool mapEntitiesOnly = false;
bool listOnly = false;
var dumpArchives = new List<string>();

for (int i = 0; i < args.Length; i++) {
    string arg = args[i];
    switch (arg) {
        case "--run-navmesh":
            runNavmesh = true;
            break;
        case "--drop-data":
            dropData = true;
            break;
        case "--copy-game-from-mysql":
            copyGameFromMySql = true;
            break;
        case "--copy-game-from-sqlite" when i + 1 < args.Length:
            copyGameFromSqlite = args[++i];
            break;
        case "--rebuild-data-from-sqlite" when i + 1 < args.Length:
            rebuildDataFromSqlite = args[++i];
            break;
        case "--dump-xml" when i + 1 < args.Length:
            dumpXml = args[++i];
            break;
        case "--probe-xblock" when i + 1 < args.Length:
            probeXBlock = args[++i].ToLower();
            break;
        case "--probe-all" when i + 1 < args.Length:
            probeAll = args[++i];
            break;
        case "--exported" when i + 1 < args.Length:
            exportedOverride = args[++i];
            break;
        case "--map-entities-only":
            mapEntitiesOnly = true;
            break;
        case "--list" :
            listOnly = true;
            break;
        case "--archive" when i + 1 < args.Length:
            dumpArchives.Add(args[++i]);
            break;
    }
}

// Force Globalization to en-US because we use periods instead of commas for decimals
CultureInfo.CurrentCulture = new CultureInfo("en-US");

DotEnv.Load();

if (copyGameFromMySql) {
    GameDataCopier.CopyFromMySql();
    return;
}
if (copyGameFromSqlite != null) {
    GameDataCopier.CopyFromSqlite(copyGameFromSqlite);
    return;
}
if (rebuildDataFromSqlite != null) {
    GameDataCopier.RebuildDataFromSqlite(rebuildDataFromSqlite);
    return;
}

string? languageEnv = Environment.GetEnvironmentVariable("LANGUAGE");
if (languageEnv == null) {
    throw new ArgumentException("LANGUAGE environment variable was not set");
}
language = languageEnv.ToLower();

string? ms2Root = Environment.GetEnvironmentVariable("MS2_DATA_FOLDER");
if (ms2Root == null) {
    throw new ArgumentException("MS2_DATA_FOLDER environment variable was not set");
}

string xmlPath = Path.Combine(ms2Root, "Xml.m2d");
string exportedPath = Path.Combine(ms2Root, "Resource/Exported.m2d");
string serverPath = Path.Combine(ms2Root, "Server.m2d");

if (!File.Exists(xmlPath)) {
    throw new FileNotFoundException($"Could not find Xml.m2d file at path: {xmlPath}");
}

if (!File.Exists(exportedPath)) {
    throw new FileNotFoundException($"Could not find Exported.m2d file at path: {exportedPath}");
}

if (!File.Exists(serverPath)) {
    throw new FileNotFoundException($"Could not find Server.m2d file at path: {serverPath}\n" +
                                    "You can download this file from here: https://github.com/Zintixx/MapleStory2-XML/releases/latest");
}

// Prints the original XML of matching files from Xml.m2d / Server.m2d (research aid, read-only).
// --list prints only the names; --archive <path> (repeatable) reads other archives, e.g. the client's own Xml.m2d.
if (dumpXml != null) {
    foreach (string archive in dumpArchives.Count > 0 ? dumpArchives : [xmlPath, serverPath]) {
        using var reader = new M2dReader(archive);
        foreach (var entry in reader.Files.Where(file => file.Name.Contains(dumpXml, StringComparison.OrdinalIgnoreCase))) {
            Console.WriteLine($"===== {Path.GetFileName(archive)}:{entry.Name}");
            if (!listOnly) {
                Console.WriteLine(reader.GetString(entry));
            }
        }
    }
    return;
}

// Writes the id-carrying map entities of every xblock as TSV (xblock, name, type, ids) in one pass: lets the test tools
// tell "the NA map lacks this id" (original data) from "ingest dropped it" (research aid, read-only).
if (probeAll != null) {
    using var probeReader = new M2dReader(exportedOverride ?? exportedPath);
    var probeParser = new XBlockParser(probeReader, new FlatTypeIndex(probeReader));
    string[] idProperties = ["TriggerObjectID", "interactID", "SpawnPointID", "PortalID", "TargetFieldSN"];
    var getters = new System.Collections.Concurrent.ConcurrentDictionary<Type, (string Name, System.Reflection.PropertyInfo Property)[]>();
    var lines = new System.Collections.Concurrent.ConcurrentBag<string>();
    foreach (var map in probeParser.Parallel()) {
        foreach (var entity in map.entities) {
            Type type = entity.GetType();
            // The id properties are declared on the IMS2* interfaces the flat types implement.
            var properties = getters.GetOrAdd(type, t => idProperties
                .Select(name => (name, property: t.GetProperty(name) ?? t.GetInterfaces().Select(i => i.GetProperty(name)).FirstOrDefault(p => p != null)))
                .Where(pair => pair.property != null)
                .Select(pair => (pair.name, pair.property!))
                .ToArray());
            if (properties.Length == 0) {
                continue;
            }
            string ids = string.Join(" ", properties.Select(pair => $"{pair.Name}={pair.Property.GetValue(entity)}"));
            if (entity is Maple2.File.Flat.maplestory2library.ISpawnPointNPC npcSpawn) {
                // ingest drops NPC spawns whose count is 0 or whose NPC ids are unknown: keep what the map lists
                ids += $" NpcCount={npcSpawn.NpcCount} NpcList={string.Join(",", npcSpawn.NpcList.Select(pair => $"{pair.Key}:{pair.Value}"))}";
            }
            lines.Add($"{map.xblock.ToLower()}\t{entity.EntityName}\t{type.Name}\t{ids}");
        }
    }
    System.IO.File.WriteAllLines(probeAll, lines.OrderBy(line => line, StringComparer.Ordinal));
    Console.WriteLine($"{lines.Count} entities -> {probeAll}");
    return;
}

// Lists every map entity of one xblock with its parser type and interfaces (research aid, read-only).
if (probeXBlock != null) {
    using var probeReader = new M2dReader(exportedOverride ?? exportedPath);
    var probeParser = new XBlockParser(probeReader, new FlatTypeIndex(probeReader));
    foreach (var map in probeParser.Parallel().Where(map => map.xblock.ToLower() == probeXBlock)) {
        foreach (var entity in map.entities) {
            string interfaces = string.Join(",", entity.GetType().GetInterfaces().Select(type => type.Name).Where(name => name.StartsWith("IMS2")).OrderBy(name => name));
            string detail = entity switch {
                Maple2.File.Flat.maplestory2library.IMS2Liftable liftable => $" ItemID={liftable.ItemID} Stack={liftable.ItemStackCount} Life={liftable.ItemLifeTime}",
                _ => "",
            };
            if (entity is Maple2.File.Flat.maplestory2library.IMS2MapProperties { IsObjectWeapon: true } && entity is Maple2.File.Flat.maplestory2library.IMS2PhysXProp weapon) {
                detail += $" ObjectWeapon={weapon.ObjectWeaponItemCode}";
            }
            Console.WriteLine($"{entity.EntityName} | {entity.GetType().Name} | {interfaces}{detail}");
        }
    }
    return;
}

if (DbProvider.IsSqlite) {
    Console.WriteLine("Creating embedded game database...");
    DbProvider.EnsureSqliteGameSchema();
} else {
string worldServerDir = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.World");

bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

// check if dotnet ef is installed
Process processCheck;
if (isWindows) {
    processCheck = Process.Start("CMD.exe", "/C dotnet ef");
} else if (isLinux || isMac) {
    processCheck = Process.Start("bash", "-c \"dotnet ef\"");
} else {
    throw new PlatformNotSupportedException("Unsupported OS platform");
}
processCheck.WaitForExit();

if (processCheck.ExitCode != 0) {

    Process installEf;
    if (isWindows) {
        installEf = Process.Start("CMD.exe", "/C dotnet tool install --global dotnet-ef");
    } else if (isLinux || isMac) {
        installEf = Process.Start("bash", "-c \"dotnet tool install --global dotnet-ef\"");
    } else {
        throw new PlatformNotSupportedException("Unsupported OS platform");
    }
    installEf.WaitForExit();
    if (installEf.ExitCode != 0) {
        throw new Exception("Failed to install dotnet-ef. Please install it manually by running 'dotnet tool install --global dotnet-ef'");
    }

    if (isWindows) {
        string dotnetToolsPath = Environment.GetEnvironmentVariable("USERPROFILE") + "/.dotnet/tools";
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", currentPath + ";" + dotnetToolsPath);
        Console.WriteLine($"Updated PATH to include {dotnetToolsPath}");
    } else if (isLinux || isMac) {
        string dotnetToolsPath = Environment.GetEnvironmentVariable("HOME") + "/.dotnet/tools";
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", currentPath + ":" + dotnetToolsPath);
        Console.WriteLine($"Updated PATH to include {dotnetToolsPath}");
    } else {
        throw new PlatformNotSupportedException("Unsupported OS platform");
    }
}

string cmdCommand = "cd " + worldServerDir + " && dotnet ef database update";

Console.WriteLine("Migrating game database...");

Process process;
if (isWindows) {
    process = Process.Start("CMD.exe", "/C " + cmdCommand);
} else if (isLinux || isMac) {
    process = Process.Start("bash", "-c \"" + cmdCommand + "\"");
} else {
    throw new PlatformNotSupportedException("Unsupported OS platform");
}

process.WaitForExit();

if (process.ExitCode != 0) {
    throw new Exception("Failed to migrate game database.");
}

Console.WriteLine("Game Migration complete!");
}

// Re-imports only the map-entity table (fast; --exported lets it read a copy while the game is running).
if (mapEntitiesOnly) {
    using var entityContext = new MetadataContext(DbProvider.Options(DbProvider.DATA_DB));
    using var entityReader = new M2dReader(exportedOverride ?? exportedPath);
    // Spawn points are kept only for known NPCs; a full ingest fills this from NpcMapper, here it comes from the database.
    foreach (Maple2.Model.Metadata.NpcMetadata npc in entityContext.NpcMetadata.AsNoTracking()) {
        NpcMapper.NpcMetadataById[npc.Id] = npc;
    }
    // Portals and other entities are filtered by feature/locale like in a full ingest.
    using (var featureReader = new M2dReader(xmlPath)) {
        Filter.Load(featureReader, locale, env);
    }
    if (NpcMapper.NpcMetadataById.Count == 0) {
        Console.WriteLine("No NPC metadata in the database; refusing to re-import map entities (spawn points would be dropped).");
        return;
    }
    UpdateDatabase(entityContext, new MapEntityMapper(entityContext, new XBlockParser(entityReader, new FlatTypeIndex(entityReader))));
    Console.WriteLine("Map entities re-imported.");
    return;
}

using var xmlReader = new M2dReader(xmlPath);
using var exportedReader = new M2dReader(exportedPath);
using var serverReader = new M2dReader(serverPath);

DbContextOptions options = DbProvider.Options(DbProvider.DATA_DB);

Console.WriteLine("Connecting to metadata database...");
using var metadataContext = new MetadataContext(options);

bool schemaChanged = SchemaVersionManager.ShouldRecreateDatabase(metadataContext);

if (dropData || schemaChanged) {
    Console.WriteLine("Dropping metadata database...");
    metadataContext.Database.EnsureDeleted();
    metadataContext.ChangeTracker.Clear();
}
Console.WriteLine("Ensuring metadata database is created...");
metadataContext.Database.EnsureCreated();
if (!DbProvider.IsSqlite) {
    metadataContext.Database.ExecuteSqlRaw(@"SET GLOBAL max_allowed_packet=268435456"); // 256MB
}

// Store schema version after creation
SchemaVersionManager.StoreSchemaVersion(metadataContext);

Console.WriteLine("Starting data ingestion...");

// Filter Xml results based on feature settings.
Filter.Load(xmlReader, locale, env);

// new TriggerGenerator(xmlReader).Generate();

var modelReaders = new List<PrefixedM2dReader> {
    new PrefixedM2dReader("/library/", Path.Combine(ms2Root, "Resource/Library.m2d")),
    new PrefixedM2dReader("/model/map/", Path.Combine(ms2Root, "Resource/Model/Map.m2d")),
    new PrefixedM2dReader("/model/effect/", Path.Combine(ms2Root, "Resource/Model/Effect.m2d")),
    new PrefixedM2dReader("/model/camera/", Path.Combine(ms2Root, "Resource/Model/Camera.m2d")),
    new PrefixedM2dReader("/model/tool/", Path.Combine(ms2Root, "Resource/Model/Tool.m2d")),
    new PrefixedM2dReader("/model/item/", Path.Combine(ms2Root, "Resource/Model/Item.m2d")),
    new PrefixedM2dReader("/model/npc/", Path.Combine(ms2Root, "Resource/Model/Npc.m2d")),
    new PrefixedM2dReader("/model/path/", Path.Combine(ms2Root, "Resource/Model/Path.m2d")),
    new PrefixedM2dReader("/model/character/", Path.Combine(ms2Root, "Resource/Model/Character.m2d")),
    new PrefixedM2dReader("/model/textures/", Path.Combine(ms2Root, "Resource/Model/Textures.m2d")),
};

UpdateDatabase(metadataContext, new TriggerMapper(xmlReader));

UpdateDatabase(metadataContext, new ItemMapper(xmlReader, language, false));
UpdateDatabase(metadataContext, new NpcMapper(xmlReader, language));

UpdateDatabase(metadataContext, new ServerTableMapper(serverReader));
UpdateDatabase(metadataContext, new AiMapper(serverReader));

UpdateDatabase(metadataContext, new AdditionalEffectMapper(xmlReader));
UpdateDatabase(metadataContext, new AnimationMapper(xmlReader));
UpdateDatabase(metadataContext, new PetMapper(xmlReader));
UpdateDatabase(metadataContext, new MapMapper(xmlReader, language));
UpdateDatabase(metadataContext, new UgcMapMapper(xmlReader));
UpdateDatabase(metadataContext, new ExportedUgcMapMapper(xmlReader));
UpdateDatabase(metadataContext, new QuestMapper(xmlReader, language));
UpdateDatabase(metadataContext, new RideMapper(xmlReader));
UpdateDatabase(metadataContext, new ScriptMapper(xmlReader, language));
UpdateDatabase(metadataContext, new SkillMapper(xmlReader, language));
UpdateDatabase(metadataContext, new TableMapper(xmlReader, language));
UpdateDatabase(metadataContext, new AchievementMapper(xmlReader));
UpdateDatabase(metadataContext, new FunctionCubeMapper(xmlReader));
UpdateDatabase(metadataContext, new BanWordMapper(xmlReader));

NifParserHelper.ParseNif(modelReaders);

UpdateDatabase(metadataContext, new NifMapper());
UpdateDatabase(metadataContext, new NxsMeshMapper());

var index = new FlatTypeIndex(exportedReader);

var xBlockParser = new XBlockParser(exportedReader, index);

UpdateDatabase(metadataContext, new MapEntityMapper(metadataContext, xBlockParser));

var mapDataMapper = new MapDataMapper(metadataContext, xBlockParser);

UpdateDatabase(metadataContext, mapDataMapper);

mapDataMapper.ReportStats();

if (runNavmesh) {
    _ = new NavMeshMapper(metadataContext, exportedReader);
}

Console.WriteLine("Done!".ColorGreen());

void UpdateDatabase<T>(DbContext context, TypeMapper<T> mapper) where T : class {
    string? tableName = context.GetTableName<T>();
    Debug.Assert(!string.IsNullOrEmpty(tableName), $"Invalid table name: {tableName}");

    Console.Write($"Processing {tableName}... ");
    uint crc32C = mapper.Process();
    Console.Write($"Finished in {mapper.ElapsedMilliseconds}ms");
    Console.WriteLine();

    var checksum = context.Find<TableChecksum>(tableName);
    if (checksum != null) {
        if (checksum.Crc32C == crc32C) {
            Console.WriteLine($"Table {tableName} is up-to-date".ColorGreen());
            return;
        }

        checksum.Crc32C = crc32C;
        Console.WriteLine($"Table {tableName} outdated".ColorRed());
        int result = context.Database.ExecuteSqlRaw(@$"DELETE FROM `{tableName}`");
        Console.WriteLine($"Removed table {tableName} rows: {result}");
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    // Write entries to table
    foreach (T result in mapper.Results) {
        context.Add(result);
    }

    // Write checksum to table
    if (checksum == null) {
        context.Add(new TableChecksum {
            TableName = tableName,
            Crc32C = crc32C,
        });
    } else {
        context.Update(checksum);
    }

    context.SaveChanges();

    stopwatch.Stop();
    Console.WriteLine($"Wrote {mapper.Results.Count} entries to {tableName} in {stopwatch.ElapsedMilliseconds}ms");
}
