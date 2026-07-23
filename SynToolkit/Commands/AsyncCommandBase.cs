#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SynToolkit.Commands;

/// <summary>
/// Provides a small, application-owned base for asynchronous UI commands.
/// </summary>
/// <remarks>
/// Command execution is single-flight: a second invocation is ignored until the
/// current invocation finishes. Exceptions are logged and contained because an
/// exception escaping <see cref="ICommand.Execute"/> would otherwise terminate
/// the UI process.
/// </remarks>
public abstract class AsyncCommandBase : ICommand
{
    private int _isExecuting;

    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !IsExecuting && CanExecuteCore(parameter);

    public async void Execute(object? parameter)
    {
        if (!CanExecuteCore(parameter) ||
            Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        RaiseCanExecuteChangedSafely();

        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception exception)
        {
            LogFailure(exception, "An asynchronous command failed.");
        }
        finally
        {
            Volatile.Write(ref _isExecuting, 0);
            RaiseCanExecuteChangedSafely();
        }
    }

    protected virtual bool CanExecuteCore(object? parameter) => true;

    protected abstract Task ExecuteAsync(object? parameter);

    protected void RaiseCanExecuteChanged() => RaiseCanExecuteChangedSafely();

    private void RaiseCanExecuteChangedSafely()
    {
        EventHandler? handlers = CanExecuteChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                LogFailure(exception, "A CanExecuteChanged subscriber failed.");
            }
        }
    }

    private static void LogFailure(Exception exception, string message)
    {
        try
        {
            App.logger.Error(exception, message);
        }
        catch
        {
            // Logging must never turn a contained command failure into a process crash.
        }
    }
}
