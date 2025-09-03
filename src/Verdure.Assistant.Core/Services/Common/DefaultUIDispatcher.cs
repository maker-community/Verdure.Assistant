using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// Default UI dispatcher implementation that executes actions on the current thread
/// This is a fallback implementation for scenarios where no platform-specific dispatcher is available
/// </summary>
public class DefaultUIDispatcher : IUIDispatcher
{
    /// <summary>
    /// Gets a value indicating whether the current thread is the UI thread
    /// In the default implementation, we assume the current thread is always the UI thread
    /// </summary>
    public bool IsUIThread => true;

    /// <summary>
    /// Executes the specified action on the current thread
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <returns>A completed task</returns>
    public Task InvokeAsync(Action action)
    {
        action?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes the specified function on the current thread
    /// </summary>    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="function">The function to execute</param>
    /// <returns>A task that contains the result of the function</returns>
    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        var result = function != null ? function.Invoke() : default(T)!;
        return Task.FromResult(result);
    }

    /// <summary>
    /// Executes the specified asynchronous action on the current thread
    /// </summary>
    /// <param name="asyncAction">The asynchronous action to execute</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task InvokeAsync(Func<Task> asyncAction)
    {
        if (asyncAction != null)
        {
            await asyncAction.Invoke();
        }
    }    /// <summary>
    /// Executes the specified asynchronous function on the current thread
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="asyncFunction">The asynchronous function to execute</param>
    /// <returns>A task that represents the asynchronous operation with result</returns>
    public async Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunction)
    {
        if (asyncFunction != null)
        {
            return await asyncFunction.Invoke();
        }
        return default(T)!;
    }
}
