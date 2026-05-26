using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OdataQueryLite.AspNetCore;
using Xunit;

namespace OdataQueryLite.AspNetCore.Tests
{
    // Pins the Minimal-API zero-boilerplate path: an endpoint can declare
    // OdataQueryRequest<T> as a parameter and the framework will call BindAsync without any
    // MVC controller or model-binder provider in play.
    public sealed class MinimalApiAppHost : IDisposable
    {
        private static readonly Item[] _items =
        [
            new() { Id = 1, Name = "Apple",  Price = 30 },
            new() { Id = 2, Name = "Banana", Price = 10 },
            new() { Id = 3, Name = "Cherry", Price = 50 },
            new() { Id = 4, Name = "Date",   Price = 20 },
            new() { Id = 5, Name = "Elder",  Price = 40 },
        ];

        private readonly IHost _host;
        public HttpClient Client { get; }

        public MinimalApiAppHost()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHost(b => b
                    .UseContentRoot(AppContext.BaseDirectory)
                    .UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddOdataQueryLite();
                        s.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseOdataQueryLite();
                        app.UseRouting();
                        app.UseEndpoints(e =>
                        {
                            e.MapGet("/items", (OdataQueryRequest<Item> q) =>
                            {
                                var result = q.Options.Apply(_items.AsQueryable());
                                return Results.Ok(new
                                {
                                    Total = result.Unpaged?.Cast<Item>().LongCount(),
                                    Data = result.Data.Cast<Item>().Select(i => new { i.Id, i.Name, i.Price }).ToList(),
                                });
                            });
                        });
                    }))
                .Build();
            _host.Start();
            Client = _host.GetTestClient();
        }

        public void Dispose()
        {
            Client.Dispose();
            _host.Dispose();
        }
    }

    public class MinimalApiTests(MinimalApiAppHost host) : IClassFixture<MinimalApiAppHost>
    {
        private readonly MinimalApiAppHost _host = host;

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
        public async Task Filter_top_count_bind_via_BindAsync()
        {
            var (status, body) = await GetAsync("/items?$filter=Price gt 15&$orderby=Price&$top=2&$count=true");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(4, body!.Value.GetProperty("total").GetInt64());
            var ids = body.Value.GetProperty("data").EnumerateArray()
                .Select(e => e.GetProperty("id").GetInt32()).ToArray();
            Assert.Equal(new[] { 4, 1 }, ids);
        }

        [Fact]
        public async Task Apply_rejected_through_middleware_in_minimal_api()
        {
            var (status, body) = await GetAsync("/items?$apply=groupby((Name))");
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("$apply", body!.Value.GetProperty("Option").GetString());
        }

        [Fact]
        public async Task Malformed_filter_returns_400_in_minimal_api()
        {
            var (status, _) = await GetAsync("/items?$filter=Price gt");
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }
    }
}
