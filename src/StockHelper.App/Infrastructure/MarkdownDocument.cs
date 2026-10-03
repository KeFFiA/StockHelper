using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace StockHelper.App.Infrastructure;

/// <summary>
/// Minimal Markdown → FlowDocument for the changelog: "#", "##", "###" headings, "- " bullets,
/// paragraphs and **bold**. Colors and fonts come from the app theme.
/// </summary>
public static partial class MarkdownDocument
{
    public static FlowDocument Build(string markdown, FrameworkElement themeSource)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = (FontFamily)themeSource.FindResource("Font.Text"),
            FontSize = 14,
            LineHeight = 21,
            TextAlignment = TextAlignment.Left,
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "TextFillColorPrimaryBrush");

        List? list = null;
        foreach (var raw in markdown.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                list ??= new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(22, 0, 0, 0) };
                var paragraph = Inline(line[2..]);
                paragraph.Margin = new Thickness(0, 0, 0, 4);
                list.ListItems.Add(new ListItem(paragraph));
                continue;
            }

            if (list is not null)
            {
                document.Blocks.Add(list);
                list = null;
            }

            if (line.Length == 0 || line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                var heading = Inline(line[4..]);
                heading.FontWeight = FontWeights.SemiBold;
                heading.FontSize = 15;
                heading.Margin = new Thickness(0, 10, 0, 6);
                heading.SetResourceReference(TextElement.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
                document.Blocks.Add(heading);
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = Inline(line[3..]);
                heading.FontWeight = FontWeights.SemiBold;
                heading.FontSize = 20;
                heading.FontFamily = (FontFamily)themeSource.FindResource("Font.Display");
                heading.Margin = new Thickness(0, document.Blocks.Count == 0 ? 0 : 18, 0, 6);
                document.Blocks.Add(heading);
            }
            else
            {
                var paragraph = Inline(line);
                paragraph.Margin = new Thickness(0, 0, 0, 8);
                paragraph.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorSecondaryBrush");
                document.Blocks.Add(paragraph);
            }
        }

        if (list is not null)
        {
            document.Blocks.Add(list);
        }

        return document;
    }

    private static Paragraph Inline(string text)
    {
        var paragraph = new Paragraph();
        var position = 0;
        foreach (Match match in Bold().Matches(text))
        {
            paragraph.Inlines.Add(new Run(text[position..match.Index]));
            paragraph.Inlines.Add(new Bold(new Run(match.Groups[1].Value)));
            position = match.Index + match.Length;
        }

        paragraph.Inlines.Add(new Run(text[position..]));
        return paragraph;
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();
}
