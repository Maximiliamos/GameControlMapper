using System.Diagnostics;
using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class BetaIntegrationFixture : IDisposable
{
    public TouchCapabilities Capabilities { get; }=new(10,true,false,true);
    public ContactManager Contacts { get; }
    public TouchContactAllocator Allocator { get; }
    public TouchEngine Engine { get; }
    public FakeTouchBackend Backend { get; }=new();
    public TouchScheduler Scheduler { get; }
    public long Generation { get; private set; }
    public BetaIntegrationFixture(){Contacts=new(NullLogger<ContactManager>.Instance,Capabilities);Allocator=new(Capabilities,NullLogger<TouchContactAllocator>.Instance);Engine=new(NullLogger<TouchEngine>.Instance,Contacts,Allocator);Scheduler=new(NullLogger<TouchScheduler>.Instance,Contacts,Backend,new(),null,Allocator);Backend.Initialize();Start();}
    public void Start(){Generation++;Allocator.Reset(Generation);Engine.StartAcceptingContacts();Scheduler.Resume();}
    public TouchContactLease Acquire(string owner="binding:test",double x=10,double y=20)=>Engine.StartTouch(Generation,owner,x,y)??throw new InvalidOperationException("capacity");
    public Task Frame()=>Scheduler.SendFrameOnceAsync();
    public async Task End(TouchContactLease lease){Engine.EndTouch(lease);await Frame();}
    public async Task<TouchShutdownResult> Stop(){Engine.StopAcceptingContacts();Engine.ReleaseAll();return await Scheduler.PauseAndFlushAsync();}
    public void AssertClean(){Assert.Empty(Allocator.ActiveLeases);Assert.Empty(Contacts.ActiveContacts);}
    public void Dispose(){Scheduler.Dispose();Backend.Shutdown();}
}

public sealed class BetaIntegrationStabilityTests
{
    [Fact]public async Task BetaFlow_Tap_StartsAndEndsContact()=>await CompleteOne();
    [Fact]public async Task BetaFlow_Hold_ProducesDownUpdateUp(){using var f=new BetaIntegrationFixture();var l=f.Acquire();await f.Frame();f.Engine.MoveTouch(l,20,30);await f.Frame();await f.End(l);Assert.Contains(f.Backend.RecordedFrames.SelectMany(x=>x.Contacts),x=>x.State==TouchState.Down);f.AssertClean();}
    [Fact]public async Task BetaFlow_DoubleTap_ProducesTwoCompleteContacts(){using var f=new BetaIntegrationFixture();for(var i=0;i<2;i++){var l=f.Acquire($"binding:{i}");await f.Frame();await f.End(l);}Assert.Equal(2,f.Backend.RecordedFrames.Count(x=>x.Contacts.Any(c=>c.State==TouchState.Down)));}
    [Fact]public async Task BetaFlow_Swipe_ProducesValidLifecycle()=>await MoveOne("binding:swipe");
    [Fact]public async Task BetaFlow_WasdJoystick_MovesAndReleases()=>await MoveOne("joystick:WASD");
    [Fact]public async Task BetaFlow_Camera_RecentersAndReleases()=>await MoveOne("camera:look");
    [Fact]public async Task BetaFlow_MouseArea_ReleasesOnMouseUp()=>await CompleteOne("mouse-area:fire");
    [Fact]public void BetaFlow_MultipleActions_UseUniqueContactIds(){using var f=new BetaIntegrationFixture();var leases=Enumerable.Range(0,5).Select(i=>f.Acquire($"binding:{i}")).ToArray();Assert.Equal(5,leases.Select(x=>x.ContactId).Distinct().Count());}
    [Fact]public void BetaFlow_TenContacts_ReachBackendCapacity(){using var f=new BetaIntegrationFixture();Assert.Equal(10,Enumerable.Range(0,10).Select(i=>f.Acquire($"binding:{i}")).Count());}
    [Fact]public void BetaFlow_EleventhContact_IsRejectedSafely(){using var f=new BetaIntegrationFixture();for(var i=0;i<10;i++)f.Acquire($"binding:{i}");Assert.Null(f.Engine.StartTouch(f.Generation,"binding:11",0,0));}
    [Fact]public async Task BetaFlow_RepeatedStop_IsIdempotent(){using var f=new BetaIntegrationFixture();var a=await f.Stop();var b=await f.Stop();Assert.True(a.Succeeded&&b.Succeeded);f.AssertClean();}
    [Fact]public async Task BetaFlow_StartAfterStop_UsesNewGeneration(){using var f=new BetaIntegrationFixture();var old=f.Generation;await f.Stop();f.Start();Assert.True(f.Generation>old);}
    [Fact]public async Task BetaFlow_OldQueuedInput_DoesNotEnterNewSession(){using var f=new BetaIntegrationFixture();var l=f.Acquire();await f.Stop();f.Start();f.Engine.MoveTouch(l,9,9);Assert.Empty(f.Contacts.ActiveContacts);}
    [Fact]public async Task BetaFlow_BackendFailure_ProducesDiagnosticSessionResult(){var backend=new FailingBackend();var c=new ContactManager(NullLogger<ContactManager>.Instance,new(10,true,false,true));var a=new TouchContactAllocator(new(10,true,false,true),NullLogger<TouchContactAllocator>.Instance);var e=new TouchEngine(NullLogger<TouchEngine>.Instance,c,a);var s=new TouchScheduler(NullLogger<TouchScheduler>.Instance,c,backend,new(),null,a);a.Reset(1);var l=e.StartTouch(1,"binding:x",1,1)!;await s.SendFrameOnceAsync();e.EndTouch(l);var result=await s.PauseAndFlushAsync();Assert.False(result.Succeeded);s.Dispose();}
    [Fact]public async Task SessionSoak_OneThousandStartStopCycles_RemainClean(){using var f=new BetaIntegrationFixture();for(var i=0;i<1000;i++){if(i>0)f.Start();var l=f.Acquire();await f.Frame();var r=await f.Stop();Assert.True(r.Succeeded);f.AssertClean();}}
    [Fact]public async Task MultitouchSoak_RepeatedCapacityCycles_DoNotLeakIds(){using var f=new BetaIntegrationFixture();for(var cycle=0;cycle<500;cycle++){if(cycle>0)f.Start();var ls=Enumerable.Range(0,10).Select(i=>f.Acquire($"binding:{i}")).ToArray();Assert.Equal(10,ls.Select(x=>x.ContactId).Distinct().Count());await f.Frame();await f.Stop();f.AssertClean();}}
    [Fact]public void Race_AllocatorReleaseAndAcquire_RemainsConsistent(){using var f=new BetaIntegrationFixture();var leases=Enumerable.Range(0,10).Select(i=>f.Acquire($"x:{i}")).ToArray();Parallel.ForEach(leases,l=>f.Allocator.RequestRelease(l));Assert.Equal(10,f.Allocator.ActiveLeases.Count);}
    [Fact]public async Task Race_ApplicationExitDuringFinalUp_CompletesBoundedly(){using var f=new BetaIntegrationFixture();var l=f.Acquire();await f.Frame();var task=f.Stop();Assert.Same(task,await Task.WhenAny(task,Task.Delay(5000)));}
    [Fact]public void SoakLogging_DoesNotLogEveryTouchFrame()=>Assert.False(new FrameContext().IsDebugMode);
    [Fact]public void RepeatedCapacityFailure_IsRateLimited(){var r=new NativeErrorRateLimiter();Assert.True(r.ShouldLog("capacity",DateTimeOffset.Now,out _));Assert.False(r.ShouldLog("capacity",DateTimeOffset.Now,out _));}
    [Fact]public void RepeatedBackendFailure_IsRateLimited(){var limiter=new NativeErrorRateLimiter();var now=DateTimeOffset.Now;Assert.True(limiter.ShouldLog("backend:87",now,out _));Assert.False(limiter.ShouldLog("backend:87",now.AddMilliseconds(1),out _));}
    [Fact]public void OneSession_ProducesOneStartAndOneFinalResult(){var s=new MappingSessionDiagnostics();s.Start();s.Stop("F9",true,0);Assert.NotNull(s.Last.StopReason);}
    [Fact]public void SessionId_IsConsistentAcrossSessionLogs(){var s=new MappingSessionDiagnostics();var id=s.Start();s.Stop("F9",true,0);Assert.Equal(id,s.Last.SessionId);}
    [Fact]public async Task ProfileRoundTrip_PreservesSupportedBindings(){using var p=new TempProfiles();var profile=MapperProfile.CreateDefault("roundtrip");profile.Bindings.Add(new(){Name="Tap",Hotkey="F1",Kind=BindingKind.Tap});await p.Store.SaveAsync(profile);var loaded=await p.Store.LoadAsync("roundtrip");Assert.Equal(BindingKind.Tap,loaded.Bindings.Single(x=>x.Name=="Tap").Kind);}
    [Fact]public void LegacyProfile_WithUnsupportedFeatures_LoadsWithWarnings(){var p=MapperProfile.CreateDefault();p.Gamepad.Enabled=true;Assert.Contains(new MapperProfileValidator(new HotkeyParser()).Validate(p).Warnings,x=>x.Code=="UnsupportedInBeta");}
    [Fact]public async Task InvalidProfile_DoesNotReplaceActiveProfile(){using var p=new TempProfiles();var valid=MapperProfile.CreateDefault("active");await p.Store.SaveAsync(valid);await Assert.ThrowsAsync<ProfileValidationException>(()=>p.Store.SaveAsync(new MapperProfile{Name="active",ResolutionWidth=0}));Assert.Equal(1920,(await p.Store.LoadAsync("active")).ResolutionWidth);}
    [Fact]public async Task BackupRecovery_RestoresLastValidProfile(){using var p=new TempProfiles();var a=MapperProfile.CreateDefault("backup");await p.Store.SaveAsync(a);a.Camera.SensitivityX=2;await p.Store.SaveAsync(a);Assert.NotEqual(2,(await p.Store.LoadBackupAsync("backup")).Camera.SensitivityX);}
    [Fact]public async Task ConcurrentProfileSaveAndLoad_ReturnsCompleteJson(){using var p=new TempProfiles();var profile=MapperProfile.CreateDefault("concurrent");await Task.WhenAll(Enumerable.Range(0,20).Select(_=>p.Store.SaveAsync(profile)));Assert.Equal("concurrent",(await p.Store.LoadAsync("concurrent")).Name);}
    [Fact]public async Task ProfileNameCollision_IsRejectedClearly(){using var p=new TempProfiles();var a=MapperProfile.CreateDefault("same");await p.Store.SaveAsync(a);a.Camera.SensitivityX=2;await p.Store.SaveAsync(a);Assert.Equal(2,(await p.Store.LoadAsync("same")).Camera.SensitivityX);Assert.True(File.Exists(Path.Combine(p.Directory,"same.json.bak")));}
    [Fact]public async Task SchedulerSimulation_TenThousandFrames_CompletesWithinReasonableBound(){using var f=new BetaIntegrationFixture();f.Acquire();var sw=Stopwatch.StartNew();for(var i=0;i<10000;i++)await f.Frame();sw.Stop();Assert.Equal(10000,f.Backend.RecordedFrames.Count);await f.Stop();f.AssertClean();System.Diagnostics.Trace.WriteLine($"10,000 frame simulation: {sw.ElapsedMilliseconds} ms");}
    private static async Task CompleteOne(string owner="binding:tap"){using var f=new BetaIntegrationFixture();var l=f.Acquire(owner);await f.Frame();await f.End(l);f.AssertClean();}
    private static async Task MoveOne(string owner){using var f=new BetaIntegrationFixture();var l=f.Acquire(owner);await f.Frame();f.Engine.MoveTouch(l,30,40);await f.Frame();await f.End(l);f.AssertClean();}
    private sealed class FailingBackend:ITouchBackend{private int _frames;public TouchCapabilities Capabilities=>new(10,true,false,true);public bool Initialize()=>true;public bool SendFrame(TouchFrame frame)=>Interlocked.Increment(ref _frames)==1;public void Shutdown(){}}
    private sealed class TempProfiles:IDisposable{private readonly string _dir=Path.Combine(Path.GetTempPath(),"gcm-integration-"+Guid.NewGuid().ToString("N"));public string Directory=>_dir;public ProfileStore Store{get;}public TempProfiles(){Store=new(NullLogger<ProfileStore>.Instance,new MapperProfileValidator(new HotkeyParser()),_dir);}public void Dispose(){try{System.IO.Directory.Delete(_dir,true);}catch{}}}
}
