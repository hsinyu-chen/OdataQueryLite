using System.Collections.Generic;
using System.Linq;
using OdataQueryLite.Ast;

namespace OdataQueryLite.Caching
{
    /// <summary>
    /// A reusable, literals-free compiled <c>$filter</c> over <typeparamref name="T"/>. One instance is
    /// produced per query shape (see <see cref="QueryShapeKey"/>) and rebinds its literal slots per call.
    /// </summary>
    /// <typeparam name="T">Entity type whose properties the filter references.</typeparam>
    public interface ICompiledQuery<T>
    {
        /// <summary>Composes the compiled <c>Where</c> onto <paramref name="source"/>.</summary>
        /// <param name="source">Input query.</param>
        /// <param name="literals">Literal values in the order matching the shape's slots.</param>
        /// <returns>The filtered query.</returns>
        IQueryable<T> Apply(IQueryable<T> source, IReadOnlyList<LiteralValue> literals);
    }
}
