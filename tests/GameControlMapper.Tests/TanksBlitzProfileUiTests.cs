using GameControlMapper.Models;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class TanksBlitzProfileUiTests
{
    [Fact]
    public void DefaultProfile_MatchesDocumentedTanksBlitzLayout()
    {
        var profile=MapperProfile.CreateDefault();
        Assert.Equal("Tanks Blitz",profile.Game);Assert.Equal("tanksblitz",profile.Window.ProcessName);Assert.Equal("Tanks Blitz",profile.Window.WindowTitle);
        Assert.Equal(1920,profile.ResolutionWidth);Assert.Equal(1080,profile.ResolutionHeight);Assert.Equal(12,profile.Bindings.Count);
        Assert.Contains(profile.Bindings,binding=>binding.Hotkey=="WASD"&&binding.Kind==BindingKind.Joystick);
        Assert.Contains(profile.Bindings,binding=>binding.Hotkey=="LeftCtrl"&&binding.Kind==BindingKind.Aim);
        Assert.Contains(profile.Bindings,binding=>binding.Hotkey=="MouseLeft"&&binding.Kind==BindingKind.MouseArea);
        Assert.Contains(profile.Bindings,binding=>binding.Hotkey=="MouseRight"&&binding.Kind==BindingKind.MouseArea);
        Assert.All(profile.Bindings,binding=>Assert.True(binding.CenterX>=0&&binding.CenterX<1920&&binding.CenterY>=0&&binding.CenterY<1080));
    }

    [Fact]
    public void DefaultCamera_IsDirectAndResponsive()
    {
        var camera=MapperProfile.CreateDefault().Camera;
        Assert.Equal(0.5,camera.SensitivityX);Assert.Equal(0.5,camera.SensitivityY);Assert.Equal(0,camera.DeadZone);Assert.Equal(0,camera.Smooth);Assert.Equal(64,camera.MaxSpeed);Assert.Equal(220,camera.DragRadius);
    }

    [Fact]
    public void DefaultProfile_UsesObservedNativeBattleHudCenters()
    {
        var profile=MapperProfile.CreateDefault();
        AssertCenter(profile,"WASD",137,959);AssertCenter(profile,"MouseLeft",1740,938);AssertCenter(profile,"MouseRight",1587,1034);
        AssertCenter(profile,"1",1655,1015);AssertCenter(profile,"2",1717,1015);AssertCenter(profile,"3",1779,1015);
        AssertCenter(profile,"Q",1886,770);AssertCenter(profile,"E",1886,836);AssertCenter(profile,"R",1886,901);
        AssertCenter(profile,"Escape",1717,32);AssertCenter(profile,"Space",1306,1029);
    }

    private static void AssertCenter(MapperProfile profile,string hotkey,double x,double y)
    {
        var binding=Assert.Single(profile.Bindings,item=>item.Hotkey==hotkey);Assert.Equal(x,binding.CenterX);Assert.Equal(y,binding.CenterY);
    }
}
