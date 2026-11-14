using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.ViewModels;
using Xunit;

namespace DeepSeek.Tests;

public class DeepSeekChatViewModelTests
{
    private sealed class MemorySettings : ISettingsService<AppSettings>
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler<AppSettings>? SettingsChanged;
        public Task<AppSettings> LoadSettingsAsync() => Task.FromResult(Current);
        public Task SaveSettingsAsync(AppSettings settings)
        {
            Current = settings;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }
        public Task<bool> ExportSettingsAsync(string filePath, AppSettings settings) => Task.FromResult(true);
        public Task<AppSettings?> ImportSettingsAsync(string filePath) => Task.FromResult<AppSettings?>(null);
        public Task ResetToDefaultAsync()
        {
            Current = new AppSettings();
            return Task.CompletedTask;
        }
        public AppSettings GetCurrentSettings() => Current;
    }

    private static (DeepSeekChatViewModel Vm, FakeHandler Handler, InMemorySecretStore Secrets, DeepSeekOptions Options)
        Create(string? apiKey)
    {
        var handler = new FakeHandler();
        var options = new DeepSeekOptions { RetryBaseDelayMs = 1, RetryMaxDelaySeconds = 1, MaxRetries = 0 };
        var client = new DeepSeekClient(new HttpClient(handler), options, delay: (_, _) => Task.CompletedTask);
        var session = new DeepSeekChatSession(client, options);
        var secrets = new InMemorySecretStore();
        if (apiKey != null)
        {
            secrets.Set(SecretNames.DeepSeekApiKey, apiKey);
        }

        var vm = new DeepSeekChatViewModel(
            NullLogger<DeepSeekChatViewModel>.Instance, session, options, client,
            tools: null, new MemorySettings(), secrets);
        return (vm, handler, secrets, options);
    }

    private static async Task SendAsync(DeepSeekChatViewModel vm, string text)
    {
        vm.InputText = text;
        await vm.SendCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task WithoutApiKey_OpensSettings_AndSendsNothing()
    {
        var (vm, handler, _, _) = Create(apiKey: null);
        await vm.InitializeAsync();

        Assert.False(vm.IsConfigured);
        Assert.True(vm.IsSettingsOpen);

        await SendAsync(vm, "你好");

        Assert.Empty(handler.Requests);
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public async Task Send_StreamsReasoningAndContent_IntoAssistantMessage()
    {
        var (vm, handler, _, _) = Create("sk-test");
        handler.EnqueueJson("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"12.30\"}]}");
        await vm.InitializeAsync();

        handler.EnqueueSse(TestData.TextSse("你好呀", reasoning: "用户在打招呼"));
        await SendAsync(vm, "你好");

        Assert.Equal(2, vm.Messages.Count);
        var assistant = vm.Messages[1];
        Assert.Equal("你好呀", assistant.Content);
        Assert.Equal("用户在打招呼", assistant.Reasoning);
        Assert.False(assistant.IsStreaming);
        Assert.False(assistant.IsReasoningExpanded);
        Assert.Contains("缓存命中 4", assistant.UsageText);
        Assert.False(vm.IsBusy);
        Assert.Equal("就绪", vm.StatusText);
    }

    [Fact]
    public async Task AuthenticationFailure_ShowsFriendlyError_AndKeepsAppUsable()
    {
        var (vm, handler, _, _) = Create("sk-wrong");
        handler.EnqueueError(HttpStatusCode.Unauthorized, "authentication_error", "Authentication Fails");
        await vm.InitializeAsync();
        await Task.Delay(50);

        handler.EnqueueError(HttpStatusCode.Unauthorized, "authentication_error", "Authentication Fails");
        await SendAsync(vm, "你好");

        var assistant = vm.Messages[^1];
        Assert.True(assistant.IsError);
        Assert.Contains("API Key", assistant.Footnote);
        Assert.False(vm.IsBusy);
        Assert.True(vm.SendCommand.CanExecute(null) == false, "输入框已清空，发送按钮应不可用");
    }

    [Fact]
    public async Task SaveSettings_StoresKeyInSecretStore_NotInSettings_AndAppliesToOptions()
    {
        var (vm, handler, secrets, options) = Create(apiKey: null);
        handler.EnqueueJson("{\"is_available\":true,\"balance_infos\":[]}");
        await vm.InitializeAsync();

        vm.ApiKeyInput = "sk-new-key";
        vm.Model = DeepSeekModelNames.Pro;
        vm.ThinkingEnabled = false;
        vm.ReasoningEffort = DeepSeekReasoningEffort.Max;
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.Equal("sk-new-key", secrets.Get(SecretNames.DeepSeekApiKey));
        Assert.Equal("sk-new-key", options.ApiKey);
        Assert.Equal(DeepSeekModelNames.Pro, options.Model);
        Assert.False(options.ThinkingEnabled);
        Assert.True(vm.IsConfigured);
        Assert.Equal(string.Empty, vm.ApiKeyInput);
    }

    [Fact]
    public async Task InvalidSettings_AreRejected_WithMessage()
    {
        var (vm, _, _, _) = Create("sk-test");
        await vm.InitializeAsync();

        vm.BaseUrl = "not a url";
        await vm.SaveSettingsCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.SettingsMessage));
        Assert.DoesNotContain("已保存", vm.SettingsMessage);
    }

    [Fact]
    public async Task NewChat_ClearsMessagesAndHistory()
    {
        var (vm, handler, _, _) = Create("sk-test");
        handler.EnqueueJson("{\"is_available\":true,\"balance_infos\":[]}");
        await vm.InitializeAsync();
        handler.EnqueueSse(TestData.TextSse("好的"));
        await SendAsync(vm, "记住 42");

        vm.NewChatCommand.Execute(null);

        Assert.Empty(vm.Messages);
    }

    [Fact]
    public void ClearApiKey_RemovesSecret()
    {
        var (vm, _, secrets, options) = Create("sk-test");
        vm.InitializeAsync().GetAwaiter().GetResult();

        vm.ClearApiKeyCommand.Execute(null);

        Assert.Null(secrets.Get(SecretNames.DeepSeekApiKey));
        Assert.False(vm.IsConfigured);
        Assert.Equal(string.Empty, options.ApiKey);
    }
}
