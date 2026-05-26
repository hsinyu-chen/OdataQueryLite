using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace OdataQueryLite.ExpressionBuilding
{
    // Shared between FilterExpressionBuilder and OrderByApplier so the OData semantic surface
    // (member resolution error messages + collection-element-type detection used by `$count`)
    // is identical across $filter and $orderby. Diverging messages would surface as
    // "different exception in $orderby vs $filter for the same broken request" — a user-error
    // class that must produce the same diagnostic regardless of which $-option triggered it.
    internal static class MemberPathResolver
    {
        // Walks the implemented interfaces to find IEnumerable<T> and return T. Accepts any
        // type implementing IEnumerable<T> (including third-party collections), with string
        // explicitly excluded — string implements IEnumerable<char> but `$count("Name")` would
        // return character count, not a collection cardinality, surprising callers.
        // DynamicallyAccessedMembers(Interfaces) propagates the trim requirement: callers must
        // hand in a Type whose interface metadata the trimmer preserved. EF Core navigation
        // property types satisfy this because they're reachable from the entity class which
        // already carries [DynamicallyAccessedMembers(PublicProperties)] up at the engine entry.
        public static Type? GetEnumerableElementType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type t)
        {
            // string + byte[] are OData primitive types (Edm.String / Edm.Binary), not collections.
            // string implements IEnumerable<char>; byte[] is an array of primitives. Treating
            // either as a collection would reject `$orderby=Name` (the round-3 collection-orderby
            // guard) and `$orderby=RowVersion` (byte[] concurrency tokens).
            if (t == typeof(string) || t == typeof(byte[])) return null;
            // Multi-dimensional arrays (int[,]) report IsArray=true but don't implement
            // IEnumerable<T> — accepting the rank-1 element type would compile Enumerable.Count<T>
            // and explode as 500 at execution. Only single-dimensional arrays are valid OData
            // collections. Multi-dim falls through to GetInterfaces() which finds nothing
            // generic and returns null, yielding a clean 400.
            if (t.IsArray && t.GetArrayRank() == 1) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return t.GetGenericArguments()[0];
            foreach (var iface in t.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }
            return null;
        }

        // Walks `path[startIndex..]` from `root`, dereferencing each segment as a public
        // property and translating a terminal `$count` segment into Enumerable.Count<T>().
        // Identical semantics + error messages from $filter and $orderby — the whole point
        // of this helper class.
        [RequiresUnreferencedCode("Resolves T's members by name; T's properties must be preserved by the trimmer.")]
        [RequiresDynamicCode("Builds Expression.Call to generic Enumerable.Count<T> at runtime for $count terminal segments.")]
        public static Expression WalkPath(Expression root, IReadOnlyList<string> path, int startIndex = 0)
        {
            var cursor = root;
            for (int i = startIndex; i < path.Count; i++)
            {
                var seg = path[i];
                if (seg == "$count")
                {
                    if (i != path.Count - 1)
                        throw new OdataQueryException($"$count must be the terminal segment; saw '{string.Join('/', path)}'.");
                    var elem = GetEnumerableElementType(cursor.Type)
                        ?? throw new OdataQueryException($"$count target is not enumerable: {cursor.Type.Name}");
                    return Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [elem], cursor);
                }
                cursor = Expression.Property(cursor, ResolveProperty(cursor.Type, seg));
            }
            return cursor;
        }

        public static PropertyInfo ResolveProperty(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type t,
            string name)
        {
            var pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            // Treat ignored properties as if they didn't exist — same error message so callers
            // can't discriminate "wrong name" from "hidden by [JsonIgnore]/[OdataIgnore]" and
            // mount boolean probes like `$filter=startswith(Password, 's')` against them. The
            // Available list is filtered for the same reason.
            if (pi != null && !IsIgnored(pi)) return pi;
            var available = string.Join(", ",
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => !IsIgnored(p))
                    .Select(p => p.Name));
            throw new OdataQueryException(
                $"Property '{name}' not found on type '{t.Name}'. Available: {available}.");
        }

        /// <summary>
        /// Whether <paramref name="prop"/> is hidden from OData via
        /// <see cref="OdataIgnoreAttribute"/>, <c>Newtonsoft.Json.JsonIgnoreAttribute</c>, or
        /// <c>System.Text.Json.Serialization.JsonIgnoreAttribute</c>. The JSON attributes are
        /// matched by full name so the engine carries no NuGet dependency on either package.
        /// Hidden properties are unreachable from any <c>$</c>-option — <c>$select</c>,
        /// <c>$expand</c>, <c>$filter</c>, and <c>$orderby</c> all reject them, surfacing the
        /// same "not found" diagnostic as a misspelled property to deny attackers a way to
        /// discriminate hidden-but-present from absent.
        /// </summary>
        public static bool IsIgnored(PropertyInfo prop) =>
            IgnoredCache.GetOrAdd(prop, static p =>
            {
                foreach (var attr in p.GetCustomAttributes(inherit: true))
                {
                    // Cheap concrete-type check first; FullName allocation only on miss.
                    if (attr is OdataIgnoreAttribute) return true;
                    var fullName = attr.GetType().FullName;
                    if (fullName == NewtonsoftJsonIgnoreFullName) return true;
                    if (fullName == SystemTextJsonIgnoreFullName) return true;
                }
                return false;
            });

        // PropertyInfo is reflection-stable per (DeclaringType, Name); equality is structural,
        // so the cache survives even if the BCL returns distinct PropertyInfo instances across
        // calls. Filter/orderby/projection all hit IsIgnored on every property of T, so the
        // cache turns N reflection probes per request into N once-per-process.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<PropertyInfo, bool> IgnoredCache = new();

        private const string NewtonsoftJsonIgnoreFullName = "Newtonsoft.Json.JsonIgnoreAttribute";
        private const string SystemTextJsonIgnoreFullName = "System.Text.Json.Serialization.JsonIgnoreAttribute";
    }
}
