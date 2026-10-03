using System.Diagnostics;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using StockHelper.App.Resources;

namespace StockHelper.App.Services;

public enum ExportFormat
{
    Text,
    Quantity,
    Money,
    Integer,
    Date,
    DateTime,
    Percent,
}

public sealed record ExportColumn<T>(string Header, Func<T, object?> Value, ExportFormat Format = ExportFormat.Text);

/// <summary>One worksheet: an optional title block followed by a table.</summary>
public sealed record ExportSheet(
    string Name,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Headers,
    IReadOnlyList<ExportFormat> Formats,
    IReadOnlyList<object?[]> Rows)
{
    public static ExportSheet From<T>(string name, string title, string? subtitle, IEnumerable<T> rows, IReadOnlyList<ExportColumn<T>> columns) => new(
        name,
        title,
        subtitle,
        [.. columns.Select(c => c.Header)],
        [.. columns.Select(c => c.Format)],
        [.. rows.Select(r => columns.Select(c => c.Value(r)).ToArray())]);
}

public interface IExportService
{
    /// <summary>Asks for a file name and exports a single table.</summary>
    Task ExportAsync<T>(string title, IReadOnlyList<T> rows, IReadOnlyList<ExportColumn<T>> columns, string? subtitle = null);

    /// <summary>Asks for a file name and exports several sheets (reports).</summary>
    Task ExportAsync(string fileTitle, IReadOnlyList<ExportSheet> sheets);

    /// <summary>Writes the workbook without UI (used by tests and automation).</summary>
    void Write(string path, IReadOnlyList<ExportSheet> sheets);
}

public sealed class ExcelExportService(IDialogService dialogs, INotificationService notifications, ILogger<ExcelExportService> logger)
    : IExportService
{
    private const string QuantityFormat = "#,##0.####";
    private const string MoneyFormat = "#,##0.00 \"₽\"";

    public Task ExportAsync<T>(string title, IReadOnlyList<T> rows, IReadOnlyList<ExportColumn<T>> columns, string? subtitle = null) =>
        ExportAsync(title, [ExportSheet.From(title, title, subtitle, rows, columns)]);

    public async Task ExportAsync(string fileTitle, IReadOnlyList<ExportSheet> sheets)
    {
        var fileName = $"{fileTitle} {DateTime.Now:yyyy-MM-dd HH-mm}.xlsx";
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        var path = dialogs.PickSaveFile(fileName, Strings.Export_Filter);
        if (path is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => Write(path, sheets));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Export failed: {Path}", path);
            dialogs.ShowError(Strings.Export_FileLocked);
            return;
        }

        logger.LogInformation("Exported {Sheets} sheet(s) to {Path}", sheets.Count, path);
        notifications.Show(NotificationKind.Success, Strings.Export_Done, Path.GetFileName(path), Strings.Export_Open,
            () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
    }

    public void Write(string path, IReadOnlyList<ExportSheet> sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var sheet in sheets)
        {
            WriteSheet(workbook, sheet);
        }

        workbook.SaveAs(path);
    }

    private static void WriteSheet(XLWorkbook workbook, ExportSheet sheet)
    {
        var ws = workbook.Worksheets.Add(SafeSheetName(workbook, sheet.Name));
        var row = 1;

        ws.Cell(row, 1).Value = sheet.Title;
        ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(14);
        row++;

        var subtitle = string.Join("   ·   ", new[] { sheet.Subtitle, string.Format(Strings.Export_GeneratedAt, DateTime.Now) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        ws.Cell(row, 1).Value = subtitle;
        ws.Cell(row, 1).Style.Font.SetFontColor(XLColor.Gray);
        row += 2;

        var headerRow = row;
        for (var c = 0; c < sheet.Headers.Count; c++)
        {
            ws.Cell(row, c + 1).Value = sheet.Headers[c];
        }

        var header = ws.Range(row, 1, row, Math.Max(1, sheet.Headers.Count));
        header.Style.Font.SetBold()
            .Fill.SetBackgroundColor(XLColor.FromHtml("#F2F2F2"))
            .Border.SetBottomBorder(XLBorderStyleValues.Thin);

        foreach (var values in sheet.Rows)
        {
            row++;
            for (var c = 0; c < sheet.Headers.Count; c++)
            {
                SetValue(ws.Cell(row, c + 1), c < values.Length ? values[c] : null, sheet.Formats[c]);
            }
        }

        if (sheet.Headers.Count > 0)
        {
            ws.Range(headerRow, 1, Math.Max(headerRow, row), sheet.Headers.Count).SetAutoFilter();
            ws.SheetView.FreezeRows(headerRow);
            ws.Columns(1, sheet.Headers.Count).AdjustToContents(headerRow, row, 8, 60);
        }
    }

    private static void SetValue(IXLCell cell, object? value, ExportFormat format)
    {
        switch (value)
        {
            case null:
                return;
            case decimal d:
                cell.Value = d;
                break;
            case int i:
                cell.Value = i;
                break;
            case double dbl:
                cell.Value = dbl;
                break;
            case DateTime dt:
                cell.Value = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
                break;
            case bool b:
                cell.Value = b ? Strings.Common_Yes : Strings.Common_No;
                break;
            default:
                cell.Value = value.ToString();
                break;
        }

        switch (format)
        {
            case ExportFormat.Quantity:
                cell.Style.NumberFormat.Format = QuantityFormat;
                break;
            case ExportFormat.Money:
                cell.Style.NumberFormat.Format = MoneyFormat;
                break;
            case ExportFormat.Integer:
                cell.Style.NumberFormat.Format = "#,##0";
                break;
            case ExportFormat.Date:
                cell.Style.DateFormat.Format = "dd.MM.yyyy";
                break;
            case ExportFormat.DateTime:
                cell.Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
                break;
            case ExportFormat.Percent:
                cell.Style.NumberFormat.Format = "0%";
                break;
        }
    }

    private static string SafeSheetName(XLWorkbook workbook, string name)
    {
        var clean = new string([.. name.Where(ch => !"[]:*?/\\".Contains(ch))]);
        clean = clean.Length > 31 ? clean[..31] : clean;
        if (string.IsNullOrWhiteSpace(clean))
        {
            clean = "Sheet";
        }

        var candidate = clean;
        for (var i = 2; workbook.Worksheets.Contains(candidate); i++)
        {
            var suffix = $" ({i})";
            candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
        }

        return candidate;
    }
}
