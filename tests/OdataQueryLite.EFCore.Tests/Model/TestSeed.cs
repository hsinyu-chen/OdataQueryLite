using Microsoft.EntityFrameworkCore;

namespace OdataQueryLite.EFCore.Tests.Model
{
    /// <summary>
    /// Deterministic seed shared by BOTH the engine runner (Tier 2) and the legacy oracle (Tier 3).
    /// No Random / DateTime.Now — every value is a fixed literal so the two products see byte-identical
    /// data. Designed so every corpus filter has both matching and non-matching rows, every nullable
    /// field has null and non-null rows, each enum value appears, Tag collections vary in size
    /// (0, 1, 3), a self-ref Parent chain exists, and at least one string needs apostrophe escaping.
    /// </summary>
    public static class TestSeed
    {
        // All timestamps are explicit UTC (DateTimeKind/offset known) — the engine rejects
        // Unspecified-kind dates, and OData v4 date literals require Z/offset.
        private static DateTimeOffset T(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
            => new(year, month, day, hour, minute, second, TimeSpan.Zero);

        public static void Apply(TestDbContext ctx)
        {
            // Idempotent: a freshly EnsureCreated db is empty, but guard anyway so a caller that
            // reuses a context can't double-seed.
            if (ctx.Items.Any()) return;

            var catA = new Category { Id = 1, Name = "Alpha", Kind = Priority.Low };
            var catB = new Category { Id = 2, Name = "Beta", Kind = Priority.Medium };
            var catC = new Category { Id = 3, Name = "O'Hara", Kind = Priority.High }; // apostrophe in a ref-nav string
            ctx.Categories.AddRange(catA, catB, catC);

            // Item 1: root of a self-ref chain (no parent), Active, has 3 tags, non-null nullables.
            var i1 = new Item
            {
                Id = 1,
                Name = "Widget",
                Code = "AAA-001",
                Quantity = 10,
                Price = 19.99m,
                IsActive = true,
                IsArchived = false,
                CreatedTime = T(2024, 1, 15, 9, 30, 0),
                ClosedTime = T(2024, 6, 1, 12, 0, 0),
                Status = Status.Active,
                Priority = Priority.High,
                Secret = "s1",
                ParentId = null,
                CategoryId = 1,
            };

            // Item 2: child of Item 1 (self-ref), Pending, 1 tag, IsArchived null, Priority null,
            // ClosedTime null — exercises the null side of every nullable.
            var i2 = new Item
            {
                Id = 2,
                Name = "Gadget",
                Code = "BBB-002",
                Quantity = 0,
                Price = 100.00m,
                IsActive = false,
                IsArchived = null,
                CreatedTime = T(2024, 3, 20, 14, 0, 0),
                ClosedTime = null,
                Status = Status.Pending,
                Priority = null,
                Secret = "s2",
                ParentId = 1,
                CategoryId = 2,
            };

            // Item 3: grandchild (chain 1 -> 2 -> 3), Closed, 0 tags, apostrophe in Name.
            var i3 = new Item
            {
                Id = 3,
                Name = "O'Brien's Tool",
                Code = "CCC-003",
                Quantity = 250,
                Price = 5.50m,
                IsActive = true,
                IsArchived = true,
                CreatedTime = T(2023, 12, 31, 23, 59, 59),
                ClosedTime = T(2024, 12, 31, 0, 0, 0),
                Status = Status.Closed,
                Priority = Priority.Low,
                Secret = "s3",
                ParentId = 2,
                CategoryId = 3,
            };

            // Item 4: standalone, Active, 1 tag, mid-range numbers — gives range filters a hit in the middle.
            var i4 = new Item
            {
                Id = 4,
                Name = "Sprocket",
                Code = "AAA-004",
                Quantity = 50,
                Price = 49.95m,
                IsActive = true,
                IsArchived = false,
                CreatedTime = T(2024, 5, 5, 8, 15, 30),
                ClosedTime = T(2024, 7, 7, 7, 7, 7),
                Status = Status.Active,
                Priority = Priority.Medium,
                Secret = "s4",
                ParentId = null,
                CategoryId = 1,
            };

            // Item 5: standalone, Pending, 0 tags, high quantity, null ClosedTime — boundary for date $filter.
            var i5 = new Item
            {
                Id = 5,
                Name = "Bracket",
                Code = "DDD-005",
                Quantity = 999,
                Price = 0.99m,
                IsActive = false,
                IsArchived = null,
                CreatedTime = T(2025, 2, 28, 0, 0, 0),
                ClosedTime = null,
                Status = Status.Pending,
                Priority = Priority.High,
                Secret = "s5",
                ParentId = null,
                CategoryId = 2,
            };

            ctx.Items.AddRange(i1, i2, i3, i4, i5);

            ctx.Tags.AddRange(
                new Tag { Id = 1, Label = "red", Value = 1, ItemId = 1 },
                new Tag { Id = 2, Label = "blue", Value = 5, ItemId = 1 },
                new Tag { Id = 3, Label = "green", Value = 9, ItemId = 1 },
                new Tag { Id = 4, Label = "red", Value = 3, ItemId = 2 },
                new Tag { Id = 5, Label = "yellow", Value = 7, ItemId = 4 });

            ctx.SaveChanges();
        }
    }
}
