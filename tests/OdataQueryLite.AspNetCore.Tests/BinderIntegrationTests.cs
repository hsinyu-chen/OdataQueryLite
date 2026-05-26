using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace OdataQueryLite.AspNetCore.Tests
{
    public class BinderIntegrationTests(TestAppHost host) : IClassFixture<TestAppHost>
    {
        private readonly TestAppHost _host = host;

        private async Task<(HttpStatusCode Status, JsonElement? Body)> GetAsync(string url)
        {
            var res = await _host.Client.GetAsync(url);
            if (!res.IsSuccessStatusCode)
            {
                var raw = await res.Content.ReadAsStringAsync();
                return (res.StatusCode, string.IsNullOrEmpty(raw) ? null : JsonDocument.Parse(raw).RootElement.Clone());
            }
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
            return (res.StatusCode, doc);
        }

        [Fact]
        public async Task No_query_returns_all_rows()
        {
            var (status, body) = await GetAsync("/items");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(5, body!.Value.GetProperty("Data").GetArrayLength());
        }

        [Fact]
        public async Task Filter_binds_and_applies()
        {
            var (status, body) = await GetAsync("/items?$filter=Price gt 25");
            Assert.Equal(HttpStatusCode.OK, status);
            var ids = body!.Value.GetProperty("Data").EnumerateArray()
                .Select(e => e.GetProperty("Id").GetInt32()).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { 1, 3, 5 }, ids);
        }

        [Fact]
        public async Task OrderBy_top_skip_count_compose()
        {
            var (status, body) = await GetAsync("/items?$orderby=Price desc&$skip=1&$top=2&$count=true");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(5, body!.Value.GetProperty("Total").GetInt64());
            var ids = body.Value.GetProperty("Data").EnumerateArray()
                .Select(e => e.GetProperty("Id").GetInt32()).ToArray();
            Assert.Equal(new[] { 5, 1 }, ids);
        }

        [Fact]
        public async Task Dollar_apply_returns_400_with_option_name()
        {
            var (status, body) = await GetAsync("/items?$apply=groupby((Name))");
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("$apply", body!.Value.GetProperty("Option").GetString());
        }

        [Fact]
        public async Task Negative_top_returns_400()
        {
            var (status, body) = await GetAsync("/items?$top=-1");
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("$top", body!.Value.GetProperty("Message").GetString());
        }

        [Fact]
        public async Task Malformed_filter_returns_400()
        {
            var (status, body) = await GetAsync("/items?$filter=Price gt");
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.NotNull(body!.Value.GetProperty("Message").GetString());
        }

        [Fact]
        public async Task Top_non_integer_returns_400()
        {
            var (status, _) = await GetAsync("/items?$top=abc");
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }

        [Fact]
        public async Task Count_invalid_returns_400()
        {
            var (status, _) = await GetAsync("/items?$count=maybe");
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }
    }
}
