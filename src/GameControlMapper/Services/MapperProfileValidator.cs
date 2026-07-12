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
        if(string.IsNullOrWhiteSpace(p.Name))Add("profile.name.required","name","Profile name is required.");
        if(p.ResolutionWidth<=0)Add("profile.resolution.width","resolutionWidth","Width must be positive.");
        if(p.ResolutionHeight<=0)Add("profile.resolution.height","resolutionHeight","Height must be positive.");
        if(p.ResolutionWidth>MaxResolution||p.ResolutionHeight>MaxResolution)Add("profile.resolution.excessive","resolution","Resolution exceeds the supported limit.");
        if(p.Bindings is null){Add("profile.bindings.null","bindings","Bindings must not be null.");return new(e,[]);}
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
            if(b.HoldMilliseconds<0||b.DelayMilliseconds<0)Add("binding.duration.negative",$"{path}.duration","Duration and delay must be non-negative.");
            if(b.HoldMilliseconds>MaxDurationMilliseconds||b.DelayMilliseconds>MaxDurationMilliseconds)Add("binding.duration.excessive",$"{path}.duration","Duration or delay exceeds the supported limit.");
            if(!Finite(b.Opacity)||b.Opacity<0||b.Opacity>1)Add("binding.opacity.invalid",$"{path}.opacity","Opacity must be between 0 and 1.");
            if(b.Actions is null)Add("binding.actions.null",$"{path}.actions","Actions must not be null.");else for(var j=0;j<b.Actions.Count;j++){var a=b.Actions[j];if(!Enum.IsDefined(a.Kind))Add("action.kind.unsupported",$"{path}.actions[{j}].kind","Action kind is unsupported.");if(a.DelayMilliseconds<0||a.DelayMilliseconds>MaxDurationMilliseconds)Add("action.delay.invalid",$"{path}.actions[{j}].delayMilliseconds","Action delay is invalid.");if(!Finite(a.X)||!Finite(a.Y))Add("action.coordinates.nonfinite",$"{path}.actions[{j}]","Action coordinates must be finite.");}
        }
        if(p.Gamepad?.Enabled==true)w.Add(new("UnsupportedInBeta","gamepad.enabled","XInput отключён в beta-версии."));
        if(p.InputMode is InputMode.RawInput or InputMode.Interception or InputMode.ViGEm)w.Add(new("UnsupportedInBeta","inputMode",$"Режим {p.InputMode} отключён в beta-версии."));
        return new(e,w);
    }
    private bool ValidHotkey(string value)=>value.Equals("WASD",StringComparison.OrdinalIgnoreCase)||value.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries) is {Length:>0} parts&&parts.All(p=>_hotkeys.ToVirtualKey(p)!=0);
    private static bool Finite(double value)=>double.IsFinite(value);
}
