namespace OdataQueryLite
{
    /// <summary>
    /// Raw query options as parsed from a request URL (or set by hand for tests).
    /// </summary>
    /// <remarks>
    /// The AspNet binder fills this from <c>HttpRequest.Query</c>; the orchestrator takes a parts record so
    /// it stays usable outside ASP.NET (CLI tools, batch jobs).
    /// </remarks>
    public sealed record OdataQueryParts
    {
        /// <summary>Raw <c>$filter</c> expression, or <see langword="null"/> when the caller did not supply one.</summary>
        public string? Filter { get; init; }

        /// <summary>Raw <c>$orderby</c> expression, or <see langword="null"/> when the caller did not supply one.</summary>
        public string? OrderBy { get; init; }

        /// <summary>Raw <c>$expand</c> expression, or <see langword="null"/> when the caller did not supply one.</summary>
        public string? Expand { get; init; }

        /// <summary>Raw <c>$select</c> expression, or <see langword="null"/> when the caller did not supply one.</summary>
        public string? Select { get; init; }

        /// <summary>Raw <c>$top</c> value, or <see langword="null"/> when the caller did not supply one. Must be non-negative.</summary>
        public int? Top { get; init; }

        /// <summary>Raw <c>$skip</c> value, or <see langword="null"/> when the caller did not supply one. Must be non-negative.</summary>
        public int? Skip { get; init; }

        /// <summary>Whether the caller requested <c>$count=true</c>.</summary>
        public bool Count { get; init; }

        /// <summary>
        /// Raw <c>$apply</c> expression. Non-null/non-empty triggers <see cref="OdataQueryLite.Parsing.UnsupportedQueryOptionException"/>
        /// at <see cref="OdataQueryOptions{T}"/> construction — aggregation is out of scope.
        /// </summary>
        public string? Apply { get; init; }
    }
}
