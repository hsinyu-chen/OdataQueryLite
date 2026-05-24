namespace OdataQueryLite.Parsing
{
    public enum TokenKind
    {
        Identifier,
        StringLiteral,
        NumberLiteral,
        BoolLiteral,
        NullLiteral,
        DateTimeLiteral,
        LParen,
        RParen,
        Comma,
        Slash,
        Semicolon,
        Equals,
        Colon,
        EOF
    }

    public readonly record struct Token(TokenKind Kind, string Text, int Position);
}
