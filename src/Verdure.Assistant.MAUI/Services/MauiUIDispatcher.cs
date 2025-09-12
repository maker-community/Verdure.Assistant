using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.MAUI.Services;

/// <summary>
/// MAUI平台的UI调度器实现
/// </summary>
public class MauiUIDispatcher : IUIDispatcher
{
    /// <summary>
    /// Gets a value indicating whether the current thread is the UI thread
    /// </summary>
    public bool IsUIThread => Application.Current?.Dispatcher.IsDispatchRequired == false;

    /// <summary>
    /// Executes the specified action on the UI thread asynchronously
    /// </summary>
    /// <param name="action">The action to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task InvokeAsync(Action action)
    {
        return Application.Current?.Dispatcher.DispatchAsync(action) ?? Task.CompletedTask;
    }

    /// <summary>
    /// Executes the specified function on the UI thread asynchronously
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="func">The function to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation with result</returns>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        
        Application.Current?.Dispatcher.Dispatch(() =>
        {
            try
            {
                var result = func();
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
        var tcs = new TaskCompletionSource();
        Application.Current?.Dispatcher.Dispatch(async () =>
        {
            try
            {
                await asyncAction();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Executes the specified asynchronous function on the UI thread
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="asyncFunction">The asynchronous function to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation with result</returns>
    public Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunction)
    {
        var tcs = new TaskCompletionSource<T>();
        Application.Current?.Dispatcher.Dispatch(async () =>
        {
            try
            {
                var result = await asyncFunction();
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
