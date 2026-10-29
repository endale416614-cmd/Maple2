using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Maple2.Tools.Extensions;

public static class EnumSkipLog {
    private static readonly ConcurrentQueue<string> entries = new();

    public static T ParseOrDefault<T>(string? value, T fallback, string context, bool ignoreCase = false) where T : struct, Enum {
        if (string.IsNullOrEmpty(value)) {
            return fallback;
        }

        // For flags enums, any successful parse is accepted
        if (typeof(T).GetCustomAttribute<FlagsAttribute>() != null) {
            if (Enum.TryParse<T>(value, ignoreCase, out T result)) {
                return result;
            }
        } else {
            // For non-flags enums, check if the value is defined
            if (Enum.TryParse<T>(value, ignoreCase, out T result) && Enum.IsDefined<T>(result)) {
                return result;
            }
        }

        // Log unknown values
        entries.Enqueue($"{context}: {typeof(T).Name} '{value}'");
        return fallback;
    }

    public static bool TryParseOrSkip<T>(string? value, string context, out T result, bool ignoreCase = false) where T : struct, Enum {
        if (string.IsNullOrEmpty(value)) {
            result = default;
            return false;
        }

        // For flags enums, any successful parse is accepted
        if (typeof(T).GetCustomAttribute<FlagsAttribute>() != null) {
            if (Enum.TryParse<T>(value, ignoreCase, out result)) {
                return true;
            }
        } else {
            // For non-flags enums, check if the value is defined
            if (Enum.TryParse<T>(value, ignoreCase, out result) && Enum.IsDefined<T>(result)) {
                return true;
            }
        }

        // Log unknown values
        entries.Enqueue($"{context}: {typeof(T).Name} '{value}'");
        result = default;
        return false;
    }

    // A value that is not an enum name, for the same log.
    public static void Note(string entry) => entries.Enqueue(entry);

    public static IReadOnlyCollection<string> Entries => entries.ToList().AsReadOnly();

    public static void Clear() {
        while (entries.TryDequeue(out _)) {
            // Clear all entries
        }
    }

    public static void WriteLog(string path) {
        File.WriteAllLines(path, entries);
    }
}