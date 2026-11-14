namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 机密存储（API Key 等）。各平台提供安全实现（Windows 凭据库等），避免把密钥明文写进设置 JSON。
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// 读取机密；不存在时返回 null
    /// </summary>
    string? Get(string name);

    /// <summary>
    /// 保存机密；<paramref name="value"/> 为 null 或空串表示删除
    /// </summary>
    void Set(string name, string? value);
}

/// <summary>
/// 仅保存在内存中的机密存储，用作没有平台实现时的兜底，并优先回退到同名环境变量
/// （例如 <c>DEEPSEEK_API_KEY</c>）
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public string? Get(string name)
    {
        lock (_gate)
        {
            if (_values.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return Environment.GetEnvironmentVariable(name.ToUpperInvariant().Replace('.', '_').Replace('-', '_'));
    }

    public void Set(string name, string? value)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(value))
            {
                _values.Remove(name);
            }
            else
            {
                _values[name] = value;
            }
        }
    }
}

/// <summary>
/// 已知的机密名称
/// </summary>
public static class SecretNames
{
    public const string DeepSeekApiKey = "DeepSeek.ApiKey";
}
