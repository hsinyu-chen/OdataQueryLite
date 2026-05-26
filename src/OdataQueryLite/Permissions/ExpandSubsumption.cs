using System;
using System.Linq;
using OdataQueryLite.Ast;

namespace OdataQueryLite.Permissions
{
    /// <summary>
    /// Checks whether a request-side <see cref="ExpandRequestNode"/> tree is subsumed by a permission-side
    /// <see cref="AllowedExpandNode"/> tree.
    /// </summary>
    public static class ExpandSubsumption
    {
        /// <summary>
        /// Returns <see langword="true"/> when every expansion / selection in <paramref name="request"/> is
        /// covered by <paramref name="allowed"/>. A restricted allow-set requires the request to supply a
        /// <c>$select</c> whose fields are a subset — a missing <c>$select</c> would otherwise bypass the
        /// whitelist.
        /// </summary>
        /// <param name="request">Request tree from the parsed <c>$expand</c> + <c>$select</c>.</param>
        /// <param name="allowed">Permission tree built via <see cref="AllowedExpandBuilder{TEntity}"/>.</param>
        /// <returns><see langword="true"/> if the request is fully covered.</returns>
        /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
        public static bool IsAllowed(ExpandRequestNode request, AllowedExpandNode allowed)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allowed);

            // Restricted allowed side: request MUST $select (null $select means "give me everything",
            // which would bypass the whitelist) AND the requested fields must be a subset of allowed.
            if (allowed.AllowedSelectFields != null
                && (request.SelectedFields == null
                    || !request.SelectedFields.IsSubsetOf(allowed.AllowedSelectFields)))
            {
                return false;
            }

            return request.ExpandedProperties.All(req =>
                allowed.ExpandableProperties.TryGetValue(req.Key, out var sub)
                && IsAllowed(req.Value, sub));
        }
    }
}
