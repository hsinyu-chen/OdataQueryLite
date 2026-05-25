namespace OdataQueryLite.Parsing
{
    public sealed class UnsupportedQueryOptionException(string optionName, string message)
        : OdataQueryException(message)
    {
        public string OptionName { get; } = optionName;
    }
}
