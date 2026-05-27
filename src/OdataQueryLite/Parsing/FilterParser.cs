using System;
using System.Collections.Generic;
using System.Globalization;
using OdataQueryLite.Ast;

namespace OdataQueryLite.Parsing
{
    /// <summary>Parses raw <c>$filter</c> strings into a <see cref="FilterParseResult"/> AST.</summary>
    public static class FilterParser
    {
        /// <summary>Tokenizes and parses <paramref name="input"/>.</summary>
        /// <param name="input">Raw <c>$filter</c> value.</param>
        /// <returns>The parsed AST plus literal slots.</returns>
        /// <exception cref="FilterSyntaxException">The input does not match the OData <c>$filter</c> grammar.</exception>
        public static FilterParseResult Parse(string input) => Parse(OdataLexer.Tokenize(input));

        /// <summary>Parses an already-tokenized <paramref name="lexed"/> stream.</summary>
        /// <param name="lexed">Lexer output for the filter string.</param>
        /// <returns>The parsed AST plus literal slots.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="lexed"/> is <see langword="null"/>.</exception>
        /// <exception cref="FilterSyntaxException">The tokens do not match the OData <c>$filter</c> grammar.</exception>
        public static FilterParseResult Parse(LexedQuery lexed)
        {
            ArgumentNullException.ThrowIfNull(lexed);
            var state = new ParserState(lexed.Tokens);
            List<LiteralValue> literals = [];
            var node = ParseOr(state, literals);
            if (state.Peek().Kind != TokenKind.EOF)
            {
                var t = state.Peek();
                throw new FilterSyntaxException($"unexpected token '{t.Text}'", t.Position);
            }
            return new FilterParseResult(node, literals);
        }

        private static FilterNode ParseOr(ParserState s, List<LiteralValue> literals)
        {
            s.EnterRecursion();
            try
            {
                var left = ParseAnd(s, literals);
                while (s.TryConsumeKeyword("or"))
                {
                    var right = ParseAnd(s, literals);
                    left = new BinaryNode(BinaryOp.Or, left, right);
                }
                return left;
            }
            finally { s.ExitRecursion(); }
        }

        private static FilterNode ParseAnd(ParserState s, List<LiteralValue> literals)
        {
            var left = ParseComparison(s, literals);
            while (s.TryConsumeKeyword("and"))
            {
                var right = ParseComparison(s, literals);
                left = new BinaryNode(BinaryOp.And, left, right);
            }
            return left;
        }

        private static FilterNode ParseComparison(ParserState s, List<LiteralValue> literals)
        {
            var left = ParseUnary(s, literals);
            var t = s.Peek();
            if (t.Kind == TokenKind.Identifier && TryMapCompareOp(t.Text, out var op))
            {
                s.Consume();
                var right = ParseUnary(s, literals);
                return new BinaryNode(op, left, right);
            }
            return left;
        }

        private static FilterNode ParseUnary(ParserState s, List<LiteralValue> literals)
        {
            // `not` is the one recursion path that doesn't transit ParseOr, so it needs its own
            // depth guard — otherwise `not not not ... 1 eq 1` walks the call stack unchecked.
            if (s.TryConsumeKeyword("not"))
            {
                s.EnterRecursion();
                try { return new UnaryNode(UnaryOp.Not, ParseUnary(s, literals)); }
                finally { s.ExitRecursion(); }
            }
            return ParsePrimary(s, literals);
        }

        private static FilterNode ParsePrimary(ParserState s, List<LiteralValue> literals)
        {
            var t = s.Peek();
            switch (t.Kind)
            {
                case TokenKind.LParen:
                    s.Consume();
                    var inner = ParseOr(s, literals);
                    s.Expect(TokenKind.RParen);
                    return inner;
                case TokenKind.StringLiteral:
                    s.Consume();
                    return EmitParam(literals, t.Text, LiteralKind.String);
                case TokenKind.NumberLiteral:
                    s.Consume();
                    return EmitParam(literals, ParseNumber(t.Text), LiteralKind.Number);
                case TokenKind.BoolLiteral:
                    s.Consume();
                    return EmitParam(literals, t.Text == "true", LiteralKind.Boolean);
                case TokenKind.NullLiteral:
                    s.Consume();
                    return EmitParam(literals, null, LiteralKind.Null);
                case TokenKind.DateTimeLiteral:
                    s.Consume();
                    return EmitParam(literals, ParseDate(t.Text, t.Position), LiteralKind.DateTime);
                case TokenKind.Identifier:
                    return ParseIdentifierStart(s, literals);
                default:
                    throw new FilterSyntaxException($"unexpected token '{t.Text}'", t.Position);
            }
        }

        private static FilterNode ParseIdentifierStart(ParserState s, List<LiteralValue> literals)
        {
            var head = s.Consume();
            if (s.Peek().Kind == TokenKind.LParen && TryMapFunction(head.Text, out var fn))
            {
                s.Consume(); // (
                List<FilterNode> args = [];
                if (s.Peek().Kind != TokenKind.RParen)
                {
                    args.Add(ParseOr(s, literals));
                    while (s.Peek().Kind == TokenKind.Comma)
                    {
                        s.Consume();
                        args.Add(ParseOr(s, literals));
                    }
                }
                s.Expect(TokenKind.RParen);
                return new FunctionNode(fn, args);
            }
            // Member path, possibly terminated by a /any(...) or /all(...) lambda.
            List<string> path = [head.Text];
            while (s.Peek().Kind == TokenKind.Slash)
            {
                s.Consume();
                var seg = s.Expect(TokenKind.Identifier);
                if ((seg.Text == "any" || seg.Text == "all") && s.Peek().Kind == TokenKind.LParen)
                {
                    var op = seg.Text == "any" ? LambdaOp.Any : LambdaOp.All;
                    return ParseLambdaBody(s, path, op, literals);
                }
                path.Add(seg.Text);
            }
            return new MemberNode(path);
        }

        private static LambdaCollectionNode ParseLambdaBody(ParserState s, List<string> collectionPath, LambdaOp op, List<LiteralValue> literals)
        {
            s.Expect(TokenKind.LParen);
            string? param = null;
            FilterNode? body = null;
            if (s.Peek().Kind != TokenKind.RParen)
            {
                param = s.Expect(TokenKind.Identifier).Text;
                s.Expect(TokenKind.Colon);
                body = ParseOr(s, literals);
            }
            s.Expect(TokenKind.RParen);
            return new LambdaCollectionNode(collectionPath, op, param, body);
        }

        private static ParamRefNode EmitParam(List<LiteralValue> literals, object? value, LiteralKind kind)
        {
            var idx = literals.Count;
            literals.Add(new LiteralValue(value, kind));
            return new ParamRefNode(idx, kind);
        }

        private static bool TryMapCompareOp(string text, out BinaryOp op)
        {
            switch (text)
            {
                case "eq": op = BinaryOp.Eq; return true;
                case "ne": op = BinaryOp.Ne; return true;
                case "gt": op = BinaryOp.Gt; return true;
                case "ge": op = BinaryOp.Ge; return true;
                case "lt": op = BinaryOp.Lt; return true;
                case "le": op = BinaryOp.Le; return true;
                default: op = default; return false;
            }
        }

        private static bool TryMapFunction(string text, out FunctionName fn)
        {
            switch (text)
            {
                case "contains": fn = FunctionName.Contains; return true;
                case "startswith": fn = FunctionName.StartsWith; return true;
                case "endswith": fn = FunctionName.EndsWith; return true;
                case "tolower": fn = FunctionName.ToLower; return true;
                case "toupper": fn = FunctionName.ToUpper; return true;
                case "trim": fn = FunctionName.Trim; return true;
                case "length": fn = FunctionName.Length; return true;
                case "indexof": fn = FunctionName.IndexOf; return true;
                case "substring": fn = FunctionName.Substring; return true;
                case "concat": fn = FunctionName.Concat; return true;
                case "year": fn = FunctionName.Year; return true;
                case "month": fn = FunctionName.Month; return true;
                case "day": fn = FunctionName.Day; return true;
                case "hour": fn = FunctionName.Hour; return true;
                case "minute": fn = FunctionName.Minute; return true;
                case "second": fn = FunctionName.Second; return true;
                case "round": fn = FunctionName.Round; return true;
                case "floor": fn = FunctionName.Floor; return true;
                case "ceiling": fn = FunctionName.Ceiling; return true;
                default: fn = default; return false;
            }
        }

        private static object ParseNumber(string text)
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        private static DateTimeOffset ParseDate(string text, int pos)
        {
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto;
            throw new FilterSyntaxException($"invalid datetime literal '{text}'", pos);
        }
    }
}
