namespace SmartData.Server.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class IndexAttribute : Attribute
{
    public string Name { get; }
    public string[] Columns { get; }
    public bool Unique { get; set; }

    public IndexAttribute(string name, params string[] columns)
    {
        Name = name;
        Columns = columns;
    }
}
