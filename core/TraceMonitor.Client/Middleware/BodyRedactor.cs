using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using TraceMonitor.Client.Configuration;

namespace TraceMonitor.Client.Middleware;

public static class BodyRedactor
{
    private const string RedactedValue = "***REDACTED***";

    public static Dictionary<string, string> RedactHeaders(IHeaderDictionary headers, TraceMonitorOptions options)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            result[header.Key] = options.SensitiveHeaders.Contains(header.Key)
                ? RedactedValue
                : header.Value.ToString();
        }
        return result;
    }

    public static string? RedactAndTruncateBody(string? rawBody, string? contentType, TraceMonitorOptions options)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        var processed = rawBody;

        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var node = JsonNode.Parse(rawBody);
                if (node is not null)
                {
                    RedactJsonNode(node, options.SensitiveJsonFields);
                    processed = node.ToJsonString();
                }
            }
            catch
            {
                // Not valid JSON; fall back to raw truncation
            }
        }

        var maxLen = Math.Max(256, options.MaxBodyLengthBytes);
        if (processed.Length > maxLen)
        {
            return string.Concat(processed.AsSpan(0, maxLen), "...[TRUNCATED]");
        }

        return processed;
    }

    private static void RedactJsonNode(JsonNode node, HashSet<string> sensitiveFields)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kvp => kvp.Key).ToList())
            {
                if (sensitiveFields.Contains(key))
                {
                    obj[key] = RedactedValue;
                }
                else if (obj[key] is JsonNode child)
                {
                    RedactJsonNode(child, sensitiveFields);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                {
                    RedactJsonNode(item, sensitiveFields);
                }
            }
        }
    }
}
