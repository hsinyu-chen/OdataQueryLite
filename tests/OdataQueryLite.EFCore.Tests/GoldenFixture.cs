using System.Text.Json;
using System.Text.Json.Serialization;

namespace OdataQueryLite.EFCore.Tests
{
    /// <summary>
    /// One golden entry per corpus case label. The oracle (Tier 3) produces these; the engine
    /// runner (Tier 2) reads them and compares. <see cref="Rows"/> is the canonical serialized
    /// row-set; <see cref="Count"/> is the unpaged total when the case requested $count.
    /// </summary>
    public sealed class GoldenEntry
    {
        public string Label { get; set; } = string.Empty;

        /// <summary>What the oracle did: "rows", "reject" (legacy threw / refused), "error".</summary>
        public string OracleStatus { get; set; } = "rows";

        /// <summary>Canonical row-set: a list of property-bag rows, each a sorted-key dictionary.</summary>
        public List<Dictionary<string, JsonElement>> Rows { get; set; } = new();

        public long? Count { get; set; }

        /// <summary>Free-form note from the oracle (e.g. legacy's actual behavior on $apply).</summary>
        public string? Note { get; set; }
    }

    /// <summary>The whole golden file: case-label -> entry.</summary>
    public sealed class GoldenFile
    {
        public string EngineVersionNote { get; set; } = string.Empty;
        public Dictionary<string, GoldenEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Shared canonical serialization for materialized row-sets. Both products MUST canonicalize
    /// identically or the comparison is meaningless — so the logic lives here, used by both.
    /// </summary>
    public static class RowSetSerializer
    {
        private static readonly JsonSerializerOptions Canonical = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            // Round-trippable, culture-invariant numeric/date formatting.
            NumberHandling = JsonNumberHandling.Strict,
        };

        /// <summary>
        /// Canonicalizes a single materialized row (an entity or a projection dictionary) into a
        /// key-sorted Dictionary&lt;string, JsonElement&gt;. Navigation references that EF returns
        /// as full entities are reduced to their scalar columns only (no recursion into related
        /// entities) so the two products' default materialization can't diverge on lazy/eager nav
        /// loading — projection cases ($select/$expand) carry their nav shape explicitly via the
        /// dictionary the engine/oracle already built.
        /// </summary>
        public static Dictionary<string, JsonElement> Canonicalize(object row)
        {
            JsonElement el = JsonSerializer.SerializeToElement(NormalizeValue(row), Canonical);
            var bag = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in el.EnumerateObject())
                    bag[prop.Name] = prop.Value.Clone();
            }
            else
            {
                bag["$value"] = el.Clone();
            }
            return SortKeys(bag);
        }

        private static Dictionary<string, JsonElement> SortKeys(Dictionary<string, JsonElement> bag)
        {
            var sorted = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var key in bag.Keys.OrderBy(k => k, StringComparer.Ordinal))
                sorted[key] = bag[key];
            return sorted;
        }

        /// <summary>
        /// Strips EF navigation entity instances to scalar columns only for the unprojected case so
        /// canonicalization is deterministic regardless of how each provider populated navs. For
        /// projection dictionaries (Dictionary&lt;string,object?&gt;) the shape is already explicit
        /// and passes through unchanged.
        /// </summary>
        private static object? NormalizeValue(object? row)
        {
            if (row is null) return null;

            if (row is IDictionary<string, object?> dict)
            {
                var clean = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (k, v) in dict)
                    clean[k] = NormalizeProjectedValue(v);
                return clean;
            }

            // Entity (no projection): emit scalar/primitive properties only, skip navs/collections.
            var type = row.GetType();
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var p in type.GetProperties())
            {
                if (p.GetIndexParameters().Length > 0) continue;
                var pt = p.PropertyType;
                if (!IsScalar(pt)) continue; // skip Item Parent / Category / ICollection<Tag>
                result[p.Name] = Stringify(p.GetValue(row));
            }
            return result;
        }

        private static object? NormalizeProjectedValue(object? v)
        {
            if (v is null) return null;
            if (v is IDictionary<string, object?> nested)
            {
                var clean = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (k, vv) in nested) clean[k] = NormalizeProjectedValue(vv);
                return clean;
            }
            if (v is System.Collections.IEnumerable en and not string)
            {
                var list = new List<object?>();
                foreach (var item in en) list.Add(NormalizeProjectedValue(item));
                return list;
            }
            return Stringify(v);
        }

        // Enums and dates render to invariant strings so the two products agree byte-for-byte.
        private static object? Stringify(object? v) => v switch
        {
            null => null,
            DateTimeOffset dto => dto.ToUniversalTime().ToString("o"),
            DateTime dt => dt.ToUniversalTime().ToString("o"),
            Enum e => e.ToString(),
            // Strip trailing zeros so a decimal compares by value, not scale: SQLite round-trips
            // decimal-as-TEXT and can drop a trailing zero (100.00 -> 100.0) — the same number.
            decimal d => d.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture),
            _ => v,
        };

        private static bool IsScalar(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal)
                || u == typeof(DateTime) || u == typeof(DateTimeOffset) || u == typeof(Guid);
        }

        public static List<Dictionary<string, JsonElement>> CanonicalizeRows(IEnumerable<object> rows)
            => rows.Select(Canonicalize).ToList();

        public static string ToJson(GoldenFile file) => JsonSerializer.Serialize(file, FileOptions);
        public static GoldenFile FromJson(string json) => JsonSerializer.Deserialize<GoldenFile>(json, FileOptions) ?? new GoldenFile();

        private static readonly JsonSerializerOptions FileOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = null,
        };
    }
}
