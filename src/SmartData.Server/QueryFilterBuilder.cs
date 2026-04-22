using System.Text.Json;
using SmartData.Server.Providers;

namespace SmartData.Server;

/// <summary>
/// Parses structured JSON filters into a WhereClause AST.
/// The AST is then converted to parameterized SQL by the database provider.
/// </summary>
internal static class QueryFilterBuilder
{
    public static WhereClause? Parse(string? filterJson)
    {
        if (string.IsNullOrEmpty(filterJson))
            return null;

        var filter = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(filterJson);
        if (filter == null || filter.Count == 0)
            return null;

        var conditions = new List<WhereClause>();

        foreach (var (field, value) in filter)
        {
            if (field == "$and")
                conditions.Add(ParseLogical<And>(value));
            else if (field == "$or")
                conditions.Add(ParseLogical<Or>(value));
            else
                conditions.AddRange(ParseField(field, value));
        }

        return conditions.Count == 1 ? conditions[0] : new And(conditions.ToArray());
    }

    private static WhereClause ParseLogical<T>(JsonElement value) where T : WhereClause
    {
        var conditions = new List<WhereClause>();

        foreach (var item in value.EnumerateArray())
        {
            var subFilter = item.Deserialize<Dictionary<string, JsonElement>>();
            if (subFilter == null) continue;

            var subConditions = new List<WhereClause>();
            foreach (var (field, val) in subFilter)
            {
                if (field == "$and")
                    subConditions.Add(ParseLogical<And>(val));
                else if (field == "$or")
                    subConditions.Add(ParseLogical<Or>(val));
                else
                    subConditions.AddRange(ParseField(field, val));
            }

            if (subConditions.Count == 1)
                conditions.Add(subConditions[0]);
            else if (subConditions.Count > 1)
                conditions.Add(new And(subConditions.ToArray()));
        }

        if (typeof(T) == typeof(Or))
            return new Or(conditions.ToArray());
        return new And(conditions.ToArray());
    }

    private static List<WhereClause> ParseField(string field, JsonElement value)
    {
        // Direct value = equals
        if (value.ValueKind != JsonValueKind.Object)
            return [new Comparison(field, CompareOp.Equal, ExtractValue(value))];

        var clauses = new List<WhereClause>();

        foreach (var prop in value.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "$gt":
                    clauses.Add(new Comparison(field, CompareOp.GreaterThan, ExtractValue(prop.Value)));
                    break;
                case "$gte":
                    clauses.Add(new Comparison(field, CompareOp.GreaterThanOrEqual, ExtractValue(prop.Value)));
                    break;
                case "$lt":
                    clauses.Add(new Comparison(field, CompareOp.LessThan, ExtractValue(prop.Value)));
                    break;
                case "$lte":
                    clauses.Add(new Comparison(field, CompareOp.LessThanOrEqual, ExtractValue(prop.Value)));
                    break;
                case "$ne":
                    clauses.Add(new Comparison(field, CompareOp.NotEqual, ExtractValue(prop.Value)));
                    break;
                case "$like":
                    clauses.Add(new Like(field, prop.Value.GetString()!));
                    break;
                case "$starts":
                    clauses.Add(new Like(field, prop.Value.GetString() + "%"));
                    break;
                case "$ends":
                    clauses.Add(new Like(field, "%" + prop.Value.GetString()));
                    break;
                case "$contains":
                    clauses.Add(new Like(field, "%" + prop.Value.GetString() + "%"));
                    break;
                case "$in":
                    clauses.Add(new InList(field, ExtractArray(prop.Value)));
                    break;
                case "$nin":
                    clauses.Add(new InList(field, ExtractArray(prop.Value), Negate: true));
                    break;
                case "$null":
                    clauses.Add(new IsNull(field, Negate: !prop.Value.GetBoolean()));
                    break;
                case "$notnull":
                    clauses.Add(new IsNull(field, Negate: prop.Value.GetBoolean()));
                    break;
            }
        }

        return clauses;
    }

    private static object[] ExtractArray(JsonElement el) =>
        el.EnumerateArray().Select(ExtractValue).ToArray();

    private static object ExtractValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number when el.TryGetInt64(out var l) => l,
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => el.GetString() ?? ""
    };
}
