using System.IO.Compression;
using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;
public sealed class ProductionDiagnosticsTests
{
 [Fact]public void ProductionLogging_KeySequenceIsNotPersisted()=>AssertPrivateLogRejected("KeyDown W; KeyUp W");
 [Fact]public void ProductionLogging_VirtualKeyCodesAreNotPersisted()=>AssertPrivateLogRejected("virtualKey=87 vkCode=87 scanCode=17");
 [Fact]public void ProductionLogging_MouseButtonsAreNotPersisted()=>AssertPrivateLogRejected("MouseDown Left; MouseUp Left");
 [Fact]public void ProductionLogging_BindingNamesAreNotPersistedFromInputPath()=>AssertPrivateLogRejected("HandlePressedInput binding SECRET_BINDING");
 [Fact]public void ProductionLogging_JoystickCoordinatesAreNotPersisted()=>AssertPrivateLogRejected("Joystick center X=100 Y=200 scaled center=300,400");
 [Fact]public void ProductionLogging_CameraDeltasAreNotPersisted()=>AssertPrivateLogRejected("Camera delta dx=12 dy=-4");
 [Fact]public void ProductionLogging_SchedulerFramesAreNotPersisted()=>AssertPrivateLogRejected("Scheduler FRAME 42 X=10 Y=20");
 [Fact]public void ProductionLogging_SessionStartIsPersisted(){using var f=new F();Logger(f).LogInformation("Mapping session abc123 started");f.Log.Flush();Assert.Contains("Mapping session abc123 started",ReadLog(f));}
 [Fact]public void ProductionLogging_StopReasonIsPersisted(){using var f=new F();Logger(f).LogInformation("Mapping session stopped. Reason=focus loss");f.Log.Flush();Assert.Contains("Reason=focus loss",ReadLog(f));}
 [Fact]public void ProductionLogging_BackendFailureIsPersisted(){using var f=new F();Logger(f).LogError("Touch backend initialization failed with Win32 error 87");f.Log.Flush();Assert.Contains("Win32 error 87",ReadLog(f));}
 [Fact]public async Task DiagnosticZip_InputHistoryIsAbsent(){using var f=new F();await File.WriteAllTextAsync(Path.Combine(f.Dir,"game-control-mapper.log.injected"),"KeyDown W\nvirtualKey=87\nMouseUp Left\nCamera delta dx=5");var z=await f.Export();var all=ReadAll(z);Assert.DoesNotContain("KeyDown",all,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("virtualKey",all,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("MouseUp",all,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("dx=5",all,StringComparison.OrdinalIgnoreCase);}
 [Fact]public async Task DiagnosticZip_ProfileJsonIsAbsent(){using var f=new F();await File.WriteAllTextAsync(Path.Combine(f.Dir,"private-profile.json"),"SECRET_PROFILE_CONTENT");var z=await f.Export();Assert.DoesNotContain(".json",string.Join("\n",Entries(z)),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("SECRET_PROFILE_CONTENT",ReadAll(z));}
 [Fact]public async Task DiagnosticZip_UserPathIsRedacted(){using var f=new F();var privatePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"private","file.txt");f.Log.Write(DateTimeOffset.Now,"Error","Test",$"Failure at {privatePath}");var z=await f.Export();var all=ReadAll(z);Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),all,StringComparison.OrdinalIgnoreCase);Assert.Contains("%USERPROFILE%",all);}
 [Fact]public void TenThousandJoystickUpdates_ProduceBoundedLogSize(){using var f=new F();var logger=Logger(f);logger.LogInformation("Mapping session bounded started");for(var i=0;i<10_000;i++)logger.LogInformation("Joystick center X={X} Y={Y} scaled center={SX},{SY}",i,i+1,i+2,i+3);f.Log.Flush();Assert.True(new FileInfo(f.Log.CurrentPath).Length<4096);}
 [Fact]public void FileLogger_WritesStructuredEntry(){using var f=new F();f.Log.Write(DateTimeOffset.Parse("2025-01-01T00:00:00Z"),"Information","Test","hello");f.Log.Flush();Assert.Contains("[Information] Test: hello",File.ReadAllText(f.Log.CurrentPath));}
 [Fact]public void FileLogger_WritesExceptionStackTrace(){using var f=new F();f.Log.Write(DateTimeOffset.Now,"Error","Test","failed",new InvalidOperationException("boom"));f.Log.Flush();Assert.Contains("InvalidOperationException",File.ReadAllText(f.Log.CurrentPath));}
 [Fact]public void FileLogger_IsThreadSafe(){using var f=new F();Parallel.For(0,100,i=>f.Log.Write(DateTimeOffset.Now,"Info","T",$"m{i}"));f.Log.Flush();Assert.True(File.ReadLines(f.Log.CurrentPath).Count()>=100);}
 [Fact]public void FileLogger_RotatesAtSizeLimit(){using var f=new F(300);for(var i=0;i<30;i++)f.Log.Write(DateTimeOffset.Now,"Info","T",new string('x',80));f.Log.Flush();Assert.True(File.Exists(f.Log.CurrentPath+".1"));}
 [Fact]public void FileLogger_KeepsAtMostThreeArchives(){using var f=new F(256);for(var i=0;i<100;i++)f.Log.Write(DateTimeOffset.Now,"Info","T",new string('x',100));f.Log.Flush();Assert.True(Directory.GetFiles(f.Dir,"game-control-mapper.log.*").Count(p=>!p.Equals(f.Log.CurrentPath,StringComparison.OrdinalIgnoreCase))<=3);}
 [Fact]public void FileLogger_WriteFailureDoesNotCrashCaller(){var path=Path.GetTempFileName();using var l=new FileLogSink(path);l.Write(DateTimeOffset.Now,"I","T","x");l.Flush();File.Delete(path);}
 [Fact]public void FileLogger_FlushesOnShutdown(){var f=new F();f.Log.Write(DateTimeOffset.Now,"I","T","flush");f.Log.Dispose();Assert.Contains("flush",File.ReadAllText(f.Log.CurrentPath));f.DisposeDirectory();}
 [Fact]public void MappingStart_CreatesSessionId(){var s=new MappingSessionDiagnostics();Assert.Equal(8,s.Start().Length);}
 [Fact]public void RepeatedStart_UsesDifferentSessionId(){var s=new MappingSessionDiagnostics();Assert.NotEqual(s.Start(),s.Start());}
 [Fact]public void Stop_LogsSessionResult(){var s=new MappingSessionDiagnostics();s.Start();s.Stop("F9",true,0);Assert.True(s.Last.ReleaseSucceeded);}
 [Fact]public void FocusLoss_LogsStopReason()=>Reason("focus loss");
 [Fact]public void GeometryInvalidation_LogsStopReason()=>Reason("geometry invalidation");
 [Fact]public void BackendFailure_LogsWin32Message(){var e=new System.ComponentModel.Win32Exception(87);Assert.False(string.IsNullOrWhiteSpace(e.Message));}
 [Fact]public void RepeatedNativeError_IsRateLimited(){var r=new NativeErrorRateLimiter(TimeSpan.FromMinutes(1));Assert.True(r.ShouldLog("x",DateTimeOffset.Now,out _));Assert.False(r.ShouldLog("x",DateTimeOffset.Now,out _));}
 [Fact]public async Task DispatcherException_IsRecorded()=>await Crash("Dispatcher");
 [Fact]public async Task BackgroundException_IsRecorded()=>await Crash("AppDomain");
 [Fact]public async Task UnobservedTaskException_IsRecorded()=>await Crash("TaskScheduler");
 [Fact]public async Task ExceptionHandler_DoesNotReenterRecursively(){var entered=new TaskCompletionSource();var release=new TaskCompletionSource();var c=new CrashHandlingService(NullLogger<CrashHandlingService>.Instance,async()=>{entered.SetResult();await release.Task;},()=>{});var first=c.HandleAsync("a",new Exception());await entered.Task;await c.HandleAsync("b",new Exception());release.SetResult();await first;Assert.Single(c.Exceptions);}
 [Fact]public async Task CrashHandling_AttemptsGracefulStop(){var stop=false;var c=new CrashHandlingService(NullLogger<CrashHandlingService>.Instance,()=>{stop=true;return Task.CompletedTask;},()=>{});await c.HandleAsync("x",new Exception());Assert.True(stop);}
 [Fact]public async Task CrashHandling_AttemptsCursorRestore(){var restore=false;var c=new CrashHandlingService(NullLogger<CrashHandlingService>.Instance,()=>Task.CompletedTask,()=>restore=true);await c.HandleAsync("x",new Exception());Assert.True(restore);}
 [Fact]public async Task DiagnosticExport_ContainsRequiredMetadata(){using var f=new F();var z=await f.Export();Assert.Contains("metadata.txt",Entries(z));}
 [Fact]public async Task DiagnosticExport_DoesNotContainProfileContents(){using var f=new F();var z=await f.Export();Assert.DoesNotContain("SECRET_PROFILE_CONTENT",ReadAll(z));}
 [Fact]public async Task DiagnosticExport_DoesNotContainPressedKeys(){using var f=new F();var z=await f.Export();Assert.DoesNotContain("pressedKeys",ReadAll(z),StringComparison.OrdinalIgnoreCase);}
 [Fact]public void DiagnosticExport_RedactsUserPaths(){var p=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)+"\\secret.txt";Assert.StartsWith("%USERPROFILE%",FileLogSink.Redact(p));}
 [Fact]public async Task DiagnosticExport_IncludesRecentLogs(){using var f=new F();f.Log.Write(DateTimeOffset.Now,"I","T","recent-marker");var z=await f.Export();Assert.Contains("recent-marker",ReadAll(z));}
 [Fact]public async Task DiagnosticExport_FailureDoesNotDamageLogs(){using var f=new F();f.Log.Write(DateTimeOffset.Now,"I","T","keep");f.Log.Flush();await Assert.ThrowsAnyAsync<Exception>(()=>f.Exporter.ExportAsync(f.Dir));Assert.Contains("keep",File.ReadAllText(f.Log.CurrentPath));}
 [Fact]public void Shutdown_WhenLoggerFails_StillCompletes()=>FileLogger_WriteFailureDoesNotCrashCaller();
 [Fact]public void SessionLogging_DoesNotLogEverySchedulerFrame(){var s=new MappingSessionDiagnostics();s.Start();s.Stop("stop",true,0);Assert.Equal("stop",s.Last.StopReason);}
 private static ILogger Logger(F fixture)=>new AppLoggerProvider(new AppLogSink(),fixture.Log).CreateLogger("GameControlMapper.Tests.Privacy");
 private static string ReadLog(F fixture)=>File.Exists(fixture.Log.CurrentPath)?File.ReadAllText(fixture.Log.CurrentPath):string.Empty;
 private static void AssertPrivateLogRejected(string message){using var f=new F();var logger=Logger(f);logger.LogInformation("Mapping session privacy-test started");logger.LogInformation("{PrivateInput}",message);f.Log.Flush();var text=ReadLog(f);Assert.Contains("privacy-test",text);Assert.DoesNotContain(message,text,StringComparison.OrdinalIgnoreCase);}
 private static void Reason(string reason){var s=new MappingSessionDiagnostics();s.Start();s.Stop(reason,true,0);Assert.Equal(reason,s.Last.StopReason);}
 private static async Task Crash(string source){var c=new CrashHandlingService(NullLogger<CrashHandlingService>.Instance,()=>Task.CompletedTask,()=>{});await c.HandleAsync(source,new Exception("x"));Assert.Contains(source,c.Exceptions[0]);}
 private static string[] Entries(string z){using var a=ZipFile.OpenRead(z);return a.Entries.Select(e=>e.FullName).ToArray();}private static string ReadAll(string z){using var a=ZipFile.OpenRead(z);return string.Join("\n",a.Entries.Select(e=>{using var r=new StreamReader(e.Open());return r.ReadToEnd();}));}
 private sealed class F:IDisposable{public string Dir=Path.Combine(Path.GetTempPath(),"gcm-log-"+Guid.NewGuid().ToString("N"));public FileLogSink Log;public DiagnosticExportService Exporter;public F(long max=1024*1024){Directory.CreateDirectory(Dir);Log=new(Dir,max);Exporter=new(Log,new Profiles(),new MappingSessionDiagnostics());}public async Task<string> Export(){var z=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".zip");await Exporter.ExportAsync(z);return z;}public void Dispose(){Log.Dispose();DisposeDirectory();}public void DisposeDirectory(){try{Directory.Delete(Dir,true);}catch{}}}
 private sealed class Profiles:IProfileStore{public Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken c=default)=>Task.FromResult<IReadOnlyList<string>>(["SafeProfile"]);public Task<MapperProfile> LoadAsync(string n,CancellationToken c=default)=>throw new NotSupportedException();public Task SaveAsync(MapperProfile p,CancellationToken c=default)=>throw new NotSupportedException();public Task DeleteAsync(string n,CancellationToken c=default)=>throw new NotSupportedException();public Task<string> ExportAsync(MapperProfile p,string t,CancellationToken c=default)=>throw new NotSupportedException();public Task<MapperProfile> ImportAsync(string s,CancellationToken c=default)=>throw new NotSupportedException();}
}
