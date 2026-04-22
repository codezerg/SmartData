using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Server;

namespace SmartData.Console.Controllers;

public class SettingsController : ConsoleBaseController
{
    private readonly SettingsService _settings;

    public SettingsController(IAuthenticatedProcedureService procedureService, SettingsService settings) : base(procedureService)
    {
        _settings = settings;
    }

    [HttpGet("/console/settings")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await PopulateLayout(null, ct);
        return PageOrPartial("Index", BuildViewModel());
    }

    [HttpPost("/console/settings")]
    public async Task<IActionResult> Save(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);
        var errors = new List<string>();
        var changed = 0;

        foreach (var desc in _settings.Descriptors)
        {
            if (desc.IsReadOnly) continue;

            string newValue;
            if (desc.PropertyType == typeof(bool))
                newValue = form.ContainsKey(desc.Key) ? "True" : "False";
            else if (!form.TryGetValue(desc.Key, out var formValue))
                continue;
            else
                newValue = formValue.ToString();

            try
            {
                _settings.Update(desc.Key, newValue);
                changed++;
            }
            catch (Exception ex)
            {
                errors.Add($"{desc.DisplayName}: {ex.Message}");
            }
        }

        await PopulateLayout(null, ct);
        var model = BuildViewModel();
        if (errors.Count > 0)
            model.ErrorMessage = string.Join("; ", errors);
        else
            model.SuccessMessage = $"Settings saved ({changed} updated).";

        return PageOrPartial("Index", model);
    }

    private SettingsViewModel BuildViewModel()
    {
        var entries = _settings.GetAll();
        var groups = entries
            .GroupBy(e => e.Section)
            .Select(g => new SettingsGroupViewModel { Section = g.Key, Items = g.ToList() })
            .ToList();

        return new SettingsViewModel { Groups = groups };
    }
}
