using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OdataQueryLite.EFCore.Tests.Model;
using OdataQueryLite.Parsing;

namespace OdataQueryLite.EFCore.Tests
{
    public enum DivergenceStatus
    {
        Equal,
        Diverge,
        NoOracle,
        ClientEval,
        Error,
        Reject400,             // engine correctly rejected ($apply, or $select of a hidden [JsonIgnore] prop) — expected
    }

    /// <summary>One row of the structured divergence report — OUTPUT DATA, never an assertion target.</summary>
    public sealed record DivergenceRecord
    {
        public required string Label { get; init; }
        public required string Group { get; init; }
        public required DivergenceStatus Status { get; init; }
        public bool ClientEvalFired { get; init; }
        /// <summary>Whether the generated SQL parameterized its literals (@-marker present). Null when not captured.</summary>
        public bool? Parameterized { get; init; }
        public string? Detail { get; init; }
        public int EngineRowCount { get; init; }
        public int? GoldenRowCount { get; init; }
    }

    /// <summary>
    /// Drives one corpus case through the engine against SQLite-backed data and classifies the result
    /// against the golden oracle. NEVER throws on divergence — divergence is recorded and returned.
    /// SQL-translation gaps are detected via <c>ToQueryString()</c> (the filter throws there when it
    /// cannot be translated — EF Core 10's replacement for the removed QueryClientEvaluationWarning)
    /// and via the materialization catch for order-by / projection gaps.
    /// </summary>
    public static class EngineRunner
    {
        public static DivergenceRecord Run(CorpusCase c, TestDbContext db, GoldenFile golden)
        {
            try
            {
                OdataQueryOptions<Model.Item> opts;
                try
                {
                    opts = new OdataQueryOptions<Model.Item>(ToParts(c));
                }
                catch (UnsupportedQueryOptionException)
                {
                    // $apply path: construction rejects. Expected for Reject400 cases.
                    return Classify400(c, "engine rejected at construction (UnsupportedQueryOptionException)");
                }
                catch (OdataQueryException ex)
                {
                    return Classify400(c, "engine parse/validation error: " + ex.Message);
                }

                var result = opts.Apply(db.Set<Model.Item>());

                // ToQueryString() forces SQL translation WITHOUT executing. An untranslatable filter
                // throws here and is caught below as a translation gap. The returned SQL also reveals
                // whether literals were parameterized (@-marker) — the cache design requires it.
                // Order-by / projection live on Data, not Unpaged; their translation is exercised by
                // Materialize below (the same catch classifies a throw as ClientEval).
                string sql = result.Unpaged.ToQueryString();
                bool parameterized = sql.Contains('@');

                var rows = Materialize(result.Data);
                long? count = c.Count ? result.Unpaged.LongCount() : null;

                var engineRows = RowSetSerializer.CanonicalizeRows(rows);

                if (!golden.Entries.TryGetValue(c.Label, out var goldenEntry))
                    return new DivergenceRecord { Label = c.Label, Group = c.Group, Status = DivergenceStatus.NoOracle, Parameterized = parameterized, EngineRowCount = engineRows.Count, Detail = "no golden entry" };

                // Legacy couldn't produce a usable reference (it rejected/errored on this query, e.g.
                // nested $select it doesn't support) — there's nothing to compare against.
                if (goldenEntry.OracleStatus != "rows")
                    return new DivergenceRecord { Label = c.Label, Group = c.Group, Status = DivergenceStatus.NoOracle, Parameterized = parameterized, EngineRowCount = engineRows.Count, Detail = $"legacy produced no reference (oracle: {goldenEntry.OracleStatus}; {goldenEntry.Note})" };

                var (equal, detail) = Compare(engineRows, goldenEntry.Rows, count, goldenEntry.Count);
                return new DivergenceRecord
                {
                    Label = c.Label,
                    Group = c.Group,
                    Status = equal ? DivergenceStatus.Equal : DivergenceStatus.Diverge,
                    Parameterized = parameterized,
                    Detail = detail,
                    EngineRowCount = engineRows.Count,
                    GoldenRowCount = goldenEntry.Rows.Count,
                };
            }
            catch (Exception ex) when (EfTranslation.IsTranslationFailure(ex))
            {
                // EF Core 10's replacement for the removed QueryClientEvaluationWarning: the query
                // could not be translated to SQL. Recorded as a translation gap, not a harness error.
                return new DivergenceRecord { Label = c.Label, Group = c.Group, Status = DivergenceStatus.ClientEval, ClientEvalFired = true, Detail = "EF could not translate to SQL: " + ex.Message };
            }
            catch (OdataQueryException ex)
            {
                // Engine refused the query during Apply (e.g. $select naming a hidden [JsonIgnore]
                // property). Expected for Reject400 cases; an unexpected refusal on a Rows case
                // surfaces as Error via Classify400.
                return Classify400(c, "engine rejected during Apply: " + ex.Message);
            }
            catch (Exception ex)
            {
                return new DivergenceRecord { Label = c.Label, Group = c.Group, Status = DivergenceStatus.Error, Detail = ex.GetType().Name + ": " + ex.Message };
            }
        }

        private static DivergenceRecord Classify400(CorpusCase c, string detail) => c.Expect switch
        {
            ExpectKind.Reject400 => new() { Label = c.Label, Group = c.Group, Status = DivergenceStatus.Reject400, Detail = detail },
            // An unexpected rejection on a Rows case is itself an interesting divergence to surface.
            _ => new() { Label = c.Label, Group = c.Group, Status = DivergenceStatus.Error, Detail = "unexpected rejection: " + detail },
        };

        private static (bool equal, string detail) Compare(
            List<Dictionary<string, JsonElement>> engine,
            List<Dictionary<string, JsonElement>> goldenRows,
            long? engineCount, long? goldenCount)
        {
            if (engineCount.HasValue && goldenCount.HasValue && engineCount != goldenCount)
                return (false, $"count mismatch: engine={engineCount} golden={goldenCount}");

            if (engine.Count != goldenRows.Count)
                return (false, $"row-count mismatch: engine={engine.Count} golden={goldenRows.Count}");

            // Order-insensitive set comparison by canonical JSON of each row — orderby cases are rare
            // enough in the differential that exact-order comparison would add noise; the golden and
            // engine both come from the same seed so a set match is the meaningful signal.
            var engineSet = engine.Select(CanonRow).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var goldenSet = goldenRows.Select(CanonRow).OrderBy(s => s, StringComparer.Ordinal).ToList();
            for (int k = 0; k < engineSet.Count; k++)
            {
                if (!string.Equals(engineSet[k], goldenSet[k], StringComparison.Ordinal))
                    return (false, $"row diff at sorted index {k}: engine={engineSet[k]} golden={goldenSet[k]}");
            }
            return (true, "equal");
        }

        private static string CanonRow(Dictionary<string, JsonElement> row)
        {
            var sorted = row.OrderBy(kv => kv.Key, StringComparer.Ordinal);
            return string.Join("|", sorted.Select(kv => kv.Key + "=" + CanonJson(kv.Value)));
        }

        // Whitespace-, key-order- and array-order-insensitive rendering of a JsonElement. The engine's
        // Dictionary projection and the oracle's unwrapped wrapper serialize nested objects with
        // different indentation/property order, and a collection nav ($expand) comes back in
        // provider-arbitrary order (no ORDER BY on the nested set) — so compare semantic content as a
        // set, not raw text. Mirrors the order-insensitive top-level row-set comparison.
        private static string CanonJson(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(",", el.EnumerateObject()
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => JsonSerializer.Serialize(p.Name) + ":" + CanonJson(p.Value))) + "}",
            JsonValueKind.Array => "[" + string.Join(",", el.EnumerateArray()
                .Select(CanonJson).OrderBy(s => s, StringComparer.Ordinal)) + "]",
            _ => el.GetRawText(),
        };

        private static List<object> Materialize(IQueryable data)
        {
            var list = new List<object>();
            foreach (var row in data) list.Add(row);
            return list;
        }

        private static OdataQueryParts ToParts(CorpusCase c) => new()
        {
            Filter = c.Filter,
            OrderBy = c.OrderBy,
            Expand = c.Expand,
            Select = c.Select,
            Top = c.Top,
            Skip = c.Skip,
            Count = c.Count,
            Apply = c.Apply,
        };
    }
}
