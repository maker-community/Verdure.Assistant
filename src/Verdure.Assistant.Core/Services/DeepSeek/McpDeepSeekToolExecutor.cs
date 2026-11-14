using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 把进程内的 <see cref="McpServer"/> 工具适配为 DeepSeek function 工具。
/// MCP 名称（如 self.music.play）会被映射为 [a-zA-Z0-9_-]{1,128} 并可逆还原。
/// </summary>
public sealed class McpDeepSeekToolExecutor : IDeepSeekToolExecutor
{
    private const int MaxNameLength = 128;

    private static readonly JsonElement EmptyObjectSchema =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private readonly McpServer _server;
    private readonly ILogger<McpDeepSeekToolExecutor>? _logger;
    private readonly Func<McpTool, bool>? _filter;
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, string> _toMcp = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _toDeepSeek = new Dictionary<string, string>();

    /// <param name="server">MCP 服务器</param>
    /// <param name="logger">可选日志</param>
    /// <param name="filter">可选过滤器，返回 false 的工具不会暴露给模型</param>
    public McpDeepSeekToolExecutor(McpServer server, ILogger<McpDeepSeekToolExecutor>? logger = null,
        Func<McpTool, bool>? filter = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _logger = logger;
        _filter = filter;
    }

    public IReadOnlyList<DeepSeekToolDefinition> GetTools()
    {
        var tools = SnapshotTools();
        RebuildMap(tools);

        var result = new List<DeepSeekToolDefinition>(tools.Count);
        foreach (var tool in tools)
        {
            result.Add(DeepSeekToolDefinition.ForFunction(
                _toDeepSeek[tool.Name],
                string.IsNullOrWhiteSpace(tool.Description) ? null : tool.Description,
                BuildSchema(tool)));
        }

        return result;
    }

    /// <summary>MCP 名称 → DeepSeek 名称；未注册的名称仅做字符清洗</summary>
    public string ToDeepSeekName(string mcpName)
    {
        if (_toDeepSeek.TryGetValue(mcpName, out var mapped))
        {
            return mapped;
        }

        RebuildMap(SnapshotTools());
        return _toDeepSeek.TryGetValue(mcpName, out mapped) ? mapped : Sanitize(mcpName);
    }

    /// <summary>DeepSeek 名称 → MCP 名称</summary>
    public bool TryGetMcpName(string deepSeekName, out string mcpName)
    {
        if (_toMcp.TryGetValue(deepSeekName, out mcpName!))
        {
            return true;
        }

        RebuildMap(SnapshotTools());
        return _toMcp.TryGetValue(deepSeekName, out mcpName!);
    }

    public async Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryGetMcpName(name, out var mcpName))
        {
            throw new DeepSeekToolException($"Unknown tool: {name}");
        }

        var tool = SnapshotTools().FirstOrDefault(t => t.Name == mcpName);
        var declared = tool?.Properties.ToDictionary(p => p.Name, p => p.Type)
            ?? new Dictionary<string, McpPropertyType>();

        var arguments = ParseArguments(argumentsJson, declared);
        _logger?.LogDebug("Executing DeepSeek tool {Tool} (MCP {McpTool})", name, mcpName);

        string resultJson;
        try
        {
            resultJson = await _server.CallToolAsync(mcpName, arguments).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DeepSeekToolException($"Tool execution failed: {ex.Message}", ex);
        }

        var (isError, text) = ParseResult(resultJson);
        if (isError)
        {
            throw new DeepSeekToolException(text);
        }

        return string.IsNullOrEmpty(text) ? "OK" : text;
    }

    private IReadOnlyList<McpTool> SnapshotTools()
    {
        var all = _server.GetTools().ToArray();
        return _filter == null ? all : all.Where(_filter).ToArray();
    }

    private void RebuildMap(IReadOnlyList<McpTool> tools)
    {
        var toMcp = new Dictionary<string, string>(StringComparer.Ordinal);
        var toDeepSeek = new Dictionary<string, string>(StringComparer.Ordinal);

        // 本身合法的名称优先占用，避免被其他工具清洗后的名称抢占
        foreach (var tool in tools.Where(t => DeepSeekToolDefinition.IsValidFunctionName(t.Name)))
        {
            toMcp[tool.Name] = tool.Name;
            toDeepSeek[tool.Name] = tool.Name;
        }

        foreach (var tool in tools.Where(t => !DeepSeekToolDefinition.IsValidFunctionName(t.Name)))
        {
            var candidate = Sanitize(tool.Name);
            if (toMcp.ContainsKey(candidate))
            {
                var suffix = "_" + StableHash(tool.Name);
                candidate = candidate[..Math.Min(candidate.Length, MaxNameLength - suffix.Length)] + suffix;
                for (var i = 2; toMcp.ContainsKey(candidate); i++)
                {
                    var counter = $"{suffix}{i}";
                    candidate = candidate[..Math.Min(candidate.Length, MaxNameLength - counter.Length)] + counter;
                }
            }

            toMcp[candidate] = tool.Name;
            toDeepSeek[tool.Name] = candidate;
        }

        lock (_gate)
        {
            _toMcp = toMcp;
            _toDeepSeek = toDeepSeek;
        }
    }

    /// <summary>把非法字符替换为下划线并限制长度</summary>
    internal static string Sanitize(string name)
    {
        var sb = new StringBuilder(Math.Min(name.Length, MaxNameLength));
        foreach (var c in name)
        {
            if (sb.Length >= MaxNameLength)
            {
                break;
            }

            sb.Append(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' ? c : '_');
        }

        return sb.Length == 0 ? "tool" : sb.ToString();
    }

    private static string StableHash(string value)
    {
        // FNV-1a，保证不同进程下结果一致
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash.ToString("x8");
    }

    private static JsonElement BuildSchema(McpTool tool)
    {
        try
        {
            var element = JsonSerializer.SerializeToElement(tool.InputSchema, DeepSeekJson.Options);
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("type", out _))
            {
                return element;
            }
        }
        catch (NotSupportedException)
        {
            // 无法序列化的 schema 退化为空参数
        }

        return EmptyObjectSchema;
    }

    private static Dictionary<string, object> ParseArguments(string argumentsJson,
        IReadOnlyDictionary<string, McpPropertyType> declared)
    {
        var result = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return result;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException ex)
        {
            throw new DeepSeekToolException($"Invalid arguments JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Null)
            {
                return result;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DeepSeekToolException("Tool arguments must be a JSON object");
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                McpPropertyType? type = declared.TryGetValue(property.Name, out var t) ? t : null;
                var value = ConvertValue(property.Value, type);
                if (value != null)
                {
                    result[property.Name] = value;
                }
            }
        }

        return result;
    }

    /// <summary>JsonElement 转为 MCP 属性可识别的 CLR 值（bool/int/long/double/string）</summary>
    public static object? ConvertValue(JsonElement element, McpPropertyType? declared)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            case JsonValueKind.True:
            case JsonValueKind.False:
                return declared == McpPropertyType.String ? element.GetRawText() : element.GetBoolean();

            case JsonValueKind.Number:
                if (declared == McpPropertyType.String)
                {
                    return element.GetRawText();
                }

                if (element.TryGetInt32(out var i))
                {
                    return i;
                }

                if (element.TryGetInt64(out var l))
                {
                    return l;
                }

                var d = element.GetDouble();
                return declared == McpPropertyType.Integer && d == Math.Truncate(d) && Math.Abs(d) <= int.MaxValue
                    ? (int)d
                    : d;

            case JsonValueKind.String:
                var s = element.GetString() ?? string.Empty;
                if (declared == McpPropertyType.Integer && int.TryParse(s, out var parsedInt))
                {
                    return parsedInt;
                }

                if (declared == McpPropertyType.Boolean && bool.TryParse(s, out var parsedBool))
                {
                    return parsedBool;
                }

                return s;

            default:
                return element.GetRawText();
        }
    }

    private static (bool IsError, string Text) ParseResult(string resultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            var isError = root.TryGetProperty("isError", out var err) && err.ValueKind == JsonValueKind.True;
            var texts = new List<string>();
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        texts.Add(text.GetString() ?? string.Empty);
                    }
                }
            }

            return (isError, string.Join("\n", texts));
        }
        catch (JsonException)
        {
            return (false, resultJson);
        }
    }
}
