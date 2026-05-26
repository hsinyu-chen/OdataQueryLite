namespace OdataQueryLite.Parsing
{
    /// <summary>
    /// Raised when a <c>$</c>-option the engine has chosen not to implement (currently <c>$apply</c>) is
    /// supplied. Carries the option name so hosts can surface it back to the client without parsing the
    /// message.
    /// </summary>
    /// <param name="optionName">The unsupported option, including the leading <c>$</c>.</param>
    /// <param name="message">Human-readable explanation.</param>
    public sealed class UnsupportedQueryOptionException(string optionName, string message)
        : OdataQueryException(message)
    {
        /// <summary>The unsupported option name (e.g. <c>$apply</c>).</summary>
        public string OptionName { get; } = optionName;
    }
}
