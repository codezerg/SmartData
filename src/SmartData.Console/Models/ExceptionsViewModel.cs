using SmartData.Contracts;

namespace SmartData.Console.Models;

public class ExceptionsViewModel
{
    public ExceptionsResult Exceptions { get; set; } = new();
    public string? FilterType { get; set; }
    public string? FilterProcedure { get; set; }
}
