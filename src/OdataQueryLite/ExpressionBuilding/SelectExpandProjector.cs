using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using OdataQueryLite.Ast;

namespace OdataQueryLite.ExpressionBuilding
{
    /// <summary>
    /// Builds an <c>IQueryable&lt;Dictionary&lt;string, object?&gt;&gt;</c> projection from a parsed
    /// <see cref="ExpandRequestNode"/> tree. The projection composes onto any provider (EF Core,
    /// in-memory LINQ) without bespoke knowledge — EF Core 6+ translates
    /// <c>new Dictionary&lt;,&gt;(new[] { new KeyValuePair&lt;,&gt;(...) })</c> straight to a flat
    /// <c>SELECT col1, col2, ...</c> (single LEFT JOIN per ref nav, single denormalized LEFT JOIN
    /// per collection nav).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why dictionary, not anonymous type:</b> anonymous types require IL.Emit at runtime —
    /// reverses the engine's "no IL emit" stance. <c>Dictionary&lt;string, object?&gt;</c> serializes
    /// cleanly under both Newtonsoft and STJ with no <c>JsonConverter</c> plumbing.
    /// </para>
    /// <para>
    /// <b>Why <c>KeyValuePair</c> array ctor, not <c>{ [k] = v }</c> initializer:</b> the dictionary
    /// indexer initializer syntax sugar is rejected inside an Expression tree (CS8074 "An expression
    /// tree lambda may not contain a dictionary initializer"). The
    /// <c>IEnumerable&lt;KeyValuePair&lt;,&gt;&gt;</c> constructor produces an equivalent runtime
    /// shape and is Expression-tree legal.
    /// </para>
    /// <para>
    /// <b>Property visibility:</b> properties decorated with <see cref="OdataIgnoreAttribute"/>,
    /// <c>Newtonsoft.Json.JsonIgnoreAttribute</c>, or
    /// <c>System.Text.Json.Serialization.JsonIgnoreAttribute</c> are filtered out unconditionally —
    /// even when explicitly named in <c>$select</c>. The two JSON attributes are matched by full
    /// name reflection so the engine carries no NuGet dependency on either package.
    /// </para>
    /// </remarks>
    public static class SelectExpandProjector
    {
        private static readonly Type DictType = typeof(Dictionary<string, object?>);
        private static readonly Type KvpType = typeof(KeyValuePair<string, object?>);

        // Dictionary<,> and KeyValuePair<,> are concrete BCL types — the trimmer keeps their
        // constructors when we reference the typeof(...) above, so these GetConstructor calls
        // are safe to suppress.
        [UnconditionalSuppressMessage("Trimming", "IL2080:RequiresUnreferencedCode",
            Justification = "Dictionary<string, object?> ctor over IEnumerable<KeyValuePair<,>> is part of the BCL surface preserved by typeof().")]
        private static readonly ConstructorInfo DictFromKvpEnumerable =
            DictType.GetConstructor([typeof(IEnumerable<KeyValuePair<string, object?>>)])!;

        [UnconditionalSuppressMessage("Trimming", "IL2080:RequiresUnreferencedCode",
            Justification = "KeyValuePair<string, object?>(string, object) ctor is part of the BCL surface preserved by typeof().")]
        private static readonly ConstructorInfo KvpCtor =
            KvpType.GetConstructor([typeof(string), typeof(object)])!;

        /// <summary>
        /// Composes a dictionary projection onto <paramref name="source"/> matching
        /// <paramref name="node"/>. Returns <paramref name="source"/> unchanged (boxed to the
        /// non-generic <see cref="IQueryable"/>) when <paramref name="node"/> requests no
        /// restriction (null <see cref="ExpandRequestNode.SelectedFields"/> and empty
        /// <see cref="ExpandRequestNode.ExpandedProperties"/>).
        /// </summary>
        /// <typeparam name="T">Root entity type.</typeparam>
        /// <param name="source">The query to project from.</param>
        /// <param name="node">Parsed <c>$select</c> + <c>$expand</c> tree.</param>
        /// <returns>
        /// An <see cref="IQueryable"/> whose element type is either <typeparamref name="T"/> (when
        /// no projection is needed) or <see cref="Dictionary{TKey, TValue}"/> of
        /// <c>string</c>→<c>object?</c>.
        /// </returns>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        [RequiresUnreferencedCode("Resolves T's public properties by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Builds Expression<Func<T, Dictionary<string, object?>>> and calls Queryable.Select<T, Dictionary<string, object?>>.")]
        public static IQueryable Project<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
            IQueryable<T> source,
            ExpandRequestNode node)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(node);

            if (!RequiresProjection(node))
                return source;

            var param = Expression.Parameter(typeof(T), "x");
            var body = BuildDictionaryExpression(typeof(T), param, node);
            var lambda = Expression.Lambda<Func<T, Dictionary<string, object?>>>(body, param);
            return source.Select(lambda);
        }

        private static bool RequiresProjection(ExpandRequestNode node) =>
            node.SelectedFields is not null || node.ExpandedProperties.Count > 0;

        [RequiresUnreferencedCode("Resolves t's public properties by name.")]
        [RequiresDynamicCode("Builds nested generic Expression.Call to Enumerable.Select / Enumerable.ToList for collection navigations.")]
        private static NewExpression  BuildDictionaryExpression(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type t,
            Expression source,
            ExpandRequestNode node)
        {
            var kvps = BuildKeyValuePairs(t, source, node);
            // Empty $select=, empty $expand: produce empty dictionary rather than throwing —
            // server-side compatible with `$select=Ignored` where every named field is filtered.
            var array = Expression.NewArrayInit(KvpType, kvps);
            return Expression.New(DictFromKvpEnumerable, array);
        }

        [RequiresUnreferencedCode("Resolves t's public properties by name.")]
        [RequiresDynamicCode("Constructs Expression.Call to Enumerable.Select<TSource, Dictionary<,>> for collection navigations.")]
        private static List<Expression> BuildKeyValuePairs(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type t,
            Expression source,
            ExpandRequestNode node)
        {
            var kvps = new List<Expression>();
            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                // Skip indexers (`public object this[string key]`) — Expression.Property
                // requires an index argument and throws ArgumentException otherwise.
                if (prop.GetIndexParameters().Length > 0) continue;
                if (MemberPathResolver.IsIgnored(prop)) continue;

                var name = prop.Name;
                var isExpanded = node.ExpandedProperties.TryGetValue(name, out var childNode);
                var isScalarSelected = node.SelectedFields is null
                    ? !IsNavigationLike(prop.PropertyType)
                    : node.SelectedFields.Contains(name);

                Expression? value = null;
                if (isExpanded)
                {
                    value = BuildExpandedValue(prop, source, childNode!);
                }
                else if (isScalarSelected && !IsNavigationLike(prop.PropertyType))
                {
                    value = Expression.Property(source, prop);
                }

                if (value is null) continue;

                // KeyValuePair<string, object?> takes (string, object). Value types must box.
                if (value.Type.IsValueType)
                    value = Expression.Convert(value, typeof(object));

                kvps.Add(Expression.New(KvpCtor, Expression.Constant(name), value));
            }
            return kvps;
        }

        [RequiresUnreferencedCode("Resolves the navigation element type's public properties by name.")]
        [RequiresDynamicCode("Builds generic Enumerable.Select / Enumerable.ToList calls.")]
        private static Expression BuildExpandedValue(PropertyInfo prop, Expression source, ExpandRequestNode childNode)
        {
            var propType = prop.PropertyType;
            var memberAccess = Expression.Property(source, prop);

            var elementType = MemberPathResolver.GetEnumerableElementType(propType);
            if (elementType is not null)
            {
                // Collection navigation: x.Items.Select(i => dict).ToList()
                var itemParam = Expression.Parameter(elementType, "i");
                var itemDict = BuildDictionaryExpression(elementType, itemParam, childNode);
                var itemLambda = Expression.Lambda(itemDict, itemParam);

                // Enumerable.Select<TSource, TResult>(IEnumerable<TSource>, Func<TSource, TResult>)
                var selectCall = Expression.Call(
                    typeof(Enumerable),
                    nameof(Enumerable.Select),
                    [elementType, DictType],
                    memberAccess,
                    itemLambda);

                // Enumerable.ToList<Dictionary<,>>(IEnumerable<Dictionary<,>>)
                return Expression.Call(
                    typeof(Enumerable),
                    nameof(Enumerable.ToList),
                    [DictType],
                    selectCall);
            }

            // Reference navigation: x.Customer == null ? null : new Dictionary<>(...).
            // EF Core translates this conditional cleanly (CASE WHEN [c].[Id] IS NULL THEN NULL ...
            // over the LEFT JOIN), and in-memory LINQ now also handles null navs without NRE.
            var dictExpr = BuildDictionaryExpression(propType, memberAccess, childNode);
            var nullDict = Expression.Constant(null, DictType);
            var isNull = Expression.Equal(memberAccess, Expression.Constant(null, memberAccess.Type));
            return Expression.Condition(isNull, nullDict, dictExpr);
        }

        private static bool IsNavigationLike(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type t)
        {
            // string + byte[] are scalar (OData primitives). All other IEnumerable<T>, class
            // types, and interface types (covers `public ICustomer Customer { get; set; }`
            // pattern) count as navigation: $select on them in absence of $expand has no
            // defined semantics here, so we omit by default.
            if (t == typeof(string) || t == typeof(byte[])) return false;
            if (MemberPathResolver.GetEnumerableElementType(t) is not null) return true;
            if (t.IsClass || t.IsInterface) return true;
            return false;
        }
    }
}
