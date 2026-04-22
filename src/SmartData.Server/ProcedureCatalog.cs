using SmartData.Server.Procedures;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SmartData.Server;

public class ProcedureCatalog
{
    private static readonly Assembly SystemAssembly = typeof(ProcedureCatalog).Assembly;
    private readonly Dictionary<string, Type> _procedures = new(StringComparer.OrdinalIgnoreCase);

    public ProcedureCatalog(
        IEnumerable<ProcedureAssemblyRegistration> assemblyRegistrations,
        IEnumerable<ProcedureRegistration> procedureRegistrations)
    {
        // Register procedures from assembly scanning
        foreach (var reg in assemblyRegistrations)
            RegisterFromAssembly(reg.Assembly);

        // Register procedures with explicit names
        foreach (var reg in procedureRegistrations)
            Register(reg.Name, reg.Type);
    }

    public void Register(string name, Type type) =>
        _procedures[name] = type;

    public void Register<T>(string name) where T : IStoredProcedure =>
        _procedures[name] = typeof(T);

    public void RegisterFromAssembly(Assembly assembly)
    {
        var isSystem = assembly == SystemAssembly;
        var prefix = isSystem ? "sp_" : "usp_";

        var types = assembly.GetTypes()
            .Where(t => (typeof(IStoredProcedure).IsAssignableFrom(t) || typeof(IAsyncStoredProcedure).IsAssignableFrom(t))
                && t is { IsAbstract: false, IsInterface: false });

        foreach (var type in types)
        {
            var name = ToSnakeCase(type.Name);

            // System procedures: SpUserList → sp_user_list (strip "sp_" from name, re-add prefix)
            // User procedures:   UserList   → usp_user_list
            if (name.StartsWith("sp_"))
                name = prefix + name[3..];
            else
                name = prefix + name;

            _procedures.TryAdd(name, type);
        }
    }

    public Type Resolve(string name) =>
        _procedures.TryGetValue(name, out var type)
            ? type
            : throw new InvalidOperationException($"Unknown procedure: '{name}'");

    public IReadOnlyDictionary<string, Type> GetAll() => _procedures;

    private static string ToSnakeCase(string name)
    {
        var result = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2");
        return result.ToLowerInvariant();
    }
}
