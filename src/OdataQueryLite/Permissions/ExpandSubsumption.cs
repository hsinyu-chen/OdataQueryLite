using System;
using System.Linq;
using OdataQueryLite.Ast;

namespace OdataQueryLite.Permissions
{
    public static class ExpandSubsumption
    {
        public static bool IsAllowed(ExpandRequestNode request, AllowedExpandNode allowed)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allowed);

            if (request.SelectedFields != null
                && allowed.AllowedSelectFields != null
                && !request.SelectedFields.IsSubsetOf(allowed.AllowedSelectFields))
            {
                return false;
            }

            return request.ExpandedProperties.All(req =>
                allowed.ExpandableProperties.TryGetValue(req.Key, out var sub)
                && IsAllowed(req.Value, sub));
        }
    }
}
