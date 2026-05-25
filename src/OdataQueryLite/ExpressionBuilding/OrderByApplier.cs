using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using OdataQueryLite.Ast;

namespace OdataQueryLite.ExpressionBuilding
{
    public static class OrderByApplier
    {
        // Builds `Queryable.OrderBy(x => x.Path).ThenBy(...)` from a parsed OrderByClause.
        // Goes through the Provider so EF Core sees a real Queryable.OrderBy call and can
        // translate to SQL ORDER BY — building an IOrderedEnumerable in memory would force
        // client-side sorting and break the parameterized-SQL contract.
        [RequiresUnreferencedCode("Resolves Queryable.OrderBy/ThenBy by name via Expression.Call.")]
        [RequiresDynamicCode("Constructs generic Func<T,TKey> lambdas whose TKey is only known at runtime.")]
        public static IQueryable<T> Apply<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            IQueryable<T> source, OrderByClause clause)
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
        [RequiresDynamicCode("Constructs Func<T, TKey> at runtime via Type.MakeGenericType.")]
        private static (LambdaExpression Lambda, Type KeyType) BuildKeySelector<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(MemberNode member)
        {
            var param = Expression.Parameter(typeof(T), "x");
            var body = MemberPathResolver.WalkPath(param, member.Path);
            var lambdaType = typeof(Func<,>).MakeGenericType(typeof(T), body.Type);
            return (Expression.Lambda(lambdaType, body, param), body.Type);
        }

    }
}
