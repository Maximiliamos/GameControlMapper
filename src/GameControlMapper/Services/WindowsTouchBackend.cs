using System;
using System.Runtime.InteropServices;
using GameControlMapper.Models;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace GameControlMapper.Services;

public class WindowsTouchBackend : ITouchBackend
{
    private readonly ILogger<WindowsTouchBackend> _logger;
    private bool _initialized;
    private bool _disposed;
    private readonly MappingSessionDiagnostics? _sessions;private readonly NativeErrorRateLimiter _errors=new();

    public WindowsTouchBackend(ILogger<WindowsTouchBackend> logger,MappingSessionDiagnostics? sessions=null)
    {
        _logger = logger;
        _sessions=sessions;
    }

    public TouchCapabilities Capabilities => new TouchCapabilities(10, true, false, true);

    public bool Initialize()
    {
        if (_initialized)
        {
            _logger.LogDebug("WindowsTouchBackend already initialized");
            return true;
        }

        _logger.LogDebug("Initializing WindowsTouchBackend...");
        bool ok = NativeMethods.InitializeTouchInjection(10, NativeMethods.TOUCH_FEEDBACK_NONE);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            LogNative("InitializeTouchInjection",err,LogLevel.Error);
            return false;
        }

        _initialized = true;
        return true;
    }

    public bool SendFrame(TouchFrame frame)
    {
        if (_disposed || !_initialized)
        {
            _logger.LogError("WindowsTouchBackend not initialized or already disposed!");
            return false;
        }

        var contacts = frame.GetContacts();
        if (contacts.Length == 0)
            return true;

        var pointerInfos = new NativeMethods.POINTER_TOUCH_INFO[contacts.Length];

        for (int i = 0; i < contacts.Length; i++)
        {
            pointerInfos[i] = Convert(contacts[i]);
        }

        bool ok = NativeMethods.InjectTouchInput(
            (uint)pointerInfos.Length,
            pointerInfos);

        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            LogNative("InjectTouchInput",err,LogLevel.Warning);
        }

        return ok;
    }
    private void LogNative(string operation,int code,LogLevel level){var key=$"{operation}:{code}";if(!_errors.ShouldLog(key,DateTimeOffset.Now,out var suppressed))return;var message=new Win32Exception(code).Message;_logger.Log(level,"{Operation} failed. Win32 code={Code}; message={Message}; mapping session={SessionId}; previously suppressed={Suppressed}",operation,code,message,_sessions?.Last.SessionId??"none",suppressed);}

    private NativeMethods.POINTER_TOUCH_INFO Convert(TouchContact contact)
    {
        var flags = GetFlags(contact.State);
        int pressure = Math.Clamp((int)contact.Pressure, 0, 1024);

        var info = new NativeMethods.POINTER_TOUCH_INFO
        {
            pointerInfo = new NativeMethods.POINTER_INFO
            {
                pointerType = NativeMethods.PT_TOUCH,
                pointerId = (uint)contact.ContactId,
                ptPixelLocation = new NativeMethods.POINT
                {
                    X = (int)Math.Round(contact.X),
                    Y = (int)Math.Round(contact.Y)
                },
                pointerFlags = flags
            },
            touchFlags = 0, // NativeMethods doesn't have TOUCH_FLAGS, use 0 for none
            touchMask = NativeMethods.TOUCH_MASK_CONTACTAREA |
                        NativeMethods.TOUCH_MASK_PRESSURE,
            orientation = 0,
            pressure = (uint)pressure,
            rcContact = new NativeMethods.RECT
            {
                Left = (int)Math.Round(contact.X) - 10,
                Top = (int)Math.Round(contact.Y) - 10,
                Right = (int)Math.Round(contact.X) + 10,
                Bottom = (int)Math.Round(contact.Y) + 10
            }
        };

        return info;
    }

    private uint GetFlags(TouchState state)
    {
        return state switch
        {
            TouchState.Down =>
                NativeMethods.POINTER_FLAG_DOWN |
                NativeMethods.POINTER_FLAG_INRANGE |
                NativeMethods.POINTER_FLAG_INCONTACT,
            TouchState.Update =>
                NativeMethods.POINTER_FLAG_UPDATE |
                NativeMethods.POINTER_FLAG_INRANGE |
                NativeMethods.POINTER_FLAG_INCONTACT,
            TouchState.Up =>
                NativeMethods.POINTER_FLAG_UP,
            _ =>
                NativeMethods.POINTER_FLAG_NONE
        };
    }

    public void Shutdown()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}
