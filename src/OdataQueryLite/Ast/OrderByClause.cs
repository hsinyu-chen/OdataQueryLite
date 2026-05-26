using System.Collections.Generic;

namespace OdataQueryLite.Ast
{
    /// <summary>A parsed <c>$orderby</c> clause — an ordered list of (member, direction) items.</summary>
    /// <param name="Items">Sort items in their as-written order; first item is the primary sort key.</param>
    public sealed record OrderByClause(IReadOnlyList<OrderByItem> Items);

    /// <summary>A single sort key from a <c>$orderby</c> clause.</summary>
    /// <param name="Member">Member path (and optional <c>$count</c> terminal) to sort by.</param>
    /// <param name="Direction">Sort direction.</param>
    public sealed record OrderByItem(MemberNode Member, OrderByDirection Direction);

    /// <summary>Sort direction for an <see cref="OrderByItem"/>.</summary>
    public enum OrderByDirection
    {
        /// <summary><c>asc</c> — ascending (default when neither suffix is given).</summary>
        Ascending,
        /// <summary><c>desc</c> — descending.</summary>
        Descending
    }
}
