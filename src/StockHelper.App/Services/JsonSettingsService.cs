using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StockHelper.App.Infrastructure;
using StockHelper.Core.Abstractions;

namespace StockHelper.App.Services;

/// <summary>Stores <see cref="AppSettings"/> in %APPDATA%\StockHelper\settings.json.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<JsonSettingsService> _logger;

    public JsonSettingsService(ILogger<JsonSettingsService> logger)
    {
        _logger = logger;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = AppPaths.SettingsFile + ".tmp";
        await File.WriteAllTextAsync(temp, json);
        File.Move(temp, AppPaths.SettingsFile, overwrite: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings file is unreadable, defaults are used");
        }

        return new AppSettings();
    }
}
