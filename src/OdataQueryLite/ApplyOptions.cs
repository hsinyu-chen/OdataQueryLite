namespace OdataQueryLite
{
    public interface IApplyOptions
    {
        bool Paging { get; }
        bool OrderBy { get; }
        bool SelectExpand { get; }
        bool Count { get; }
    }

    public sealed class ApplyOptions : IApplyOptions
    {
        public bool Paging { get; set; } = true;
        public bool OrderBy { get; set; } = true;
        public bool SelectExpand { get; set; } = true;
        public bool Count { get; set; } = true;

        public ApplyOptions ApplyPaging(bool value) { Paging = value; return this; }
        public ApplyOptions ApplyOrderBy(bool value) { OrderBy = value; return this; }
        public ApplyOptions ApplySelectExpand(bool value) { SelectExpand = value; return this; }
        public ApplyOptions ApplyCount(bool value) { Count = value; return this; }
    }
}
