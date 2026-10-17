using Maple2.Model.Enum;

namespace Maple2.Server.Game.Commands;

/// <summary>Turns the words of "/give Red Potion 5" into an item id and a count.</summary>
public static class ItemNameResolver {
    public const int MAX_LISTED = 10;

    public readonly record struct Entry(int Id, string Name, JobCode[] Jobs);

    /// <param name="Id">The chosen item, or 0 when there is none.</param>
    /// <param name="Options">Candidates to show when the name is ambiguous, at most <see cref="MAX_LISTED"/>.</param>
    public readonly record struct Result(string Name, int Count, int Id, IReadOnlyList<Entry> Options);

    /// <param name="search">Items whose name contains the given text.</param>
    public static Result Resolve(string[] words, JobCode job, Func<string, IEnumerable<Entry>> search) {
        string name = string.Join(' ', words).Trim();
        int count = 1;
        List<Entry> hits = Search(search, name);

        // A trailing number is the count, unless the full text is itself an item name ("Onyx Crystal 2").
        if (words.Length > 1 && int.TryParse(words[^1], out int parsed) && parsed > 0 && !hits.Any(e => IsName(e, name))) {
            name = string.Join(' ', words[..^1]).Trim();
            count = parsed;
            hits = Search(search, name);
        }

        List<Entry> exact = hits.Where(e => IsName(e, name)).ToList();
        if (exact.Count > 1) {
            List<Entry> forJob = exact.Where(e => e.Jobs.Contains(job)).ToList();
            if (forJob.Count > 0) {
                exact = forJob;
            }
        }
        if (exact.Count == 1) {
            return new Result(name, count, exact[0].Id, []);
        }
        if (exact.Count == 0 && hits.Count == 1) {
            return new Result(name, count, hits[0].Id, []);
        }
        return new Result(name, count, 0, (exact.Count > 0 ? exact : hits).Take(MAX_LISTED).ToList());
    }

    private static List<Entry> Search(Func<string, IEnumerable<Entry>> search, string name) {
        return name.Length == 0
            ? []
            : search(name).Where(e => !string.IsNullOrEmpty(e.Name)).OrderBy(e => e.Id).ToList();
    }

    private static bool IsName(Entry entry, string name) => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
}
