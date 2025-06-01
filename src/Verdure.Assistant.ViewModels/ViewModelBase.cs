using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.ViewModels;

/// <summary>
/// ViewModel基类，提供通用功能
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    protected readonly ILogger _logger;

    protected ViewModelBase(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 异步初始化方法，由派生类重写
    /// </summary>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清理资源方法，由派生类重写
    /// </summary>
    public virtual void Cleanup()
    {
        // 基类无需清理
    }
}
