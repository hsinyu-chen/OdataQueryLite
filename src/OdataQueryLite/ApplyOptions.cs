namespace OdataQueryLite
{
    /// <summary>
    /// Host-side toggles that suppress individual transformation stages inside
    /// <see cref="OdataQueryOptions{T}.Apply(System.Linq.IQueryable{T}, IApplyOptions?)"/>.
    /// Useful when a host wants to compose paging / ordering / projection itself for some
    /// callsites while still using the OData parse + filter pipeline.
    /// </summary>
    public interface IApplyOptions
    {
        /// <summary>Apply <c>$top</c> / <c>$skip</c> to the result. Default true.</summary>
        bool Paging { get; }

        /// <summary>Apply <c>$orderby</c> to the result. Default true.</summary>
        bool OrderBy { get; }

        /// <summary>
        /// Reserved for a future <c>$select</c> / <c>$expand</c> projection stage.
        /// Currently unused by the engine — the parsed expand tree is exposed via
        /// <see cref="OdataQueryOptions{T}.Expand"/> for whitelist validation only.
        /// </summary>
        bool SelectExpand { get; }
    }

    /// <summary>Default <see cref="IApplyOptions"/> with every stage enabled.</summary>
    public sealed class ApplyOptions : IApplyOptions
    {
        /// <inheritdoc />
        public bool Paging { get; set; } = true;

        /// <inheritdoc />
        public bool OrderBy { get; set; } = true;

        /// <inheritdoc />
        public bool SelectExpand { get; set; } = true;

        /// <summary>Fluent setter for <see cref="Paging"/>.</summary>
        public ApplyOptions ApplyPaging(bool value) { Paging = value; return this; }

        /// <summary>Fluent setter for <see cref="OrderBy"/>.</summary>
        public ApplyOptions ApplyOrderBy(bool value) { OrderBy = value; return this; }

        /// <summary>Fluent setter for <see cref="SelectExpand"/>.</summary>
        public ApplyOptions ApplySelectExpand(bool value) { SelectExpand = value; return this; }
    }
}
