using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace GameControlMapper.Services;
public sealed class FileLogSink : IDisposable
{
    private const int QueueCapacity = 2048;
    private readonly string _directory;private readonly long _maxBytes;private readonly BlockingCollection<string> _queue=new(QueueCapacity);private readonly Task _worker;private readonly object _gate=new();private StreamWriter? _writer;private int _disposed;
    private int _pending;
    private long _droppedEntries;
    public FileLogSink():this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GameControlMapper","Logs")){}
    public FileLogSink(string directory,long maxBytes=5*1024*1024){_directory=directory;_maxBytes=Math.Max(256,maxBytes);_worker=Task.Run(Consume);}
    public string DirectoryPath=>_directory;public string CurrentPath=>Path.Combine(_directory,"game-control-mapper.log");public long DroppedEntries=>Interlocked.Read(ref _droppedEntries);
    public void Write(DateTimeOffset timestamp,string level,string category,string message,Exception? exception=null)
    {
        if(Volatile.Read(ref _disposed)!=0||ProductionLogPrivacy.ContainsInputHistory(message)||ProductionLogPrivacy.ContainsInputHistory(exception?.ToString()))return;
        var text=$"{timestamp:O} [{level}] {category}: {Redact(message)}"+(exception is null?"":Environment.NewLine+Redact(exception.ToString()));
        try
        {
            Interlocked.Increment(ref _pending);
            if(!_queue.TryAdd(text)){Interlocked.Decrement(ref _pending);Interlocked.Increment(ref _droppedEntries);}
        }
        catch{Interlocked.Decrement(ref _pending);Interlocked.Increment(ref _droppedEntries);}
    }
    private void Consume(){try{Directory.CreateDirectory(_directory);foreach(var line in _queue.GetConsumingEnumerable()){try{lock(_gate){EnsureWriter();RotateIfNeeded(line);_writer!.WriteLine(line);}}catch{}finally{Interlocked.Decrement(ref _pending);}}}catch{Interlocked.Exchange(ref _pending,0);}finally{try{lock(_gate){_writer?.Flush();_writer?.Dispose();_writer=null;}}catch{}}}
    private void EnsureWriter()=>_writer??=new StreamWriter(new FileStream(CurrentPath,FileMode.Append,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)){AutoFlush=false};
    private void RotateIfNeeded(string next){if((_writer?.BaseStream.Length??0)+Encoding.UTF8.GetByteCount(next)+2<=_maxBytes)return;_writer?.Flush();_writer?.Dispose();_writer=null;for(var i=3;i>=1;i--){var src=i==1?CurrentPath:$"{CurrentPath}.{i-1}";var dst=$"{CurrentPath}.{i}";try{if(File.Exists(dst))File.Delete(dst);if(File.Exists(src))File.Move(src,dst);}catch{}}EnsureWriter();}
    public void Flush(){try{SpinWait.SpinUntil(()=>Volatile.Read(ref _pending)==0,TimeSpan.FromSeconds(3));lock(_gate){_writer?.Flush();_writer?.Dispose();_writer=null;}}catch{}}
    public bool WriteCrashReport(string source,Exception exception){try{Directory.CreateDirectory(_directory);File.WriteAllText(Path.Combine(_directory,"crash-report.txt"),$"{DateTimeOffset.Now:O}\nSource: {source}\nType: {exception.GetType().FullName}\n{Redact(exception.ToString())}",new UTF8Encoding(false));return true;}catch{return false;}}
    public void WriteCrashHandlerFallback(string source,Exception exception){try{Directory.CreateDirectory(_directory);File.WriteAllText(Path.Combine(_directory,"crash-handler-fallback.txt"),$"{DateTimeOffset.Now:O}\nSource: {Redact(source)}\nHandler failure: {exception.GetType().FullName}\n{Redact(exception.Message)}",new UTF8Encoding(false));}catch{}}
    public static string Redact(string value){var user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);if(!string.IsNullOrEmpty(user))value=value.Replace(user,"%USERPROFILE%",StringComparison.OrdinalIgnoreCase);return Regex.Replace(value,@"[A-Za-z]:\\Users\\[^\\\s]+","%USERPROFILE%",RegexOptions.IgnoreCase);}
    public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)!=0)return;try{_queue.CompleteAdding();_worker.Wait(TimeSpan.FromSeconds(3));}catch{}Flush();_queue.Dispose();}
}
