using System.Collections.Generic;

namespace OdataQueryLite.Ast
{
    /// <summary>
    /// Parsed tree representation of a request-side <c>$expand</c> + <c>$select</c> combination.
    /// Each node corresponds to one entity reached by the expand path; <see cref="SelectedFields"/>
    /// scopes the scalar field set returned for that entity.
    /// </summary>
    public sealed class ExpandRequestNode
    {
        /// <summary>Child expansions keyed by navigation property name.</summary>
        public Dictionary<string, ExpandRequestNode> ExpandedProperties { get; } = [];

        /// <summary>
        /// Restricted field set for this node, or <see langword="null"/> when the caller did not specify
        /// <c>$select</c> here (meaning "no field restriction — return everything").
        /// </summary>
        public HashSet<string>? SelectedFields { get; set; }
    }
}
