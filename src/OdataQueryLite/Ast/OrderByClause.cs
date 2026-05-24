using System.Collections.Generic;

namespace OdataQueryLite.Ast
{
    public sealed record OrderByClause(IReadOnlyList<OrderByItem> Items);

    public sealed record OrderByItem(MemberNode Member, OrderByDirection Direction);

    public enum OrderByDirection { Ascending, Descending }
}
