namespace OdataQueryLite.Parsing
{
    /// <summary>Categorical type of a <see cref="Token"/>.</summary>
    public enum TokenKind
    {
        /// <summary>Bare identifier or keyword (<c>eq</c>, <c>and</c>, property name, function name, ...).</summary>
        Identifier,
        /// <summary>Single-quoted string literal; <c>''</c> escapes a quote.</summary>
        StringLiteral,
        /// <summary>Numeric literal (integer, decimal, or exponential form).</summary>
        NumberLiteral,
        /// <summary>Boolean literal — <c>true</c> or <c>false</c>.</summary>
        BoolLiteral,
        /// <summary>The <c>null</c> literal.</summary>
        NullLiteral,
        /// <summary>ISO-8601 date/time literal (with explicit timezone or <c>Z</c>).</summary>
        DateTimeLiteral,
        /// <summary>Left parenthesis <c>(</c>.</summary>
        LParen,
        /// <summary>Right parenthesis <c>)</c>.</summary>
        RParen,
        /// <summary>Comma <c>,</c>.</summary>
        Comma,
        /// <summary>Slash <c>/</c> — member-path separator and expand-nesting operator.</summary>
        Slash,
        /// <summary>Semicolon <c>;</c> — separator for inner <c>$expand</c> options.</summary>
        Semicolon,
        /// <summary>Equals sign <c>=</c> — assigns inner option values inside <c>$expand</c>.</summary>
        Equals,
        /// <summary>Colon <c>:</c> — separates lambda parameter from body.</summary>
        Colon,
        /// <summary>End-of-input sentinel emitted by the lexer after the last real token.</summary>
        EOF
    }

    /// <summary>One token emitted by <see cref="OdataLexer.Tokenize"/>.</summary>
    /// <param name="Kind">Token category.</param>
    /// <param name="Text">Source text for the token (string literals carry their unescaped value).</param>
    /// <param name="Position">Zero-based character offset into the original input.</param>
    public readonly record struct Token(TokenKind Kind, string Text, int Position);
}
