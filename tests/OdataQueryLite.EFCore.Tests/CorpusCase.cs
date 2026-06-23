namespace OdataQueryLite.EFCore.Tests
{
    /// <summary>What the engine is expected to do with a case, independent of the golden row-set.</summary>
    public enum ExpectKind
    {
        /// <summary>Normal case: engine produces a row-set to be compared against the golden oracle.</summary>
        Rows,

        /// <summary>
        /// Engine MUST reject the option (throws -> HTTP 400). Covers $apply and a $select that names a
        /// hidden [JsonIgnore] property (the engine's hidden-property guard).
        /// </summary>
        Reject400,
    }

    /// <summary>
    /// One labelled corpus case. Expressed ONLY against the synthetic model field names. The same
    /// instances feed both the engine runner (Tier 2) and the legacy oracle (Tier 3) so the two
    /// products can never drift in what they test.
    /// </summary>
    public sealed record CorpusCase
    {
        public required string Label { get; init; }
        public required string Group { get; init; } // "A" or "B"

        public string? Filter { get; init; }
        public string? OrderBy { get; init; }
        public string? Expand { get; init; }
        public string? Select { get; init; }
        public int? Top { get; init; }
        public int? Skip { get; init; }
        public bool Count { get; init; }
        public string? Apply { get; init; }

        public ExpectKind Expect { get; init; } = ExpectKind.Rows;

        /// <summary>Free-form note — used to record WHY a Gap case is a gap, for the report.</summary>
        public string? Note { get; init; }
    }
}
