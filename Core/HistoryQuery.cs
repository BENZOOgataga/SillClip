using System.Globalization;
using System.Text;

namespace WindowSill.ClipboardHistory.Core;

internal sealed record HistorySearchEntry(string Text, string Type, DateTimeOffset CapturedAt, bool IsPinned, IReadOnlyList<string> Collections);

/// <summary>Parses the search syntax once per query, rather than once per history entry.</summary>
internal sealed class HistoryQuery
{
    private readonly List<string> _terms = [];
    private readonly List<string> _types = [];
    private readonly List<string> _collections = [];
    private DateOnly? _after, _before;
    private bool? _pinned;
    internal string? Error { get; private set; }

    internal static HistoryQuery Parse(string text)
    {
        var query = new HistoryQuery();
        var tokens = Tokenize(text, out bool complete);
        foreach (string token in tokens)
        {
            int colon = token.IndexOf(':');
            string name = colon > 0 ? token[..colon].ToLowerInvariant() : string.Empty;
            string value = colon > 0 ? token[(colon + 1)..] : token;
            switch (name)
            {
                case "type":
                    string type = value.ToLowerInvariant() switch { "link" or "url" => "uri", "text" => "text", _ => value.ToLowerInvariant() };
                    if (type is not ("text" or "html" or "rtf" or "image" or "uri" or "file" or "color" or "applicationlink" or "useractivity" or "unknown"))
                        query.Error = "WorkspaceQueryType";
                    else query._types.Add(type);
                    break;
                case "after":
                case "before":
                    if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        query.Error = "WorkspaceQueryDate";
                    else if (name == "after") query._after = date;
                    else query._before = date;
                    break;
                case "pinned":
                    if (bool.TryParse(value, out bool pinned)) query._pinned = pinned;
                    else query.Error = "WorkspaceQueryPinned";
                    break;
                case "collection":
                    if (string.IsNullOrWhiteSpace(value)) query.Error = "WorkspaceQueryCollection";
                    else query._collections.Add(value);
                    break;
                default:
                    query._terms.Add(token);
                    break;
            }
        }
        if (!complete) query.Error = "WorkspaceQueryQuote";
        if (query._after > query._before) query.Error = "WorkspaceQueryRange";
        return query;
    }

    internal bool Matches(HistorySearchEntry item)
    {
        if (Error is not null) return false;
        var date = DateOnly.FromDateTime(item.CapturedAt.LocalDateTime);
        return (!_after.HasValue || date >= _after.Value)
            && (!_before.HasValue || date <= _before.Value)
            && (!_pinned.HasValue || item.IsPinned == _pinned.Value)
            && (_types.Count == 0 || _types.Contains(item.Type.ToLowerInvariant())
                || (_types.Contains("text") && item.Type.ToLowerInvariant() is "html" or "rtf"))
            && _collections.All(name => item.Collections.Contains(name, StringComparer.OrdinalIgnoreCase))
            && _terms.All(term => item.Text.Contains(term, StringComparison.CurrentCultureIgnoreCase));
    }

    private static List<string> Tokenize(string text, out bool complete)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            if (quoted && value == '\\' && index + 1 < text.Length && text[index + 1] is '\\' or '"')
            { current.Append(text[++index]); continue; }
            if (value == '"') quoted = !quoted;
            else if (char.IsWhiteSpace(value) && !quoted)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(value);
        }
        if (current.Length > 0) result.Add(current.ToString());
        complete = !quoted;
        return result;
    }
}
