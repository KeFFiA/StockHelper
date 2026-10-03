namespace StockHelper.App.Infrastructure;

public static class TextSearch
{
    /// <summary>True when every word of <paramref name="query"/> occurs in any of the fields (case-insensitive).</summary>
    public static bool Matches(string? query, params string?[] fields)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.All(word => fields.Any(f => f is not null && f.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
    }
}
