using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vistumbler.Core.Services;

/// <summary>
/// The original Vistumbler's filter syntax (its _AddFilerString), for a filter's advanced fields, evaluated against
/// one AP value rather than turned into SQL. Matching is case-insensitive, as Access's was.
/// <list type="bullet">
/// <item><c>*</c> or empty: anything</item>
/// <item><c>6</c> / <c>MyNetwork</c>: equal</item>
/// <item><c>1,6,11</c>: any of them (an item with <c>&lt;&gt;</c> excludes it: <c>&lt;&gt;6,&lt;&gt;11</c>)</item>
/// <item><c>1-6</c>, <c>-80--60</c>: between, inclusive</item>
/// <item><c>&lt;&gt;</c> in front: not (<c>&lt;&gt;6</c>, <c>&lt;&gt;1-6</c>)</item>
/// <item><c>%</c>: any text (<c>Linksys%</c>, <c>%guest%</c>)</item>
/// <item><c>\,</c> <c>\-</c> <c>\%</c>: a literal comma, dash or percent</item>
/// </list>
/// </summary>
public static class FilterExpression
{
    private const char Comma = '', Dash = '', Percent = '';   // stand-ins for escaped symbols

    public static bool IsAny(string? expression) => string.IsNullOrWhiteSpace(expression) || expression.Trim() == "*";

    /// <summary>Whether <paramref name="value"/> passes <paramref name="expression"/>; numbers compare as numbers.</summary>
    public static bool Matches(string? expression, string? value, bool numeric)
    {
        if (IsAny(expression)) return true;
        value ??= string.Empty;
        var text = expression!.Trim().Replace("\"", "").Replace("'", "")
            .Replace("\\,", Comma.ToString()).Replace("\\-", Dash.ToString()).Replace("\\%", Percent.ToString());

        var items = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0) return true;
        if (items.Length == 1) return MatchesTerm(items[0], value, numeric);

        // A list: none of the <> items, and any of the others (when there are others)
        var excluded = items.Where(i => i.StartsWith("<>")).ToList();
        var included = items.Where(i => !i.StartsWith("<>")).ToList();
        if (excluded.Any(i => MatchesTerm(i[2..], value, numeric))) return false;
        return included.Count == 0 || included.Any(i => MatchesTerm(i, value, numeric));
    }

    private static bool MatchesTerm(string term, string value, bool numeric)
    {
        bool not = term.StartsWith("<>");
        if (not) term = term[2..].Trim();
        return MatchesPlain(term, value, numeric) != not;
    }

    private static readonly Regex NumberRange = new(@"^(-?\d+(?:\.\d+)?)-(-?\d+(?:\.\d+)?)$", RegexOptions.Compiled);

    private static bool MatchesPlain(string term, string value, bool numeric)
    {
        if (numeric)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
            var range = NumberRange.Match(term);
            if (range.Success)
            {
                double a = double.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture);
                double b = double.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
                return v >= Math.Min(a, b) && v <= Math.Max(a, b);
            }
            if (term.Contains('%')) return Like(term, value);
            return double.TryParse(Unescape(term), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && v == n;
        }

        if (term.Contains('%')) return Like(term, value);
        // The whole text first: values like "WPA2-Personal" contain a dash, and reading that as a range (as the
        // original did unless written "WPA2\-Personal") would match nothing
        if (string.Equals(value, Unescape(term), StringComparison.OrdinalIgnoreCase)) return true;
        int dash = term.IndexOf('-');
        if (dash > 0 && dash < term.Length - 1)   // a text range, as the original allowed; "\-" for a literal dash
        {
            var low = Unescape(term[..dash]);
            var high = Unescape(term[(dash + 1)..]);
            return string.Compare(value, low, StringComparison.OrdinalIgnoreCase) >= 0 &&
                   string.Compare(value, high, StringComparison.OrdinalIgnoreCase) <= 0;
        }
        return string.Equals(value, Unescape(term), StringComparison.OrdinalIgnoreCase);
    }

    // SQL LIKE with % only, as the original's filters used
    private static bool Like(string pattern, string value)
    {
        var regex = new StringBuilder("^");
        foreach (var part in pattern.Split('%'))
            regex.Append(Regex.Escape(Unescape(part))).Append(".*");
        regex.Length -= 2;   // the last part has no % after it
        regex.Append('$');
        return Regex.IsMatch(value, regex.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static string Unescape(string s) =>
        s.Replace(Comma, ',').Replace(Dash, '-').Replace(Percent, '%').Trim();
}
