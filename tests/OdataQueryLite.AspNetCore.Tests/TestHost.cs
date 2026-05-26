using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OdataQueryLite.AspNetCore;

namespace OdataQueryLite.AspNetCore.Tests
{
    public sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
    }

    [ApiController]
    [Route("items")]
    public sealed class ItemsController : ControllerBase
    {
        private static readonly IQueryable<Item> _items = new[]
        {
            new Item { Id = 1, Name = "Apple",  Price = 30 },
            new Item { Id = 2, Name = "Banana", Price = 10 },
            new Item { Id = 3, Name = "Cherry", Price = 50 },
            new Item { Id = 4, Name = "Date",   Price = 20 },
            new Item { Id = 5, Name = "Elder",  Price = 40 },
        }.AsQueryable();

        [HttpGet]
        public IActionResult Get(OdataQueryOptions<Item> q)
        {
            var result = q.Apply(_items);
            return Ok(new
            {
                Total = result.TotalCount,
                Data = result.Data.Cast<Item>().Select(i => new { i.Id, i.Name, i.Price }).ToList(),
            });
        }
    }

    // Standalone TestServer host. Bypasses WebApplicationFactory because the latter probes
    // for a project directory matching TEntryPoint assembly name (fails for an in-test
    // Startup class) before any UseContentRoot override can take effect.
    public sealed class TestAppHost : IDisposable
    {
        private readonly IHost _host;
        public HttpClient Client { get; }

        public TestAppHost()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHost(b => b
                    .UseContentRoot(AppContext.BaseDirectory)
                    .UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddOdataQueryLite();
                        s.AddControllers()
                            .AddApplicationPart(typeof(TestAppHost).Assembly)
                            .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = null);
                    })
                    .Configure(app =>
                    {
                        app.UseOdataQueryLite();
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapControllers());
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
}
