using System.Collections.Generic;

namespace OdataQueryLite.Ast
{
    public abstract record FilterNode;

    public sealed record BinaryNode(BinaryOp Op, FilterNode Left, FilterNode Right) : FilterNode;

    public sealed record UnaryNode(UnaryOp Op, FilterNode Operand) : FilterNode;

    public sealed record FunctionNode(FunctionName Name, IReadOnlyList<FilterNode> Args) : FilterNode;

    public sealed record MemberNode(IReadOnlyList<string> Path) : FilterNode;

    // OData collection lambda: Items/any(o: o/Status eq 'Active') or Items/all(...).
    // Param + Body are null for the no-arg form `Items/any()` (collection-non-empty test).
    public sealed record LambdaCollectionNode(
        IReadOnlyList<string> CollectionPath,
        LambdaOp Op,
        string? Param,
        FilterNode? Body) : FilterNode;

    public enum LambdaOp { Any, All }

    public sealed record ParamRefNode(int Index, LiteralKind Kind) : FilterNode;

    public enum BinaryOp { Eq, Ne, Gt, Ge, Lt, Le, And, Or }

    public enum UnaryOp { Not }

    public enum FunctionName
    {
        // String comparison
        Contains, StartsWith, EndsWith,
        // String manipulation
        ToLower, ToUpper, Trim, Length, IndexOf, Substring, Concat,
        // Date extraction
        Year, Month, Day, Hour, Minute, Second,
        // Math
        Round, Floor, Ceiling
    }

    public enum LiteralKind { Number, String, Boolean, DateTime, Null }

    public sealed record LiteralValue(object? Value, LiteralKind Kind);

    public sealed record FilterParseResult(FilterNode Ast, IReadOnlyList<LiteralValue> Literals);
}
