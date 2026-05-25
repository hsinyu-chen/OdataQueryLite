using System;

namespace OdataQueryLite.Parsing
{
    public sealed class UnsupportedQueryOptionException(string optionName, string message)
        : Exception(message)
    {
        public string OptionName { get; } = optionName;
    }
}
