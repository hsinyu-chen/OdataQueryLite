using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OdataQueryLite.Permissions
{
    /// <summary>
    /// Fluent builder that produces an <see cref="AllowedExpandNode"/> tree describing the navigation /
    /// scalar properties a client is permitted to <c>$select</c> or <c>$expand</c>. Consumed by
    /// <see cref="ExpandSubsumption.IsAllowed"/> at request time.
    /// </summary>
    /// <typeparam name="TEntity">Root entity type whose properties this builder enumerates.</typeparam>
    public sealed class AllowedExpandBuilder<TEntity>
    {
        private readonly AllowedExpandNode _root = new();

        /// <summary>
        /// Allows a property-access chain ending in either a scalar (adds to the parent node's allowed-select
        /// set) or a navigation property (marks the leaf node as unrestricted-select).
        /// </summary>
        /// <typeparam name="TChild">Property type at the end of the chain.</typeparam>
        /// <param name="selector">Property-access chain rooted at the entity parameter, e.g. <c>x =&gt; x.Customer.Name</c>.</param>
        /// <returns>The same builder for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The selector body is not a property-access chain.</exception>
        public AllowedExpandBuilder<TEntity> AllowExpand<TChild>(Expression<Func<TEntity, TChild>> selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            var path = PropertyAccessVisitor.ExtractPath(selector);
            EnsureNonEmpty(path);

            var cursor = Navigate(_root, path, 0, path.Count - 1);
            var leaf = path[^1];
            if (IsNavigation(leaf.PropertyType))
            {
                var leafNode = GetOrAddChild(cursor, leaf.Name);
                leafNode.MarkSelectUnrestricted();
            }
            else
            {
                cursor.AddAllowedSelect(leaf.Name);
            }
            return this;
        }

        /// <summary>
        /// Allows a collection navigation property and recursively configures the per-element rules through
        /// <paramref name="configureChild"/>. The two-argument overload is required for collection
        /// navigations because the single-argument <c>AllowExpand</c> can't represent per-element scope.
        /// </summary>
        /// <typeparam name="TChild">Element type of the collection.</typeparam>
        /// <param name="collectionSelector">Property-access chain ending at a collection navigation.</param>
        /// <param name="configureChild">Callback that configures the rules applied to each element.</param>
        /// <returns>The same builder for fluent chaining.</returns>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        public AllowedExpandBuilder<TEntity> AllowExpand<TChild>(
            Expression<Func<TEntity, IEnumerable<TChild>>> collectionSelector,
            Action<AllowedExpandBuilder<TChild>> configureChild)
        {
            ArgumentNullException.ThrowIfNull(collectionSelector);
            ArgumentNullException.ThrowIfNull(configureChild);

            var path = PropertyAccessVisitor.ExtractPath(collectionSelector);
            EnsureNonEmpty(path);
            var collectionNode = Navigate(_root, path, 0, path.Count);

            var childBuilder = new AllowedExpandBuilder<TChild>();
            configureChild(childBuilder);
            collectionNode.MergeFrom(childBuilder._root);
            return this;
        }

        /// <summary>Returns the accumulated allow-tree.</summary>
        /// <returns>The root <see cref="AllowedExpandNode"/>.</returns>
        public AllowedExpandNode Build() => _root;

        private static AllowedExpandNode Navigate(AllowedExpandNode start, IReadOnlyList<PropertyInfo> path, int from, int toExclusive)
        {
            var cursor = start;
            for (int i = from; i < toExclusive; i++)
            {
                cursor = GetOrAddChild(cursor, path[i].Name);
            }
            return cursor;
        }

        private static AllowedExpandNode GetOrAddChild(AllowedExpandNode node, string name)
        {
            if (!node.ExpandableProperties.TryGetValue(name, out var child))
            {
                child = new AllowedExpandNode();
                node.ExpandableProperties[name] = child;
            }
            return child;
        }

        // OData structural property (scalar) vs navigation:
        // - string / value type / Nullable<T> are scalar.
        // - Collections / arrays inherit from their element type (List<int> is scalar, ICollection<Customer> is navigation).
        // - Everything else (reference types) is navigation.
        private static bool IsNavigation(Type t)
        {
            var inner = Nullable.GetUnderlyingType(t) ?? t;
            if (inner == typeof(string) || inner.IsValueType) return false;

            var element = GetCollectionElementType(inner);
            return element == null || IsNavigation(element);
        }

        // Detect element type without GetInterfaces() — covers arrays and the common BCL collection
        // generic definitions used by EF Core navigation collections. AOT-clean (no member discovery).
        // Custom collection types that don't inherit from a recognised generic def fall through to
        // null → treated as navigation; register them via the explicit collection overload instead.
        private static Type? GetCollectionElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(IEnumerable<>) || def == typeof(ICollection<>)
                    || def == typeof(IList<>) || def == typeof(IReadOnlyCollection<>)
                    || def == typeof(IReadOnlyList<>) || def == typeof(List<>)
                    || def == typeof(HashSet<>))
                {
                    return t.GetGenericArguments()[0];
                }
            }
            return null;
        }

        private static void EnsureNonEmpty(IReadOnlyList<PropertyInfo> path)
        {
            if (path.Count == 0)
                throw new ArgumentException("Selector did not yield any property access path.");
        }
    }
}
