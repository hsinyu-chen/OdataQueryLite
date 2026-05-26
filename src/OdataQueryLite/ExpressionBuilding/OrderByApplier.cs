using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using OdataQueryLite.Ast;

namespace OdataQueryLite.ExpressionBuilding
{
    /// <summary>
    /// Composes a parsed <see cref="OrderByClause"/> onto an <see cref="IQueryable{T}"/> as
    /// <c>OrderBy[Descending]</c> / <c>ThenBy[Descending]</c> calls.
    /// </summary>
    public static class OrderByApplier
    {
        /// <summary>
        /// Builds <c>Queryable.OrderBy(x =&gt; x.Path).ThenBy(...)</c> from <paramref name="clause"/>. Goes
        /// through the source's <c>Provider</c> so EF Core sees a real <c>Queryable.OrderBy</c> call and can
        /// translate to SQL <c>ORDER BY</c>.
        /// </summary>
        /// <typeparam name="T">Entity type.</typeparam>
        /// <param name="source">Input query.</param>
        /// <param name="clause">Parsed <c>$orderby</c>, or <see langword="null"/> / empty to skip ordering.</param>
        /// <returns>The ordered query; the original <paramref name="source"/> when <paramref name="clause"/> is empty.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="OdataQueryException">An item references a non-scalar property.</exception>
        [RequiresUnreferencedCode("Resolves Queryable.OrderBy/ThenBy by name via Expression.Call.")]
        [RequiresDynamicCode("Constructs generic Func<T,TKey> lambdas whose TKey is only known at runtime.")]
        public static IQueryable<T> Apply<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            IQueryable<T> source, OrderByClause? clause)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (clause is null || clause.Items.Count == 0) return source;

            Expression expr = source.Expression;
            for (int i = 0; i < clause.Items.Count; i++)
            {
                var item = clause.Items[i];
                var (lambda, keyType) = BuildKeySelector<T>(item.Member);
                var methodName = (i == 0)
                    ? (item.Direction == OrderByDirection.Descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy))
                    : (item.Direction == OrderByDirection.Descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy));
                expr = Expression.Call(typeof(Queryable), methodName, [typeof(T), keyType], expr, Expression.Quote(lambda));
            }
            return source.Provider.CreateQuery<T>(expr);
        }

        [RequiresUnreferencedCode("Resolves T's members by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Expression.Lambda + WalkPath build generic delegates / Expression.Call to Enumerable.Count<T> at runtime.")]
        private static (LambdaExpression Lambda, Type KeyType) BuildKeySelector<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(MemberNode member)
        {
            var param = Expression.Parameter(typeof(T), "x");
            var body = MemberPathResolver.WalkPath(param, member.Path);
            // OData v4.01 Part 2 §5.1.4: each $orderby expression MUST evaluate to a primitive
            // scalar type. Reject collections (`$orderby=Orders`) and complex objects
            // (`$orderby=Home` where Home is an Address class) — both would otherwise reach
            // EF Core and fail SQL translation as 500. string + byte[] count as primitives.
            var underlying = Nullable.GetUnderlyingType(body.Type) ?? body.Type;
            if (!underlying.IsValueType && underlying != typeof(string) && underlying != typeof(byte[]))
                throw new OdataQueryException(
                    $"Cannot $orderby a non-scalar property; saw '{string.Join('/', member.Path)}'. Use a scalar property or '$count' terminal.");
            // Expression.Lambda infers Func<T, TKey> from the body/param types — saves a
            // MakeGenericType reflection hop on every $orderby clause item.
            return (Expression.Lambda(body, param), body.Type);
        }

    }
}
