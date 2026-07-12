using System.Collections.ObjectModel;

namespace GameControlMapper.Services;

public sealed class AppLogSink
{
    public ObservableCollection<string> Entries { get; } = [];

    public void Add(string message)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Entries.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
            while (Entries.Count > 300)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        });
    }
}
