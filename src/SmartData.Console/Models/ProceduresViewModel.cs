namespace SmartData.Console.Models;

public class ProceduresViewModel
{
    public List<ProcedureInfo> Procedures { get; set; } = [];
    public int SystemCount { get; set; }
    public int UserCount { get; set; }
    public string ActiveTab { get; set; } = "all";
}

public class ProcedureInfo
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public List<ProcedureParameter> Parameters { get; set; } = [];
    public string ReturnType { get; set; } = "";
}

public class ProcedureParameter
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsOptional { get; set; }
}
