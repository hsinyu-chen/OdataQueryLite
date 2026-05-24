using System.Collections.Generic;
using System.Linq;
using OdataQueryLite.Ast;

namespace OdataQueryLite.Caching
{
    public interface ICompiledQuery<T>
    {
        IQueryable<T> Apply(IQueryable<T> source, IReadOnlyList<LiteralValue> literals);
    }
}
