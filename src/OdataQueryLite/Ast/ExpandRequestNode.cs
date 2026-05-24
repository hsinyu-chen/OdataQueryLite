using System.Collections.Generic;

namespace OdataQueryLite.Ast
{
    public sealed class ExpandRequestNode
    {
        public Dictionary<string, ExpandRequestNode> ExpandedProperties { get; } = [];

        // null = caller did not specify $select for this node (no field restriction)
        public HashSet<string> SelectedFields { get; set; }
    }
}
