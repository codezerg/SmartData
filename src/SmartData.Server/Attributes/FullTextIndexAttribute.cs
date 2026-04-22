namespace SmartData.Server.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class FullTextIndexAttribute : Attribute
{
    public string[] Columns { get; }

    public FullTextIndexAttribute(params string[] columns)
    {
        Columns = columns;
    }
}
