namespace OdataQueryLite
{
    public sealed class ApplyOptions
    {
        public bool Paging { get; private set; } = true;
        public bool OrderBy { get; private set; } = true;
        public bool SelectExpand { get; private set; } = true;
        public bool Count { get; private set; } = true;

        public ApplyOptions ApplyPaging(bool value) { Paging = value; return this; }
        public ApplyOptions ApplyOrderBy(bool value) { OrderBy = value; return this; }
        public ApplyOptions ApplySelectExpand(bool value) { SelectExpand = value; return this; }
        public ApplyOptions ApplyCount(bool value) { Count = value; return this; }
    }
}
