using System.Collections.Generic;

namespace OdataQueryLite.Ast
{
    /// <summary>Base type for every node in a parsed <c>$filter</c> AST.</summary>
    public abstract record FilterNode;

    /// <summary>Binary boolean / comparison operation (<c>and</c>, <c>or</c>, <c>eq</c>, <c>ne</c>, ...).</summary>
    /// <param name="Op">The operator.</param>
    /// <param name="Left">Left-hand operand.</param>
    /// <param name="Right">Right-hand operand.</param>
    public sealed record BinaryNode(BinaryOp Op, FilterNode Left, FilterNode Right) : FilterNode;

    /// <summary>Unary boolean operation (currently only <c>not</c>).</summary>
    /// <param name="Op">The operator.</param>
    /// <param name="Operand">The operand expression.</param>
    public sealed record UnaryNode(UnaryOp Op, FilterNode Operand) : FilterNode;

    /// <summary>OData built-in function invocation (<c>contains</c>, <c>tolower</c>, <c>year</c>, ...).</summary>
    /// <param name="Name">Which built-in is invoked.</param>
    /// <param name="Args">Argument expressions, positionally.</param>
    public sealed record FunctionNode(FunctionName Name, IReadOnlyList<FilterNode> Args) : FilterNode;

    /// <summary>Dotted/slashed member-access path rooted at the entity (e.g. <c>Customer/Address/City</c>).</summary>
    /// <param name="Path">Path segments from the entity outward.</param>
    public sealed record MemberNode(IReadOnlyList<string> Path) : FilterNode;

    /// <summary>
    /// OData collection lambda — <c>Items/any(o: o/Status eq 'Active')</c> or <c>Items/all(...)</c>.
    /// </summary>
    /// <param name="CollectionPath">Path from the entity to the collection navigation.</param>
    /// <param name="Op">Either <see cref="LambdaOp.Any"/> or <see cref="LambdaOp.All"/>.</param>
    /// <param name="Param">Lambda parameter name, or <see langword="null"/> for the no-arg form <c>Items/any()</c>.</param>
    /// <param name="Body">Lambda body, or <see langword="null"/> for the no-arg form (collection-non-empty test).</param>
    public sealed record LambdaCollectionNode(
        IReadOnlyList<string> CollectionPath,
        LambdaOp Op,
        string? Param,
        FilterNode? Body) : FilterNode;

    /// <summary>OData lambda operator on a collection navigation.</summary>
    public enum LambdaOp
    {
        /// <summary><c>any</c> — true when at least one element matches the predicate.</summary>
        Any,
        /// <summary><c>all</c> — true when every element matches the predicate.</summary>
        All
    }

    /// <summary>
    /// Slot reference into the <see cref="FilterParseResult.Literals"/> array; lets the engine cache one
    /// compiled Expression tree per query shape and bind literals per-request.
    /// </summary>
    /// <param name="Index">Zero-based index into the literals array.</param>
    /// <param name="Kind">Literal kind discovered at parse time.</param>
    public sealed record ParamRefNode(int Index, LiteralKind Kind) : FilterNode;

    /// <summary>Binary <c>$filter</c> operator.</summary>
    public enum BinaryOp
    {
        /// <summary><c>eq</c> — equality.</summary>
        Eq,
        /// <summary><c>ne</c> — inequality.</summary>
        Ne,
        /// <summary><c>gt</c> — greater than.</summary>
        Gt,
        /// <summary><c>ge</c> — greater than or equal.</summary>
        Ge,
        /// <summary><c>lt</c> — less than.</summary>
        Lt,
        /// <summary><c>le</c> — less than or equal.</summary>
        Le,
        /// <summary><c>and</c> — short-circuit conjunction.</summary>
        And,
        /// <summary><c>or</c> — short-circuit disjunction.</summary>
        Or
    }

    /// <summary>Unary <c>$filter</c> operator.</summary>
    public enum UnaryOp
    {
        /// <summary><c>not</c> — boolean negation.</summary>
        Not
    }

    /// <summary>Recognised OData built-in functions.</summary>
    public enum FunctionName
    {
        /// <summary><c>contains(s, t)</c>.</summary>
        Contains,
        /// <summary><c>startswith(s, t)</c>.</summary>
        StartsWith,
        /// <summary><c>endswith(s, t)</c>.</summary>
        EndsWith,
        /// <summary><c>tolower(s)</c>.</summary>
        ToLower,
        /// <summary><c>toupper(s)</c>.</summary>
        ToUpper,
        /// <summary><c>trim(s)</c>.</summary>
        Trim,
        /// <summary><c>length(s)</c>.</summary>
        Length,
        /// <summary><c>indexof(s, t)</c>.</summary>
        IndexOf,
        /// <summary><c>substring(s, start[, len])</c>.</summary>
        Substring,
        /// <summary><c>concat(s, t)</c>.</summary>
        Concat,
        /// <summary><c>year(d)</c>.</summary>
        Year,
        /// <summary><c>month(d)</c>.</summary>
        Month,
        /// <summary><c>day(d)</c>.</summary>
        Day,
        /// <summary><c>hour(d)</c>.</summary>
        Hour,
        /// <summary><c>minute(d)</c>.</summary>
        Minute,
        /// <summary><c>second(d)</c>.</summary>
        Second,
        /// <summary><c>round(n)</c>.</summary>
        Round,
        /// <summary><c>floor(n)</c>.</summary>
        Floor,
        /// <summary><c>ceiling(n)</c>.</summary>
        Ceiling
    }

    /// <summary>Literal value kind as discovered by the lexer / parser.</summary>
    public enum LiteralKind
    {
        /// <summary>Numeric literal (integer or fractional).</summary>
        Number,
        /// <summary>String literal.</summary>
        String,
        /// <summary>Boolean literal (<c>true</c> / <c>false</c>).</summary>
        Boolean,
        /// <summary>ISO-8601 date/time literal.</summary>
        DateTime,
        /// <summary>The <c>null</c> literal.</summary>
        Null
    }

    /// <summary>A parsed literal value paired with its OData literal kind.</summary>
    /// <param name="Value">The boxed value, or <see langword="null"/> for the <see cref="LiteralKind.Null"/> kind.</param>
    /// <param name="Kind">The literal kind.</param>
    public sealed record LiteralValue(object? Value, LiteralKind Kind);

    /// <summary>Result of parsing a <c>$filter</c> string — the AST plus its literal slots.</summary>
    /// <param name="Ast">Parsed filter expression tree.</param>
    /// <param name="Literals">Literal values in slot order, referenced by <see cref="ParamRefNode.Index"/>.</param>
    public sealed record FilterParseResult(FilterNode Ast, IReadOnlyList<LiteralValue> Literals);
}
