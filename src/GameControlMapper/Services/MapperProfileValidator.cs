using GameControlMapper.Models;

namespace GameControlMapper.Services;

public sealed record ProfileValidationIssue(string Code,string FieldPath,string Message);
public sealed record ProfileValidationResult(IReadOnlyList<ProfileValidationIssue> Errors,IReadOnlyList<ProfileValidationIssue> Warnings)
{ public bool IsValid=>Errors.Count==0; }
public interface IMapperProfileValidator { ProfileValidationResult Validate(MapperProfile? profile); }
public sealed class ProfileValidationException : InvalidOperationException
{ public ProfileValidationException(ProfileValidationResult result):base(string.Join("; ",result.Errors.Select(e=>$"{e.FieldPath}: {e.Message}"))){Result=result;} public ProfileValidationResult Result{get;} }

public sealed class MapperProfileValidator : IMapperProfileValidator
{
    public const int MaxResolution=16384,MaxDurationMilliseconds=600000;
    private readonly HotkeyParser _hotkeys;
    public MapperProfileValidator(HotkeyParser hotkeys)=>_hotkeys=hotkeys;
    public ProfileValidationResult Validate(MapperProfile? p)
    {
        var e=new List<ProfileValidationIssue>();var w=new List<ProfileValidationIssue>();void Add(string c,string path,string m)=>e.Add(new(c,path,m));
        if(p is null){Add("profile.null","$","Profile is null.");return new(e,[]);}
        if(!ProfileNamePolicy.TryValidate(p.Name,out var nameCode,out var nameMessage))Add(nameCode,"name",nameMessage);
        if(p.ResolutionWidth<=0)Add("profile.resolution.width","resolutionWidth","Width must be positive.");
        if(p.ResolutionHeight<=0)Add("profile.resolution.height","resolutionHeight","Height must be positive.");
        if(p.ResolutionWidth>MaxResolution||p.ResolutionHeight>MaxResolution)Add("profile.resolution.excessive","resolution","Resolution exceeds the supported limit.");
        ValidateLifecycleHotkeys(p,e);
        ValidateCamera(p,e);
        ValidateWindow(p,e);
        if(p.Gamepad is null)Add("profile.gamepad.null","gamepad","Gamepad settings must not be null.");
        else if(p.Gamepad.Enabled)w.Add(new("UnsupportedInBeta","gamepad.enabled","XInput отключён в beta-версии."));
        if(p.Bindings is null){Add("profile.bindings.null","bindings","Bindings must not be null.");return new(e,w);}
        var lifecycle=new HashSet<string>(new[]{p.EnableHotkey,p.DisableHotkey,p.ToggleOverlayHotkey,p.EditorHotkey}.Where(ValidHotkey).Select(CanonicalHotkey),StringComparer.Ordinal);
        var ids=new HashSet<Guid>();
        for(var i=0;i<p.Bindings.Count;i++)
        {
            var b=p.Bindings[i];var path=$"bindings[{i}]";
            if(b.Id==Guid.Empty)Add("binding.id.empty",$"{path}.id","Binding ID must not be empty.");else if(!ids.Add(b.Id))Add("binding.id.duplicate",$"{path}.id","Binding ID must be unique.");
            if(string.IsNullOrWhiteSpace(b.Name))Add("binding.name.required",$"{path}.name","Binding name is required.");
            if(!Enum.IsDefined(b.Kind))Add("binding.kind.unsupported",$"{path}.kind","Binding kind is unsupported.");
            if(b.Kind is BindingKind.Macro or BindingKind.Sequence)w.Add(new("UnsupportedInBeta",$"{path}.kind",$"{b.Kind} отключён в beta-версии."));
            if(!Finite(b.X)||!Finite(b.Y)||!Finite(b.Width)||!Finite(b.Height)){Add("binding.geometry.nonfinite",$"{path}.geometry","Geometry must be finite.");continue;}
            if(b.Width<=0||b.Height<=0)Add("binding.area.nonpositive",$"{path}.width","Binding area must be positive.");
            if(b.CenterX<0||b.CenterY<0||b.CenterX>=p.ResolutionWidth||b.CenterY>=p.ResolutionHeight)Add("binding.point.outside",$"{path}.center","Binding center is outside half-open profile bounds.");
            if(!ValidHotkey(b.Hotkey))Add("binding.hotkey.invalid",$"{path}.hotkey","Hotkey cannot be parsed.");
            else if(lifecycle.Contains(CanonicalHotkey(b.Hotkey)))Add("binding.hotkey.lifecycle-conflict",$"{path}.hotkey","Binding hotkey conflicts with a lifecycle hotkey.");
            if(b.HoldMilliseconds<0||b.DelayMilliseconds<0)Add("binding.duration.negative",$"{path}.duration","Duration and delay must be non-negative.");
            if(b.HoldMilliseconds>MaxDurationMilliseconds||b.DelayMilliseconds>MaxDurationMilliseconds)Add("binding.duration.excessive",$"{path}.duration","Duration or delay exceeds the supported limit.");
            if(!Finite(b.Opacity)||b.Opacity<0||b.Opacity>1)Add("binding.opacity.invalid",$"{path}.opacity","Opacity must be between 0 and 1.");
            if(b.Actions is null)Add("binding.actions.null",$"{path}.actions","Actions must not be null.");else for(var j=0;j<b.Actions.Count;j++){var a=b.Actions[j];if(!Enum.IsDefined(a.Kind))Add("action.kind.unsupported",$"{path}.actions[{j}].kind","Action kind is unsupported.");if(a.DelayMilliseconds<0||a.DelayMilliseconds>MaxDurationMilliseconds)Add("action.delay.invalid",$"{path}.actions[{j}].delayMilliseconds","Action delay is invalid.");if(!Finite(a.X)||!Finite(a.Y))Add("action.coordinates.nonfinite",$"{path}.actions[{j}]","Action coordinates must be finite.");}
        }
        if(p.InputMode is InputMode.RawInput or InputMode.Interception or InputMode.ViGEm)w.Add(new("UnsupportedInBeta","inputMode",$"Режим {p.InputMode} отключён в beta-версии."));
        return new(e,w);
    }
    private void ValidateLifecycleHotkeys(MapperProfile p,List<ProfileValidationIssue> errors)
    {
        var values=new[]{("enableHotkey",p.EnableHotkey),("disableHotkey",p.DisableHotkey),("toggleOverlayHotkey",p.ToggleOverlayHotkey),("editorHotkey",p.EditorHotkey)};
        foreach(var (path,value) in values)if(!ValidHotkey(value))errors.Add(new("profile.lifecycle-hotkey.invalid",path,"Lifecycle hotkey cannot be empty and must be parseable."));
        var valid=values.Where(x=>ValidHotkey(x.Item2)).GroupBy(x=>CanonicalHotkey(x.Item2),StringComparer.Ordinal);
        foreach(var duplicate in valid.Where(group=>group.Count()>1))foreach(var item in duplicate)errors.Add(new("profile.lifecycle-hotkey.duplicate",item.Item1,"Lifecycle hotkeys must be distinct."));
    }
    private void ValidateCamera(MapperProfile p,List<ProfileValidationIssue> errors)
    {
        void Add(string code,string path,string message)=>errors.Add(new(code,path,message));
        var c=p.Camera;if(c is null){Add("profile.camera.null","camera","Camera settings must not be null.");return;}
        if(!Finite(c.AnchorX)||!Finite(c.AnchorY))Add("camera.anchor.nonfinite","camera.anchor","Camera anchor must be finite.");
        else if(c.AnchorX<0||c.AnchorY<0||c.AnchorX>=p.ResolutionWidth||c.AnchorY>=p.ResolutionHeight)Add("camera.anchor.outside","camera.anchor","Camera anchor is outside half-open profile bounds.");
        if(!Finite(c.DragRadius)||c.DragRadius<8||c.DragRadius>MapperProfileValidator.MaxResolution)Add("camera.drag-radius.invalid","camera.dragRadius","Camera drag radius must be between 8 and 16384.");
        if(!Finite(c.SensitivityX)||c.SensitivityX<=0||c.SensitivityX>100)Add("camera.sensitivity-x.invalid","camera.sensitivityX","Horizontal sensitivity must be in (0, 100].");
        if(!Finite(c.SensitivityY)||c.SensitivityY<=0||c.SensitivityY>100)Add("camera.sensitivity-y.invalid","camera.sensitivityY","Vertical sensitivity must be in (0, 100].");
        if(!Finite(c.Acceleration)||c.Acceleration<0||c.Acceleration>10)Add("camera.acceleration.invalid","camera.acceleration","Acceleration must be between 0 and 10.");
        if(!Finite(c.DeadZone)||c.DeadZone<0||c.DeadZone>100)Add("camera.dead-zone.invalid","camera.deadZone","Dead zone must be between 0 and 100.");
        if(!Finite(c.Smooth)||c.Smooth<0||c.Smooth>1)Add("camera.smooth.invalid","camera.smooth","Smoothing must be between 0 and 1.");
        if(!Finite(c.MaxSpeed)||c.MaxSpeed<=0||c.MaxSpeed>4096)Add("camera.max-speed.invalid","camera.maxSpeed","Maximum speed must be in (0, 4096].");
        if(!ValidHotkey(c.ActivationHotkey))Add("camera.hotkey.invalid","camera.activationHotkey","Camera hotkey must be parseable.");
    }
    private static void ValidateWindow(MapperProfile p,List<ProfileValidationIssue> errors)
    {
        void Add(string code,string path,string message)=>errors.Add(new(code,path,message));
        var window=p.Window;if(window is null){Add("profile.window.null","window","Window settings must not be null.");return;}
        if(window.WindowHandle<0)Add("window.handle.negative","window.windowHandle","Window handle must not be negative.");
        if(window.Width<0||window.Height<0)Add("window.size.negative","window.size","Window dimensions must not be negative.");
        if(!Finite(window.ScaleX)||window.ScaleX<=0||!Finite(window.ScaleY)||window.ScaleY<=0)Add("window.scale.invalid","window.scale","Window scale must be finite and positive.");
        if(window.ProcessName is null)Add("window.process-name.null","window.processName","Process name must not be null.");
        if(window.WindowTitle is null)Add("window.title.null","window.windowTitle","Window title must not be null.");
    }
    private bool ValidHotkey(string? value)=>!string.IsNullOrWhiteSpace(value)&&(value.Equals("WASD",StringComparison.OrdinalIgnoreCase)||value.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries) is {Length:>0} parts&&parts.All(p=>_hotkeys.ToVirtualKey(p)!=0));
    private string CanonicalHotkey(string value)=>value.Equals("WASD",StringComparison.OrdinalIgnoreCase)?"WASD":string.Join('+',value.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(_hotkeys.ToVirtualKey));
    private static bool Finite(double value)=>double.IsFinite(value);
}
