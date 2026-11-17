using Maple2.Database.Context;
using Maple2.Model.Metadata;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Maple2.Database.Storage;

public class AiMetadataStorage(MetadataContext context) : MetadataStorage<string, AiMetadata>(context, CACHE_SIZE) {
    private const int CACHE_SIZE = 2500; // ~2.2k total items
    private string[]? allNamesCache;

    public bool TryGet(string name, [NotNullWhen(true)] out AiMetadata? npcAi) {
        if (Cache.TryGet(name, out npcAi)) {
            return true;
        }

        lock (Context) {
            // Double-checked locking
            if (Cache.TryGet(name, out npcAi)) {
                return true;
            }

            npcAi = Context.AiMetadata.Find(name);

            if (npcAi == null) {
                return false;
            }

            Cache.AddReplace(name, npcAi);
        }

        return true;
    }

    // NB-31 (BT-244): the AI_Default_Summon_<id> and _Normal sibling rules are reconstructions; the original's substitute rule is not captured.
    /// <summary>The AI to use for a path the data does not have: AI_Default_Summon_&lt;id&gt;.xml runs AI_Default_Summon.xml; the same path
    /// in a sibling folder named with "_Normal" (BossDungeon/Pinkbean/Chaos to BossDungeon/Pinkbean_Normal/Chaos); the one other path
    /// with the same file name, else the file name
    /// without a trailing "NA" (Papulatus ChaosNA -> Chaos); null when neither exists or the file name is not unique.</summary>
    public static string? Substitute(string path, IEnumerable<string> names) {
        static string FileName(string value) => value.Replace('\\', '/').Split('/')[^1];
        string file = FileName(path);
        if (Regex.IsMatch(file, @"^AI_Default_Summon_\d+\.xml$", RegexOptions.IgnoreCase)) {
            // The NPC's own summon list has the id first, which is what the base file's summon node 1 resolves to (38 of 38 NPCs).
            return names.FirstOrDefault(name => string.Equals(name, "AI_Default_Summon.xml", StringComparison.OrdinalIgnoreCase));
        }
        string[] parts = path.Replace('\\', '/').Split('/');
        for (int i = 0; i < parts.Length - 1; i++) {
            string sibling = string.Join('/', parts.Select((part, index) => index == i ? part + "_Normal" : part));
            string? found = names.FirstOrDefault(name => string.Equals(name.Replace('\\', '/'), sibling, StringComparison.OrdinalIgnoreCase));
            if (found != null) {
                return found;
            }
        }
        string[] same = names.Where(name => string.Equals(FileName(name), file, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (same.Length == 1) {
            return same[0];
        }
        if (file.EndsWith("NA.xml", StringComparison.OrdinalIgnoreCase)) {
            string withoutNa = file[..^"NA.xml".Length] + ".xml";
            string[] plain = names.Where(name => string.Equals(FileName(name), withoutNa, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (plain.Length == 1) {
                return plain[0];
            }
        }
        return null;
    }

    public bool TryGetOrSubstitute(string name, [NotNullWhen(true)] out AiMetadata? npcAi) {
        // First try the normal lookup
        if (TryGet(name, out npcAi)) {
            return true;
        }

        // If not found, try to find a substitute
        lock (Context) {
            // Build and cache all names once if needed
            if (allNamesCache is null) {
                allNamesCache = GetAis().Select(ai => ai.Name).ToArray();
            }

            string? substituteName = Substitute(name, allNamesCache);
            if (substituteName is not null && TryGet(substituteName, out npcAi)) {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<AiMetadata> GetAis() {
        lock (Context) {
            return Context.AiMetadata.ToList();
        }
    }
}
