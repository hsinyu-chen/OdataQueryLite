using System.Linq;

namespace OdataQueryLite
{
    public readonly record struct QueryResult(IQueryable Data, long? TotalCount);
}
