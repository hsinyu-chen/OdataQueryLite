using System.Collections.Generic;

namespace OdataQueryLite.Permissions
{
    /// <summary>
    /// Tree node describing the allowed <c>$expand</c> / <c>$select</c> surface at one level. Built by
    /// <see cref="AllowedExpandBuilder{TEntity}"/> and consumed by <see cref="ExpandSubsumption.IsAllowed"/>.
    /// </summary>
    public sealed class AllowedExpandNode
    {
        private HashSet<string>? _allowedSelectFields;
        private bool _explicitlyUnrestricted;

        /// <summary>Child nodes keyed by allowed navigation property name.</summary>
        public Dictionary<string, AllowedExpandNode> ExpandableProperties { get; } = [];

        /// <summary>
        /// Restricted field set, or <see langword="null"/> for "unrestricted" — the initial state and the
        /// state after <see cref="MarkSelectUnrestricted"/>.
        /// </summary>
        public IReadOnlyCollection<string>? AllowedSelectFields => _allowedSelectFields;

        /// <summary>
        /// Adds <paramref name="field"/> to the allowed-select set. Ignored once
        /// <see cref="MarkSelectUnrestricted"/> has been called, so a broader allow-rule cannot be narrowed
        /// by a subsequent scalar-leaf allow.
        /// </summary>
        /// <param name="field">Scalar property name to permit.</param>
        public void AddAllowedSelect(string field)
        {
            if (_explicitlyUnrestricted) return;
            _allowedSelectFields ??= [];
            _allowedSelectFields.Add(field);
        }

        /// <summary>Marks this node's select surface as unrestricted (clears any previously-added field list).</summary>
        public void MarkSelectUnrestricted()
        {
            _explicitlyUnrestricted = true;
            _allowedSelectFields = null;
        }

        /// <summary>Merges <paramref name="src"/> into <see langword="this"/>, recursing into children. "Broader wins" semantics apply to the unrestricted flag.</summary>
        /// <param name="src">Source tree to fold in.</param>
        public void MergeFrom(AllowedExpandNode src)
        {
            foreach (var (name, srcChild) in src.ExpandableProperties)
            {
                if (!ExpandableProperties.TryGetValue(name, out var destChild))
                {
                    destChild = new AllowedExpandNode();
                    ExpandableProperties[name] = destChild;
                }
                destChild.MergeFrom(srcChild);
            }
            if (src._explicitlyUnrestricted)
            {
                MarkSelectUnrestricted();
            }
            else if (src._allowedSelectFields != null)
            {
                foreach (var f in src._allowedSelectFields) AddAllowedSelect(f);
            }
        }
    }
}
