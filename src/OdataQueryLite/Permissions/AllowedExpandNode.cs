using System.Collections.Generic;

namespace OdataQueryLite.Permissions
{
    public sealed class AllowedExpandNode
    {
        private HashSet<string> _allowedSelectFields;
        private bool _explicitlyUnrestricted;

        public Dictionary<string, AllowedExpandNode> ExpandableProperties { get; } = [];

        // null = unrestricted (default + after MarkSelectUnrestricted); non-null = restricted to this set.
        public IReadOnlyCollection<string> AllowedSelectFields => _allowedSelectFields;

        // Once MarkSelectUnrestricted is called, scalar-leaf AllowExpand additions on this node are ignored —
        // ensures `AllowExpand(x => x.Customer)` and `AllowExpand(x => x.Customer.Name)` produce the same
        // result regardless of order ("broader wins").
        public void AddAllowedSelect(string field)
        {
            if (_explicitlyUnrestricted) return;
            _allowedSelectFields ??= [];
            _allowedSelectFields.Add(field);
        }

        public void MarkSelectUnrestricted()
        {
            _explicitlyUnrestricted = true;
            _allowedSelectFields = null;
        }

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
