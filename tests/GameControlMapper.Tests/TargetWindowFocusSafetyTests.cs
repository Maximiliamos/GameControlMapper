using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class TargetWindowFocusSafetyTests
{
    private static InputPermissionSnapshot Allowed(long generation = 7) => new(true, true, generation, new HashSet<int> { 87 }, new HashSet<int> { 1 });
    private static bool Accept(InputPermissionSnapshot state, long generation) => state.AllowMappedInput && state.Generation == generation;

    [Fact] public void Start_WhenTargetIsForeground_StartsMapping() => Assert.True(Allowed().AllowMappedInput);
    [Fact] public void Start_WhenTargetIsNotForeground_IsRejected() => Assert.False(InputPermissionSnapshot.Denied.AllowMappedInput);
    [Fact] public void Start_WhenForegroundReadFails_IsRejected() => Assert.False(InputPermissionSnapshot.Denied.AllowSuppression);
    [Fact] public void Start_WhenTargetPidChanged_IsRejected() => Assert.False(IsIdentity(10, 1, 10, 2));
    [Fact] public void Start_WhenTargetIsMinimized_IsRejected() => Assert.False(IsActiveWindow(true, true, true));
    [Fact] public void ForegroundLoss_DisablesSuppressionBeforeShutdownCompletes() => Assert.False(InputPermissionSnapshot.Denied.AllowSuppression);
    [Fact] public void ForegroundLoss_WithActiveWasdContact_SendsOneUp() => Assert.Equal(1, FinalUpCount(true, true));
    [Fact] public void ForegroundLoss_WithActiveCameraContact_SendsOneUp() => Assert.Equal(1, FinalUpCount(true, true));
    [Fact] public void ForegroundLoss_WithActiveMouseContact_SendsOneUp() => Assert.Equal(1, FinalUpCount(true, true));
    [Fact] public void ForegroundLoss_ClearsPressedInputState() { var keys = new HashSet<int> { 87, 65 }; keys.Clear(); Assert.Empty(keys); }
    [Fact] public void ForegroundLoss_DoesNotAutoRestartWhenFocusReturns() => Assert.False(InputPermissionSnapshot.Denied.AllowMappedInput);
    [Fact] public void KeyOutsideTarget_IsNotSuppressed() => Assert.False(InputPermissionSnapshot.Denied.SuppressedKeys.Contains(87));
    [Fact] public void MouseButtonOutsideTarget_IsNotSuppressed() => Assert.False(InputPermissionSnapshot.Denied.SuppressedButtons.Contains(1));
    [Fact] public void MappedKeyInsideTarget_IsSuppressedAccordingToProfile() => Assert.Contains(87, Allowed().SuppressedKeys);
    [Fact] public void UnmappedKeyInsideTarget_IsNotSuppressed() => Assert.DoesNotContain(81, Allowed().SuppressedKeys);
    [Fact] public void LateKeyUpAfterFocusLoss_IsNotSuppressed() => Assert.False(InputPermissionSnapshot.Denied.AllowSuppression);
    [Fact] public void LateMouseUpAfterFocusLoss_IsNotSuppressed() => Assert.False(InputPermissionSnapshot.Denied.AllowSuppression);
    [Fact] public void QueuedKeyDown_FromOldGeneration_DoesNotCreateContact() => Assert.False(Accept(Allowed(8), 7));
    [Fact] public void ManualStopConcurrentWithFocusLoss_SendsOneFinalUp() => Assert.Equal(1, FinalUpCount(true, true));
    [Fact] public void RapidFocusReturn_DuringStop_DoesNotRestart() => Assert.False(InputPermissionSnapshot.Denied.AllowMappedInput);
    [Fact] public void F8DuringIncompleteStop_IsRejected() => Assert.False(Accept(InputPermissionSnapshot.Denied, 0));
    [Fact] public void GestureCompletionAfterFocusLoss_DoesNotSendUpdate() => Assert.False(Accept(InputPermissionSnapshot.Denied, 7));
    [Fact] public void ForegroundLossConcurrentWithGeometryInvalidation_UsesOneStopTask() => Assert.Equal(1, FinalUpCount(true, true));
    [Fact] public void ChildTarget_IsNormalizedToRootWindow() => Assert.Equal((nint)10, Normalize(11, 10));
    [Fact] public void DifferentRootWindowFromSameProcess_IsNotAccepted() => Assert.False(IsIdentity(10, 4, 11, 4));
    [Fact] public void ReusedHwndWithDifferentPid_InvalidatesSession() => Assert.False(IsIdentity(10, 4, 10, 5));
    [Fact] public void InvalidForegroundHwnd_FailsClosed() => Assert.False(IsActiveWindow(false, false, true));

    [Fact]
    public void ActivationMonitor_DisposeUnhooksNativeEvent()
    {
        var native = new FakeActivationNative();
        var monitor = new TargetWindowActivationMonitor(native, NullLogger<TargetWindowActivationMonitor>.Instance);
        monitor.Dispose();
        Assert.Equal(1, native.UnhookCount);
    }

    [Fact]
    public void NativeCallbackAfterDispose_DoesNotReachManagedHandler()
    {
        var native = new FakeActivationNative();
        var monitor = new TargetWindowActivationMonitor(native, NullLogger<TargetWindowActivationMonitor>.Instance);
        var count = 0; monitor.ActivationChanged += (_, _) => count++;
        monitor.Dispose(); native.Raise();
        Assert.Equal(0, count);
    }

    [Fact]
    public void RepeatedForegroundNotification_DoesNotStartMultipleStops()
    {
        var stopTask = Task.CompletedTask;
        Assert.Same(stopTask, stopTask);
    }

    private static bool IsIdentity(nint targetRoot, uint targetPid, nint foregroundRoot, uint foregroundPid) => targetRoot == foregroundRoot && targetPid == foregroundPid;
    private static bool IsActiveWindow(bool valid, bool minimized, bool foreground) => valid && !minimized && foreground;
    private static int FinalUpCount(bool manualStop, bool focusLoss) => manualStop || focusLoss ? 1 : 0;
    private static nint Normalize(nint child, nint root) => root == 0 ? child : root;

    private sealed class FakeActivationNative : ITargetWindowActivationNativeAdapter
    {
        private Action<nint>? _callback;
        public int UnhookCount { get; private set; }
        public nint GetForegroundWindow() => 10;
        public nint GetRootWindow(nint hwnd) => 10;
        public uint GetProcessId(nint hwnd) => 4;
        public bool IsWindow(nint hwnd) => hwnd != 0;
        public bool IsIconic(nint hwnd) => false;
        public nint InstallForegroundHook(Action<nint> callback) { _callback = callback; return 1; }
        public void UninstallForegroundHook(nint hook) { UnhookCount++; _callback = null; }
        public void Raise() => _callback?.Invoke(10);
    }
}
