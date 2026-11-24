using System.Text.RegularExpressions;

namespace TraceMonitor.Api.Services;

public sealed class ParsedSearchQuery
{
    public List<string> FreeTextTerms { get; } = [];
    public string? AppSlug { get; set; }
    public string? Level { get; set; }
    public string? EventType { get; set; }
    public string? HttpMethod { get; set; }
    public string? StatusFilter { get; set; } // "500", "5xx", "4xx", "2xx"
    public string? TraceId { get; set; }
    public double? MinElapsedMs { get; set; }

    public string? CombinedFreeText => FreeTextTerms.Count == 0 ? null : string.Join(' ', FreeTextTerms);
}

/// <summary>
/// Parses Splunk-style search strings such as:
///   status:500 level:Error app:orders-api "timeout expired"
/// </summary>
public static partial class SearchQueryParser
{
    [GeneratedRegex("""("[^"]+"|\S+)""", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    public static ParsedSearchQuery Parse(string? rawQuery)
    {
        var parsed = new ParsedSearchQuery();
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return parsed;
        }

        foreach (Match match in TokenRegex().Matches(rawQuery))
        {
            var token = match.Value.Trim();
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            // Quoted phrase -> always free text
            if (token.StartsWith('"') && token.EndsWith('"') && token.Length >= 2)
            {
                parsed.FreeTextTerms.Add(token[1..^1]);
                continue;
            }

            var colonIdx = token.IndexOf(':');
            if (colonIdx > 0 && colonIdx < token.Length - 1)
            {
                var key = token[..colonIdx].ToLowerInvariant();
                var value = token[(colonIdx + 1)..].Trim('"');

                switch (key)
                {
                    case "app":
                        parsed.AppSlug = value;
                        continue;
                    case "level":
                        parsed.Level = NormalizeLevel(value);
                        continue;
                    case "type":
                    case "event":
                        parsed.EventType = NormalizeEventType(value);
                        continue;
                    case "method":
                        parsed.HttpMethod = value.ToUpperInvariant();
                        continue;
                    case "status":
                        parsed.StatusFilter = value.ToLowerInvariant();
                        continue;
                    case "trace":
                    case "traceid":
                        parsed.TraceId = value;
                        continue;
                    case "minms":
                    case "slow":
                        if (double.TryParse(value, out var ms))
                        {
                            parsed.MinElapsedMs = ms;
                            continue;
                        }
                        break;
                }
            }

            parsed.FreeTextTerms.Add(token);
        }

        return parsed;
    }

    private static string NormalizeLevel(string value) => value.ToLowerInvariant() switch
    {
        "debug" or "dbg" => "Debug",
        "info" or "information" => "Information",
        "warn" or "warning" => "Warning",
        "err" or "error" => "Error",
        "crit" or "critical" or "fatal" => "Critical",
        _ => value
    };

    private static string NormalizeEventType(string value) => value.ToLowerInvariant() switch
    {
        "request" or "http" => "Request",
        "outgoing" or "outgoinghttp" or "httpclient" => "OutgoingHttp",
        "exception" or "ex" => "Exception",
        "lifecycle" => "Lifecycle",
        "log" => "Log",
        _ => value
    };
}
