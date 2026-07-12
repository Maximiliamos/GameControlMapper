using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;
public sealed record MappingSessionSnapshot(string? SessionId,string? StopReason,bool? ReleaseSucceeded,int ActiveContacts,DateTimeOffset Timestamp);
public sealed class MappingSessionDiagnostics
{
    private MappingSessionSnapshot _last=new(null,null,null,0,DateTimeOffset.MinValue);public MappingSessionSnapshot Last=>Volatile.Read(ref _last);
    public string Start(){var id=Guid.NewGuid().ToString("N")[..8];Volatile.Write(ref _last,new(id,null,null,0,DateTimeOffset.Now));return id;}
    public void Stop(string reason,bool succeeded,int contacts)=>Volatile.Write(ref _last,new(Last.SessionId,reason,succeeded,contacts,DateTimeOffset.Now));
}
public sealed class NativeErrorRateLimiter
{
    private readonly object _gate=new();private readonly Dictionary<string,(int Count,DateTimeOffset Last)> _state=[];private readonly TimeSpan _window;
    public NativeErrorRateLimiter(TimeSpan? window=null)=>_window=window??TimeSpan.FromSeconds(10);
    public bool ShouldLog(string key,DateTimeOffset now,out int suppressed){lock(_gate){if(!_state.TryGetValue(key,out var s)||now-s.Last>=_window){suppressed=s.Count>0?s.Count-1:0;_state[key]=(1,now);return true;}_state[key]=(s.Count+1,s.Last);suppressed=0;return false;}}
}
public sealed class CrashHandlingService
{
    private readonly ILogger<CrashHandlingService> _logger;private readonly Func<Task> _stop;private readonly Action _restore;private readonly FileLogSink? _files;private int _handling;private readonly List<string> _exceptions=[];
    public CrashHandlingService(ILogger<CrashHandlingService> logger,Func<Task> stop,Action restore,FileLogSink? files=null){_logger=logger;_stop=stop;_restore=restore;_files=files;}
    public IReadOnlyList<string> Exceptions{get{lock(_exceptions)return _exceptions.ToArray();}}
    public async Task HandleAsync(string source,Exception exception)
    {if(Interlocked.Exchange(ref _handling,1)!=0)return;try{lock(_exceptions)_exceptions.Add($"{source}: {exception.GetType().Name}: {FileLogSink.Redact(exception.Message)}");_files?.WriteCrashReport(source,exception);_logger.LogCritical(exception,"Unhandled exception from {Source}",source);try{var task=_stop();await Task.WhenAny(task,Task.Delay(TimeSpan.FromSeconds(3)));}catch(Exception ex){_logger.LogError(ex,"Graceful crash stop failed");}try{_restore();}catch(Exception ex){_logger.LogError(ex,"Cursor restore during crash failed");}}finally{Volatile.Write(ref _handling,0);}}
}
public sealed class DiagnosticExportService
{
    private readonly FileLogSink _logs;private readonly IProfileStore _profiles;private readonly MappingSessionDiagnostics _session;private readonly CrashHandlingService? _crashes;
    public DiagnosticExportService(FileLogSink logs,IProfileStore profiles,MappingSessionDiagnostics session,CrashHandlingService? crashes=null){_logs=logs;_profiles=profiles;_session=session;_crashes=crashes;}
    public async Task ExportAsync(string zipPath,CancellationToken ct=default)
    {var temp=Path.Combine(Path.GetTempPath(),"gcm-diag-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);try{var asm=Assembly.GetEntryAssembly()??Assembly.GetExecutingAssembly();var names=await _profiles.ListProfilesAsync(ct);var metadata=new StringBuilder().AppendLine($"Version: {asm.GetName().Version}").AppendLine($"InformationalVersion: {asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}").AppendLine($"Commit: {asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}").AppendLine($"Windows: {Environment.OSVersion.VersionString}").AppendLine($".NET: {RuntimeInformation.FrameworkDescription}").AppendLine("DPI awareness: PerMonitorV2").AppendLine("Backend capabilities: maxContacts=10 touchInjection=true").AppendLine($"Last session: {_session.Last}").AppendLine($"Profiles: {string.Join(", ",names.Select(Path.GetFileName))}").AppendLine($"Profile backups present: {Directory.Exists(Path.Combine(AppContext.BaseDirectory,"Profiles"))&&Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,"Profiles"),"*.bak").Any()}").AppendLine("Target status: not captured when export is outside an active mapping session");await File.WriteAllTextAsync(Path.Combine(temp,"metadata.txt"),FileLogSink.Redact(metadata.ToString()),ct);await File.WriteAllTextAsync(Path.Combine(temp,"exceptions.txt"),string.Join(Environment.NewLine,_crashes?.Exceptions??[]),ct);await File.WriteAllTextAsync(Path.Combine(temp,"README.txt"),"Diagnostic metadata and recent application logs. Profile contents, pressed keys, process lists, and other window titles are intentionally excluded.",ct);_logs.Flush();if(Directory.Exists(_logs.DirectoryPath))foreach(var file in Directory.EnumerateFiles(_logs.DirectoryPath,"game-control-mapper.log*"))File.Copy(file,Path.Combine(temp,Path.GetFileName(file)),true);if(File.Exists(zipPath))File.Delete(zipPath);ZipFile.CreateFromDirectory(temp,zipPath,CompressionLevel.Fastest,false);}finally{try{Directory.Delete(temp,true);}catch{}}}
}
