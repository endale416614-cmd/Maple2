using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Maple2.Database.Context;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

public class TriggerScriptMetadata(MetadataContext context) : MetadataStorage<(string, string), TriggerMetadata>(context, CACHE_SIZE) {
    private const int CACHE_SIZE = 5000; // ~5k total triggers

    public bool TryGet(string mapXBlock, string triggerName, [NotNullWhen(true)] out TriggerMetadata? trigger) {
        if (Cache.TryGet((mapXBlock, triggerName), out trigger)) {
            return true;
        }

        lock (Context) {
            // Double-checked locking
            if (Cache.TryGet((mapXBlock, triggerName), out trigger)) {
                return true;
            }

            trigger = Context.TriggerMetadata.Find(mapXBlock, triggerName);

            if (trigger == null) {
                return false;
            }

            Cache.AddReplace((mapXBlock, triggerName), trigger);
        }

        return true;
    }

    private static readonly Regex StateBlock = new(@"<state\b.*?</state>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex SetPortalAction = new(@"<action\s+name=""set_portal""([^>]*)>", RegexOptions.Compiled);
    private static readonly Regex PortalIdAttribute = new(@"\bportal_id=""(\d+)""", RegexOptions.Compiled);
    private static readonly Regex EnableAttribute = new(@"\benable=""(1|true)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const string PartyWaitCondition = @"name=""check_dungeon_lobby_user_count""";
    private readonly ConcurrentDictionary<string, IReadOnlySet<int>> templateLockedPortals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Portals that the map's scripts only ever shut in the dungeon party-wait template (the state that checks
    /// check_dungeon_lobby_user_count) and never turn on again. The template was pasted into many dungeon maps; on a map
    /// whose own scripts never reopen the portal, it shuts that portal for good.
    /// </summary>
    public IReadOnlySet<int> GetTemplateLockedPortalIds(string mapXBlock) {
        if (templateLockedPortals.TryGetValue(mapXBlock, out IReadOnlySet<int>? cached)) {
            return cached;
        }

        var shutByTemplate = new HashSet<int>();
        var changedElsewhere = new HashSet<int>();
        lock (Context) {
            foreach (string xml in Context.TriggerMetadata.Where(trigger => trigger.MapXBlock == mapXBlock).Select(trigger => trigger.Xml)) {
                foreach (Match state in StateBlock.Matches(xml)) {
                    bool partyWait = state.Value.Contains(PartyWaitCondition);
                    foreach (Match action in SetPortalAction.Matches(state.Value)) {
                        Match portalId = PortalIdAttribute.Match(action.Groups[1].Value);
                        if (!portalId.Success) {
                            continue;
                        }
                        int id = int.Parse(portalId.Groups[1].Value);
                        if (partyWait && !EnableAttribute.IsMatch(action.Groups[1].Value)) {
                            shutByTemplate.Add(id);
                        } else {
                            changedElsewhere.Add(id);
                        }
                    }
                }
            }
        }

        shutByTemplate.ExceptWith(changedElsewhere);
        templateLockedPortals[mapXBlock] = shutByTemplate;
        return shutByTemplate;
    }
}
