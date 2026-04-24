using System.Text.RegularExpressions;
using SharpDocs.Models;

namespace SharpDocs.Services;

public sealed class SearchIndex
{
    private readonly List<Entry> _entries;

    public SearchIndex(DocsLoader docs)
    {
        _entries = docs.AllPages
            .Select(p => new Entry(
                Page: p,
                Tokens: Tokenize(p.Title + " " + (p.Description ?? "") + " " +
                                 string.Join(' ', p.Headings.Select(h => h.Text)) + " " + p.RawText)))
            .ToList();
    }

    public IReadOnlyList<SearchHit> Search(string query, int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();
        var terms = Tokenize(query);
        if (terms.Count == 0) return Array.Empty<SearchHit>();

        var hits = new List<SearchHit>();
        foreach (var e in _entries)
        {
            var score = 0;
            foreach (var term in terms)
            {
                if (e.Page.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                    score += 10;
                foreach (var h in e.Page.Headings)
                    if (h.Text.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 3;
                if (e.Tokens.Contains(term)) score += 1;
            }
            if (score > 0)
                hits.Add(new SearchHit(e.Page, score, Snippet(e.Page.RawText, terms)));
        }

        return hits.OrderByDescending(h => h.Score).Take(take).ToList();
    }

    private static readonly Regex Tokenizer = new(@"[a-zA-Z0-9]+", RegexOptions.Compiled);

    private static HashSet<string> Tokenize(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Tokenizer.Matches(text))
        {
            if (m.Length >= 2) set.Add(m.Value.ToLowerInvariant());
        }
        return set;
    }

    private static string Snippet(string text, HashSet<string> terms)
    {
        foreach (var term in terms)
        {
            var i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                var start = Math.Max(0, i - 60);
                var end = Math.Min(text.Length, i + 120);
                var s = text[start..end].Trim();
                if (start > 0) s = "…" + s;
                if (end < text.Length) s += "…";
                return s;
            }
        }
        return text.Length > 160 ? text[..160] + "…" : text;
    }

    private sealed record Entry(DocPage Page, HashSet<string> Tokens);
}

public sealed record SearchHit(DocPage Page, int Score, string Snippet);
