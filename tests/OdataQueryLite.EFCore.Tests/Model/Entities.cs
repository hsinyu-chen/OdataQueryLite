using System.Text.Json.Serialization;

namespace OdataQueryLite.EFCore.Tests.Model
{
    /// <summary>
    /// Root synthetic entity. Neutral field names ONLY — no business/domain terms. Hosts both the
    /// real-world corpus and the capability corpus: scalars of every primitive kind, nullable
    /// scalars, enum + nullable-enum (stored as string), a self-ref nav, a many-to-one ref nav,
    /// and a one-to-many collection nav.
    /// </summary>
    public class Item
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
        public bool? IsArchived { get; set; }
        public DateTimeOffset CreatedTime { get; set; }
        public DateTimeOffset? ClosedTime { get; set; }

        public Status Status { get; set; }
        public Priority? Priority { get; set; }

        // Single intentional-divergence anchor: the engine tightens [JsonIgnore] out of $select
        // projection even when explicitly named, whereas legacy OData surfaced it. Differential
        // expectation for this property asserts the tightened (correct) behavior, not legacy.
        [JsonIgnore]
        public string Secret { get; set; } = string.Empty;

        public long? ParentId { get; set; }
        public Item? Parent { get; set; }

        public long CategoryId { get; set; }
        public Category Category { get; set; } = null!;

        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }

    /// <summary>Many-to-one reference target for <see cref="Item"/>. Carries an enum-as-string column.</summary>
    public class Category
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Priority Kind { get; set; }

        public ICollection<Item> Items { get; set; } = new List<Item>();
    }

    /// <summary>One-to-many child of <see cref="Item"/> — drives collection-nav $expand and any/all lambdas.</summary>
    public class Tag
    {
        public long Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public int Value { get; set; }

        public long ItemId { get; set; }
        public Item Item { get; set; } = null!;
    }
}
