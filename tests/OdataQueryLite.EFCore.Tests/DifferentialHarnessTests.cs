using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OdataQueryLite.EFCore.Tests.Model;
using OdataQueryLite.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace OdataQueryLite.EFCore.Tests
{
    /// <summary>
    /// Differential verification harness for the OdataQueryLite engine, run over EF Core + SQLite
    /// (real SQL translation, NOT the InMemory provider). Divergences from the golden oracle are
    /// EMITTED as a structured report, not asserted as failures — per the harness design constraint,
    /// the engine source is never modified and the comparison is never tuned to force agreement.
    /// </summary>
    public sealed class DifferentialHarnessTests(ITestOutputHelper output)
    {
        private static string GoldenPath => "Fixtures/" + HarnessConfig.GoldenFileName;

        private static GoldenFile LoadGolden()
        {
            if (!File.Exists(GoldenPath)) return new GoldenFile();
            return RowSetSerializer.FromJson(File.ReadAllText(GoldenPath));
        }

        // ── PLUMBING SMOKE TEST ────────────────────────────────────────────────────────────────
        // Runs 2 trivial cases through the engine runner end-to-end: confirms SQLite is the provider,
        // the client-eval listener is wired, the golden is read, and the comparison+report works.
        // Does NOT run the full B+A corpus (that's the on-demand report-generation run below).
        [Fact]
        public void Smoke_Plumbing_EndToEnd()
        {
            using var factory = new TestDbFactory();
            var db = factory.Context;

            // Confirm a real translating provider (never the EF InMemory provider) is in use.
            Assert.True(
                db.Database.ProviderName is "Microsoft.EntityFrameworkCore.Sqlite" or "Microsoft.EntityFrameworkCore.SqlServer",
                $"expected a real SQL-translating provider, got '{db.Database.ProviderName}'");

            var golden = LoadGolden();

            var trivial = new[]
            {
                new CorpusCase { Label = "SMOKE_quantity_gt_0", Group = "SMOKE", Filter = "Quantity gt 0" },
                new CorpusCase { Label = "SMOKE_top_1", Group = "SMOKE", Top = 1, OrderBy = "Id asc" },
            };

            var report = new List<DivergenceRecord>();
            foreach (var c in trivial)
                report.Add(EngineRunner.Run(c, db, golden));

            foreach (var r in report)
                output.WriteLine($"[{r.Status}] {r.Label} engineRows={r.EngineRowCount} goldenRows={r.GoldenRowCount} clientEval={r.ClientEvalFired} :: {r.Detail}");

            // Plumbing assertions ONLY (not engine-correctness): the runner produced a record per
            // case and the engine actually executed against the provider (rows materialized).
            Assert.Equal(trivial.Length, report.Count);
            Assert.All(report, r => Assert.NotEqual(DivergenceStatus.Error, r.Status));
            // Quantity gt 0 must materialize >0 rows from the seed (proves the SQLite query ran).
            var qtyCase = report.Single(r => r.Label == "SMOKE_quantity_gt_0");
            Assert.True(qtyCase.EngineRowCount > 0, "engine returned no rows for Quantity gt 0 — SQLite path not executing");
        }

        // ── SQL PARAMETERIZATION + COMPILE-CACHE PROBE ─────────────────────────────────────────
        // Two same-shape/different-literal cases: capture generated SQL, RECORD whether it is
        // parameterized (@p markers) and whether the compiled plan is shared (same SQL text).
        [Fact]
        public void Smoke_Parameterization_And_CompileCache()
        {
            using var factory = new TestDbFactory();
            var db = factory.Context;

            // Same shape, different literal — drives the literal through the engine's parameterized
            // LiteralAccess path (TypeCoercion.LiteralAccess emits @p, not an inlined constant).
            string sql10 = CaptureSql(db, "Quantity gt 10");
            string sql50 = CaptureSql(db, "Quantity gt 50");

            bool parameterized10 = sql10.Contains("@p", StringComparison.OrdinalIgnoreCase) || sql10.Contains("@__", StringComparison.OrdinalIgnoreCase);
            bool parameterized50 = sql50.Contains("@p", StringComparison.OrdinalIgnoreCase) || sql50.Contains("@__", StringComparison.OrdinalIgnoreCase);
            // Compile-cache hit signal: identical SQL text for the two literals means the same plan
            // is reused with only the parameter value differing.
            bool sameShape = string.Equals(StripParamValues(sql10), StripParamValues(sql50), StringComparison.Ordinal);

            output.WriteLine("SQL(Quantity gt 10):\n" + sql10);
            output.WriteLine("SQL(Quantity gt 50):\n" + sql50);
            output.WriteLine($"parameterized10={parameterized10} parameterized50={parameterized50} sameShape={sameShape}");

            // A harness self-check on the engine (distinct from the legacy differential): the SQL is
            // produced, literals are parameterized (@p), and same-shape queries share one plan — the
            // core invariant the shape cache depends on.
            Assert.False(string.IsNullOrWhiteSpace(sql10));
            Assert.False(string.IsNullOrWhiteSpace(sql50));
            Assert.True(parameterized10 && parameterized50, "engine must parameterize filter literals (@p) — the shape cache depends on it");
            Assert.True(sameShape, "same-shape/different-literal queries must share one SQL plan (only the parameter value differs)");
        }

        private static string CaptureSql(TestDbContext db, string filter)
        {
            var opts = new OdataQueryOptions<Model.Item>(new OdataQueryParts { Filter = filter });
            // Unpaged is IQueryable<Item> — supports EF's ToQueryString without executing.
            return opts.Apply(db.Set<Model.Item>()).Unpaged.ToQueryString();
        }

        private static string StripParamValues(string sql)
        {
            // Normalize parameter names so two different-literal queries with the same plan compare equal.
            return System.Text.RegularExpressions.Regex.Replace(sql, @"@(__)?p?\w*", "@param");
        }

        // ── FULL REPORT-GENERATION RUN ─────────────────────────────────────────────────────────
        // The B+A corpus run. Skipped by default per harness scope (the first full run is a
        // report-generation run done on demand, AFTER the oracle has written a complete golden.json).
        // Set RUN_FULL_DIFFERENTIAL=1 to emit the full divergence report. NEVER fails on divergence —
        // it writes the structured report and asserts only that every case produced a record.
        [Fact]
        public void FullCorpus_EmitDivergenceReport()
        {
            if (Environment.GetEnvironmentVariable("RUN_FULL_DIFFERENTIAL") != "1")
            {
                output.WriteLine("Skipped: set RUN_FULL_DIFFERENTIAL=1 to emit the full B+A divergence report.");
                return;
            }

            using var factory = new TestDbFactory();
            var db = factory.Context;
            var golden = LoadGolden();

            var report = Corpus.All.Select(c => EngineRunner.Run(c, db, golden)).ToList();

            foreach (var r in report)
                output.WriteLine($"[{r.Status}] {r.Label} eng={r.EngineRowCount} gold={r.GoldenRowCount} clientEval={r.ClientEvalFired} :: {r.Detail}");

            var byStatus = report.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
            output.WriteLine("=== SUMMARY ===");
            foreach (var (status, n) in byStatus.OrderBy(kv => kv.Key.ToString()))
                output.WriteLine($"{status}: {n}");

            // Persist the structured report alongside the golden for inspection.
            Directory.CreateDirectory("Fixtures");
            File.WriteAllText("Fixtures/divergence-report.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            Assert.Equal(Corpus.All.Count, report.Count);
        }
    }
}
