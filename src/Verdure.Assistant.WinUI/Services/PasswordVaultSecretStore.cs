using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Windows.Security.Credentials;

namespace Verdure.Assistant.WinUI.Services;

/// <summary>
/// 使用 Windows 凭据库（PasswordVault）保存 API Key 等机密，不写入设置 JSON。
/// 凭据库不可用时回退到进程内存储（并读取同名环境变量，如 DEEPSEEK_API_KEY）。
/// </summary>
public sealed class PasswordVaultSecretStore : ISecretStore
{
    private const string Resource = "Verdure.Assistant";

    private readonly ILogger<PasswordVaultSecretStore>? _logger;
    private readonly InMemorySecretStore _fallback = new();
    private PasswordVault? _vault;

    public PasswordVaultSecretStore(ILogger<PasswordVaultSecretStore>? logger = null)
    {
        _logger = logger;
        try
        {
            _vault = new PasswordVault();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Windows 凭据库不可用，API Key 将仅保存在内存中");
        }
    }

    public string? Get(string name)
    {
        if (_vault != null)
        {
            try
            {
                var credential = _vault.Retrieve(Resource, name);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch (Exception)
            {
                // 未保存过：回退到内存/环境变量
            }
        }

        return _fallback.Get(name);
    }

    public void Set(string name, string? value)
    {
        if (_vault == null)
        {
            _fallback.Set(name, value);
            return;
        }

        try
        {
            RemoveExisting(name);
            if (!string.IsNullOrEmpty(value))
            {
                _vault.Add(new PasswordCredential(Resource, name, value));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "写入 Windows 凭据库失败，改为仅内存保存");
            _fallback.Set(name, value);
        }
    }

    private void RemoveExisting(string name)
    {
        try
        {
            foreach (var credential in _vault!.FindAllByResource(Resource))
            {
                if (string.Equals(credential.UserName, name, StringComparison.OrdinalIgnoreCase))
                {
                    _vault.Remove(credential);
                }
            }
        }
        catch (Exception)
        {
            // 没有任何凭据时 FindAllByResource 会抛异常，属正常情况
        }
    }
}
