using System.Collections.Generic;

namespace OdataQueryLite.Permissions
{
    public sealed class AllowedExpandNode
    {
        public Dictionary<string, AllowedExpandNode> ExpandableProperties { get; } = [];

        // null 代表「未限制 select」— 等同允許全部欄位
        public HashSet<string> AllowedSelectFields { get; set; }
    }
}
