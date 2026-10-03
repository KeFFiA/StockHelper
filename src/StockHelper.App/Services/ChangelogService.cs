using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Extensions.Logging;
using StockHelper.App.Infrastructure;
using StockHelper.App.Views.Dialogs;
using StockHelper.Core.Abstractions;

namespace StockHelper.App.Services;

public interface IChangelogService
{
    /// <summary>After an update shows the changes since the last seen version, once. Silent on the first install.</summary>
    Task ShowIfUpdatedAsync();

    /// <summary>Shows the whole changelog (Settings → About).</summary>
    void ShowAll();
}

/// <summary>Changelog embedded in the app (Resources/Changelog.md, one "## X.Y.Z — date" section per release).</summary>
public sealed partial class ChangelogService(ISettingsService settings, ILogger<ChangelogService> logger) : IChangelogService
{
    private const string ResourceName = "StockHelper.Changelog.md";

    public async Task ShowIfUpdatedAsync()
    {
        var current = AppInfo.Version;
        var lastSeen = settings.Current.LastSeenVersion;
        if (lastSeen == current)
        {
            return;
        }

        await settings.SaveAsync(settings.Current with { LastSeenVersion = current });
        if (lastSeen is null)
        {
            return;
        }

        var sections = Parse(Load()).Where(s => IsNewer(s.Version, lastSeen) && !IsNewer(s.Version, current)).ToList();
        if (sections.Count == 0)
        {
            return;
        }

        logger.LogInformation("Showing changelog after update {From} → {To}", lastSeen, current);
        Show(string.Format(Resources.Strings.Changelog_UpdatedTitle, current), sections);
    }

    public void ShowAll() => Show(Resources.Strings.Changelog_Title, Parse(Load()));

    private static void Show(string title, IReadOnlyList<ChangelogSection> sections)
    {
        var window = new ChangelogWindow(title, string.Join("\n\n", sections.Select(s => s.Markdown)));
        if (Application.Current.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
    }

    private static string Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Splits the document into release sections, newest first (as written in the file).</summary>
    internal static IReadOnlyList<ChangelogSection> Parse(string markdown)
    {
        var result = new List<ChangelogSection>();
        var matches = SectionHeader().Matches(markdown);
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : markdown.Length;
            result.Add(new ChangelogSection(matches[i].Groups["version"].Value, markdown[start..end].Trim()));
        }

        return result;
    }

    private static bool IsNewer(string version, string than) =>
        System.Version.TryParse(version.Split('-')[0], out var a) && System.Version.TryParse(than.Split('-')[0], out var b) && a > b;

    [GeneratedRegex(@"^## +(?<version>\d+\.\d+\.\d+[^\s]*)", RegexOptions.Multiline)]
    private static partial Regex SectionHeader();
}

public sealed record ChangelogSection(string Version, string Markdown);
