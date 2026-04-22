namespace SmartData.Server.Providers;

/// <summary>
/// Abstract syntax tree for WHERE clauses. Parsed from JSON filters,
/// converted to parameterized SQL by each database provider.
/// </summary>
public abstract record WhereClause;

public record Comparison(string Column, CompareOp Op, object Value) : WhereClause;
public record InList(string Column, object[] Values, bool Negate = false) : WhereClause;
public record Like(string Column, string Pattern) : WhereClause;
public record IsNull(string Column, bool Negate = false) : WhereClause;
public record And(WhereClause[] Conditions) : WhereClause;
public record Or(WhereClause[] Conditions) : WhereClause;

public enum CompareOp
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}
