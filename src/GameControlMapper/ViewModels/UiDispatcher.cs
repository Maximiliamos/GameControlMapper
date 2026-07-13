using System.Windows.Threading;

namespace GameControlMapper.ViewModels;

public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
        }
    }
}
