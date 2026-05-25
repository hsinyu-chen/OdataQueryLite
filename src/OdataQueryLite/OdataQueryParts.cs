namespace OdataQueryLite
{
    // Raw query options as parsed from a request URL (or set by hand for tests).
    // The AspNet binder fills this from HttpRequest.Query (Phase 1.B.12); the orchestrator
    // takes a parts record so it stays usable outside ASP.NET (CLI tools, batch jobs).
    public sealed record OdataQueryParts
    {
        public string Filter { get; init; }
        public string OrderBy { get; init; }
        public string Expand { get; init; }
        public string Select { get; init; }
        public int? Top { get; init; }
        public int? Skip { get; init; }
        public bool Count { get; init; }
        // Non-null/non-empty triggers UnsupportedQueryOptionException at construction —
        // aggregation is out of scope for Phase 1.
        public string Apply { get; init; }
    }
}
