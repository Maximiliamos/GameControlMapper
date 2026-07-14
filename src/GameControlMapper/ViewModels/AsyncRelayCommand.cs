using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly ILogger? _logger;
    private readonly string _operationName;
    private readonly Action<string>? _showError;
    private int _isRunning;

    public AsyncRelayCommand(
        Func<object?, Task> execute,
        Predicate<object?>? canExecute = null,
        ILogger? logger = null,
        string? operationName = null,
        Action<string>? showError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _logger = logger;
        _operationName = string.IsNullOrWhiteSpace(operationName) ? "UI command" : operationName;
        _showError = showError;
    }

    public event EventHandler? CanExecuteChanged;
    public event EventHandler<CommandExecutionFailedEventArgs>? ExecutionFailed;

    public bool CanExecute(object? parameter)
    {
        return Volatile.Read(ref _isRunning) == 0 && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        await ExecuteAsync(parameter).ConfigureAwait(true);
    }

    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!(_canExecute?.Invoke(parameter) ?? true) ||
            Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return;
        }

        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("{Operation} was cancelled", _operationName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{Operation} failed", _operationName);
            NotifyFailure(ex);
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
            RaiseCanExecuteChanged();
        }
    }

    private void NotifyFailure(Exception exception)
    {
        try
        {
            ExecutionFailed?.Invoke(this, new CommandExecutionFailedEventArgs(exception));
        }
        catch (Exception handlerException)
        {
            _logger?.LogError(handlerException, "Failure observer for {Operation} failed", _operationName);
        }

        try
        {
            _showError?.Invoke("Не удалось выполнить действие. Подробности записаны в журнал.");
        }
        catch (Exception handlerException)
        {
            _logger?.LogError(handlerException, "User error handler for {Operation} failed", _operationName);
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record CommandExecutionFailedEventArgs(Exception Exception);
