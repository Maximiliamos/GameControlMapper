using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace GameControlMapper.Services;

public static partial class ProfileNamePolicy
{
    public static bool TryValidate(string? name,out string code,out string message)
    {
        code=string.Empty;message=string.Empty;
        if(string.IsNullOrWhiteSpace(name)){code="profile.name.required";message="Profile name is required.";return false;}
        if(name!=name.Trim()||name.EndsWith('.')||name.EndsWith(' ')){code="profile.name.trailing";message="Profile name must not start or end with spaces or dots.";return false;}
        if(name is "." or ".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.Contains('/')||name.Contains('\\')){code="profile.name.invalid";message="Profile name is not a safe Windows file name.";return false;}
        if(!name.IsNormalized(NormalizationForm.FormC)){code="profile.name.normalization";message="Profile name must use normalized Unicode characters.";return false;}
        var stem=name.Split('.')[0];
        if(ReservedWindowsName().IsMatch(stem)){code="profile.name.reserved";message="Profile name is reserved by Windows.";return false;}
        return true;
    }

    public static string NormalizeForComparison(string name)=>name.Normalize(NormalizationForm.FormC).TrimEnd(' ','.').ToUpperInvariant();

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex ReservedWindowsName();
}
