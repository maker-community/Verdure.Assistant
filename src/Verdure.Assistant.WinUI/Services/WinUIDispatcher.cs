using Microsoft.UI.Dispatching;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.WinUI.Services;

/// <summary>
/// WinUI-specific UI dispatcher implementation
/// Uses Microsoft.UI.Dispatching.DispatcherQueue for thread marshaling
/// </summary>
public class WinUIDispatcher : IUIDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>
    /// Initializes a new instance of the WinUIDispatcher class
    /// </summary>
    /// <param name="dispatcherQueue">The WinUI DispatcherQueue to use for thread marshaling</param>
    public WinUIDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    /// <summary>
    /// Gets a value indicating whether the current thread is the UI thread
    /// </summary>
    public bool IsUIThread => _dispatcherQueue.HasThreadAccess;

    /// <summary>
    /// Executes the specified action on the UI thread asynchronously
    /// </summary>
    /// <param name="action">The action to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task InvokeAsync(Action action)
    {
        if (action == null) return Task.CompletedTask;

        if (IsUIThread)
        {
            action.Invoke();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action.Invoke();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }    /// <summary>
    /// Executes the specified function on the UI thread asynchronously
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="function">The function to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation with result</returns>
    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        if (function == null) return Task.FromResult(default(T)!);

        if (IsUIThread)
        {
            var result = function.Invoke();
            return Task.FromResult(result);
        }

        var tcs = new TaskCompletionSource<T>();
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var result = function.Invoke();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Executes the specified asynchronous action on the UI thread
    /// </summary>
    /// <param name="asyncAction">The asynchronous action to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task InvokeAsync(Func<Task> asyncAction)
    {
        if (asyncAction == null) return Task.CompletedTask;

        if (IsUIThread)
        {
            return asyncAction.Invoke();
        }

        var tcs = new TaskCompletionSource<bool>();
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await asyncAction.Invoke();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }    /// <summary>
    /// Executes the specified asynchronous function on the UI thread
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="asyncFunction">The asynchronous function to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation with result</returns>
    public Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunction)
    {
        if (asyncFunction == null) return Task.FromResult(default(T)!);

        if (IsUIThread)
        {
            return asyncFunction.Invoke();
        }

        var tcs = new TaskCompletionSource<T>();
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await asyncFunction.Invoke();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}
