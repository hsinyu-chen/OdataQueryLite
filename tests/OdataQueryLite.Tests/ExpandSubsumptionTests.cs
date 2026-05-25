using System.Linq;
using OdataQueryLite.Ast;
using OdataQueryLite.Permissions;
using Xunit;

namespace OdataQueryLite.Tests
{
    public class ExpandSubsumptionTests
    {
        private static ExpandRequestNode Req(params (string name, ExpandRequestNode child)[] children)
        {
            var n = new ExpandRequestNode();
            foreach (var (name, child) in children) n.ExpandedProperties[name] = child;
            return n;
        }

        private static AllowedExpandNode Allow(params (string name, AllowedExpandNode child)[] children)
        {
            var n = new AllowedExpandNode();
            foreach (var (name, child) in children) n.ExpandableProperties[name] = child;
            return n;
        }

        [Fact]
        public void Empty_request_is_always_allowed()
        {
            Assert.True(ExpandSubsumption.IsAllowed(new ExpandRequestNode(), new AllowedExpandNode()));
        }

        [Fact]
        public void Request_matching_allowed_tree_is_allowed()
        {
            var request = Req(("Customer", Req(("Orders", new ExpandRequestNode()))));
            var allowed = Allow(("Customer", Allow(("Orders", new AllowedExpandNode()))));
            Assert.True(ExpandSubsumption.IsAllowed(request, allowed));
        }

        [Fact]
        public void Request_expanding_unallowed_property_is_rejected()
        {
            var request = Req(("Secret", new ExpandRequestNode()));
            var allowed = Allow(("Customer", new AllowedExpandNode()));
            Assert.False(ExpandSubsumption.IsAllowed(request, allowed));
        }

        [Fact]
        public void Request_selecting_field_outside_allowed_select_is_rejected()
        {
            var request = new ExpandRequestNode { SelectedFields = ["Email"] };
            var allowed = new AllowedExpandNode();
            allowed.AddAllowedSelect("Name");
            Assert.False(ExpandSubsumption.IsAllowed(request, allowed));
        }

        [Fact]
        public void Request_select_against_unrestricted_allowed_is_allowed()
        {
            var request = new ExpandRequestNode { SelectedFields = ["Email", "Phone"] };
            var allowed = new AllowedExpandNode(); // AllowedSelectFields == null → unrestricted
            Assert.True(ExpandSubsumption.IsAllowed(request, allowed));
        }

        [Fact]
        public void Nested_select_violation_rejects_whole_tree()
        {
            var nestedAllowed = new AllowedExpandNode();
            nestedAllowed.AddAllowedSelect("Name");
            var request = Req(("Customer", new ExpandRequestNode { SelectedFields = ["Salary"] }));
            var allowed = Allow(("Customer", nestedAllowed));
            Assert.False(ExpandSubsumption.IsAllowed(request, allowed));
        }

        [Fact]
        public void Select_subset_is_allowed()
        {
            var request = new ExpandRequestNode { SelectedFields = ["Name"] };
            var allowed = new AllowedExpandNode();
            allowed.AddAllowedSelect("Name");
            allowed.AddAllowedSelect("Email");
            Assert.True(ExpandSubsumption.IsAllowed(request, allowed));
        }

        [Fact]
        public void Request_without_select_against_restricted_allowed_is_rejected()
        {
            // A restricted allow side requires the request to explicitly $select — a missing
            // $select means "return every field", which would violate the whitelist.
            var request = new ExpandRequestNode(); // SelectedFields == null
            var allowed = new AllowedExpandNode();
            allowed.AddAllowedSelect("Name");
            Assert.False(ExpandSubsumption.IsAllowed(request, allowed));
        }
    }
}
