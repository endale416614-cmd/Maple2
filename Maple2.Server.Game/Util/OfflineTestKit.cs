using System.Text.Json;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition play-testing aid, OFF unless OFFLINE_TESTKIT=true (launcher.ini: TestKit=true).
/// Kits are defined in offline-testkit.json (path: OFFLINE_TESTKIT_FILE, default next to the solution) and handed out
/// with the admin chat command "/testkit &lt;name&gt;". Items of an enabled kit ignore level and job limits.
/// </summary>
public static class OfflineTestKit {
    public sealed record Kit(string Description, int Rarity, int Enchant, bool RollMax, int[] Items);
    private sealed record KitFile(Dictionary<string, Kit> Kits);

    private static readonly Lazy<IReadOnlyDictionary<string, Kit>> LazyKits = new(Load);
    private static readonly Lazy<HashSet<int>> LazyItems = new(() => LazyKits.Value.Values.SelectMany(kit => kit.Items).ToHashSet());

    public static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_TESTKIT"), "true", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, Kit> Kits => Enabled ? LazyKits.Value : new Dictionary<string, Kit>();

    /// <summary>Whether this item may be equipped regardless of level and job (test-kit items only).</summary>
    public static bool IgnoresRestrictions(int itemId) => Enabled && LazyItems.Value.Contains(itemId);

    private static IReadOnlyDictionary<string, Kit> Load() {
        string path = Environment.GetEnvironmentVariable("OFFLINE_TESTKIT_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-testkit.json");
        if (!File.Exists(path)) {
            return new Dictionary<string, Kit>();
        }
        try {
            KitFile? file = JsonSerializer.Deserialize<KitFile>(File.ReadAllText(path), new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return new Dictionary<string, Kit>(file?.Kits ?? [], StringComparer.OrdinalIgnoreCase);
        } catch (JsonException ex) {
            Log.Logger.Error(ex, "Invalid test kit file {Path}", path);
            return new Dictionary<string, Kit>();
        }
    }
}
