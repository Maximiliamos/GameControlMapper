using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class CameraMouseLookService : IDisposable
{
    private readonly TouchEngine _touch;
    private readonly ILogger<CameraMouseLookService> _logger;
    private readonly IMouseCursorController? _cursor;
    private readonly TimeProvider _time;
    private readonly TargetWindowSessionManager? _target;
    private readonly object _gate=new();
    private CameraSettings _settings=new();
    private bool _active,_disposed,_cursorSaved,_clipSaved,_hidden,_warpExpected;
    private PhysicalScreenPoint _savedPosition,_anchor;
    private CursorClip? _savedClip;
    private double _x,_y,_vx,_vy;
    private long _generation,_lastTimestamp;
    private long _nextGeneration;
    private TouchContactLease? _lease;
    public CameraMouseLookService(TouchEngine touch, ILogger<CameraMouseLookService> logger, IMouseCursorController? cursor=null, TimeProvider? time=null, TargetWindowSessionManager? target=null)
    { _touch=touch;_logger=logger;_cursor=cursor;_time=time??TimeProvider.System;_target=target; }
    public bool IsActive { get { lock(_gate)return _active; } }
    public long Generation { get { lock(_gate)return _generation; } }

    public void Start(CameraSettings settings,double anchorX,double anchorY)
    {
        lock(_gate)
        {
            if(_disposed||_active)return;
            var target=_target?.Current;
            if(_target is not null&&(target is null||!target.IsActive)){_logger.LogWarning("Camera start rejected: target session is inactive");return;}
            _settings=settings;_anchor=new((int)Math.Round(anchorX),(int)Math.Round(anchorY));_x=_anchor.X;_y=_anchor.Y;_vx=_vy=0;
            try
            {
                if(_cursor is not null)
                {
                    if(!_cursor.TryGetPosition(out _savedPosition))throw new InvalidOperationException("GetCursorPos failed"); _cursorSaved=true;
                    if(!_cursor.TryGetClip(out _savedClip))throw new InvalidOperationException("GetClipCursor failed"); _clipSaved=true;
                    if(target is not null&&!_cursor.TrySetClip(new(target.ClientRect.Left,target.ClientRect.Top,target.ClientRect.Left+target.ClientRect.Width,target.ClientRect.Top+target.ClientRect.Height)))throw new InvalidOperationException("ClipCursor failed");
                    if(!_cursor.TrySetVisible(false))throw new InvalidOperationException("ShowCursor failed"); _hidden=true;
                    if(!_cursor.TrySetPosition(_anchor))throw new InvalidOperationException("SetCursorPos failed"); _warpExpected=true;
                }
                _generation=target?.Generation??++_nextGeneration; _lastTimestamp=_time.GetTimestamp();
                _lease=_touch.StartTouch(_generation,"camera",_x,_y);if(_lease is null)throw new InvalidOperationException("No touch contact is available for camera");_active=true;
            }
            catch(Exception ex){RestoreCursor();_logger.LogError(ex,"Camera start failed closed");}
        }
    }

    public void OnMouseMove(int dx,int dy) { lock(_gate) ProcessMove(dx,dy,_generation); }
    public void OnMouseMove(int dx,int dy,long generation) { lock(_gate) ProcessMove(dx,dy,generation); }
    private void ProcessMove(int dx,int dy,long generation)
    {
        if(!_active||_disposed||generation!=_generation)return;
        try
        {
            if(_warpExpected&&_cursor is not null&&_cursor.TryGetPosition(out var p)&&p==_anchor){_warpExpected=false;return;}
            _warpExpected=false;
            var magnitude=Math.Sqrt((double)dx*dx+(double)dy*dy);if(!double.IsFinite(magnitude)||magnitude<=Math.Max(0,_settings.DeadZone))return;
            var sx=dx*_settings.SensitivityX*(_settings.InvertX?-1:1);var sy=dy*_settings.SensitivityY*(_settings.InvertY?-1:1);
            var factor=1+Math.Max(0,_settings.Acceleration)*magnitude;var tx=sx*factor;var ty=sy*factor;
            var now=_time.GetTimestamp();var dt=Math.Clamp(_time.GetElapsedTime(_lastTimestamp,now).TotalSeconds,1e-4,0.25);_lastTimestamp=now;
            var alpha=_settings.Smooth<=0?1:1-Math.Exp(-dt/Math.Max(1e-4,_settings.Smooth));_vx+=alpha*(tx-_vx);_vy+=alpha*(ty-_vy);
            var speed=Math.Sqrt(_vx*_vx+_vy*_vy);var max=Math.Max(0,_settings.MaxSpeed);if(speed>max&&speed>0){_vx*=max/speed;_vy*=max/speed;}
            _x+=_vx;_y+=_vy;var ox=_x-_anchor.X;var oy=_y-_anchor.Y;var radius=Math.Max(0,_settings.DragRadius);var distance=Math.Sqrt(ox*ox+oy*oy);if(distance>radius&&distance>0){_x=_anchor.X+ox*radius/distance;_y=_anchor.Y+oy*radius/distance;}
            if(double.IsFinite(_x)&&double.IsFinite(_y)&&_lease is not null)_touch.MoveTouch(_lease,_x,_y);
            if(_cursor is not null){if(!_cursor.TrySetPosition(_anchor))throw new InvalidOperationException("Camera recenter failed");_warpExpected=true;}
        }
        catch(Exception ex){_logger.LogError(ex,"Camera pipeline failed closed");StopCore();}
    }
    public void Stop(){lock(_gate)StopCore();}
    private void StopCore(){if(!_active&&!_cursorSaved&&!_clipSaved&&!_hidden)return;var lease=_lease;_lease=null;_active=false;_generation=0;_warpExpected=false;if(lease is not null)_touch.EndTouch(lease);RestoreCursor();}
    private void RestoreCursor(){if(_cursor is null)return;if(_clipSaved)_cursor.TrySetClip(_savedClip);if(_hidden)_cursor.TrySetVisible(true);if(_cursorSaved)_cursor.TrySetPosition(_savedPosition);_clipSaved=_hidden=_cursorSaved=false;}
    public void Dispose(){lock(_gate){if(_disposed)return;StopCore();_disposed=true;}}
}
