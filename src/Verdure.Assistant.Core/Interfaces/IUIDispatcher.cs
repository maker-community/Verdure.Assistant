namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// Platform-agnostic UI thread dispatcher interface
/// Provides thread marshaling capabilities without platform-specific dependencies
/// </summary>
public interface IUIDispatcher
{
    /// <summary>
    /// Executes the specified action on the UI thread asynchronously
    /// </summary>
    /// <param name="action">The action to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Executes the specified function on the UI thread asynchronously
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="function">The function to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation with result</returns>
    Task<T> InvokeAsync<T>(Func<T> function);

    /// <summary>
    /// Executes the specified asynchronous action on the UI thread
    /// </summary>
    /// <param name="asyncAction">The asynchronous action to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task InvokeAsync(Func<Task> asyncAction);

    /// <summary>
    /// Executes the specified asynchronous function on the UI thread
    /// </summary>
    /// <typeparam name="T">The return type of the function</typeparam>
    /// <param name="asyncFunction">The asynchronous function to execute on the UI thread</param>
    /// <returns>A task that represents the asynchronous operation with result</returns>
    Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunction);

    /// <summary>
    /// Gets a value indicating whether the current thread is the UI thread
    /// </summary>
    bool IsUIThread { get; }
}
