using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace OdataQueryLite.EFCore.Tests.Model
{
    /// <summary>
    /// EF Core context over the synthetic model. Enum-as-string conversions are configured here so
    /// the engine's enum coercion is exercised against real provider columns rather than in-memory
    /// CLR objects.
    /// </summary>
    public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Tag> Tags => Set<Tag>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Item>(e =>
            {
                e.HasKey(x => x.Id);
                // Seed assigns explicit Ids for deterministic golden — SQL Server would otherwise make
                // the key an IDENTITY column and reject explicit inserts (error 544). SQLite ignores it.
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Status).HasConversion<string>();
                e.Property(x => x.Priority).HasConversion<string>();
                e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId);
                e.HasOne(x => x.Category).WithMany(c => c.Items).HasForeignKey(x => x.CategoryId);
                e.HasMany(x => x.Tags).WithOne(t => t.Item).HasForeignKey(t => t.ItemId);
            });

            b.Entity<Category>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.Property(x => x.Kind).HasConversion<string>();
            });

            b.Entity<Tag>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
            });
        }
    }

    /// <summary>
    /// Builds a real, SQL-translating EF Core context for the harness — never the EF InMemory provider
    /// (which does zero SQL translation). The backend is chosen by <see cref="HarnessConfig.Provider"/>:
    /// SQLite in-memory (default), SQL Server LocalDB, or PostgreSQL. Applies the deterministic seed.
    /// Dispose tears the context down (and, for SQLite, closes the connection that owns the in-memory database).
    /// </summary>
    public sealed class TestDbFactory : IDisposable
    {
        private readonly SqliteConnection? _connection;

        public TestDbContext Context { get; }

        public TestDbFactory(Action<DbContextOptionsBuilder<TestDbContext>>? configure = null)
        {
            var builder = new DbContextOptionsBuilder<TestDbContext>();

            if (HarnessConfig.Provider == "localdb")
            {
                // SQL Server LocalDB translates date/math functions, DateTimeOffset ORDER BY, and native
                // decimal that SQLite cannot. EnsureDeleted+EnsureCreated gives a fresh deterministic DB
                // per construction so the oracle (Tier 3) and the engine run see identical seeded data.
                builder.UseSqlServer(@"Server=(localdb)\MSSQLLocalDB;Database=OdataDiffHarness;Trusted_Connection=True;TrustServerCertificate=True");
                configure?.Invoke(builder);
                Context = new TestDbContext(builder.Options);
                Context.Database.EnsureDeleted();
                Context.Database.EnsureCreated();
                TestSeed.Apply(Context);
            }
            else if (HarnessConfig.Provider == "postgres")
            {
                // Real PostgreSQL (connection string from the env, never committed). EnsureDeleted+
                // EnsureCreated gives a fresh deterministic DB per run — point it at a DEDICATED
                // database, never shared application data.
                var cs = HarnessConfig.PgConnectionString
                    ?? throw new InvalidOperationException(
                        "postgres provider selected but ODATA_HARNESS_PG_CONNSTRING is not set.");
                builder.UseNpgsql(cs);
                configure?.Invoke(builder);
                Context = new TestDbContext(builder.Options);
                Context.Database.EnsureDeleted();
                Context.Database.EnsureCreated();
                TestSeed.Apply(Context);
            }
            else
            {
                // ":memory:" databases live only as long as the connection is open, so the factory
                // owns the connection for the whole context lifetime rather than per-context.
                _connection = new SqliteConnection("DataSource=:memory:");
                _connection.Open();
                builder.UseSqlite(_connection);
                configure?.Invoke(builder);
                Context = new TestDbContext(builder.Options);
                Context.Database.EnsureCreated();
                TestSeed.Apply(Context);
            }
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection?.Dispose();
        }
    }
}
