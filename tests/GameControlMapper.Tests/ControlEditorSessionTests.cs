using GameControlMapper.Models;
using GameControlMapper.ViewModels;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class ControlEditorSessionTests
{
    [Fact]
    public void Constructor_CreatesIndependentDraftAndPreservesIds()
    {
        var profile = MapperProfile.CreateDefault();
        var original = profile.Bindings[0];
        var session = new ControlEditorSession(profile);

        session.Bindings[0].X += 100;
        session.Bindings[0].Name = "Изменённый черновик";

        Assert.NotEqual(session.Bindings[0].X, original.X);
        Assert.NotEqual(session.Bindings[0].Name, original.Name);
        Assert.Equal(original.Id, session.Bindings[0].Model.Id);
        Assert.NotSame(original, session.Bindings[0].Model);
    }

    [Fact]
    public void Palette_ContainsOnlyRuntimeSupportedEditorTypes()
    {
        var kinds = ControlEditorSession.Palette.Select(item => item.Kind).ToArray();

        Assert.Equal(
            [
                BindingKind.Tap,
                BindingKind.Hold,
                BindingKind.DoubleTap,
                BindingKind.Swipe,
                BindingKind.Joystick,
                BindingKind.Aim,
                BindingKind.MouseArea
            ],
            kinds);
        Assert.DoesNotContain(BindingKind.Macro, kinds);
        Assert.DoesNotContain(BindingKind.Sequence, kinds);
    }

    [Fact]
    public void AddBinding_UsesSafeDefaultsAndKeepsCameraSingleton()
    {
        var profile = EmptyProfile();
        var session = new ControlEditorSession(profile);

        var joystick = session.AddBinding(BindingKind.Joystick, 200, 300);
        var sameJoystick = session.AddBinding(BindingKind.Joystick, 300, 400);
        var camera = session.AddBinding(BindingKind.Aim, 900, 500);
        var sameCamera = session.AddBinding(BindingKind.Aim, 1000, 600);
        var mouse = session.AddBinding(BindingKind.MouseArea, 1200, 600);

        Assert.Equal("WASD", joystick.Hotkey);
        Assert.Same(joystick, sameJoystick);
        Assert.Single(session.Bindings.Where(binding => binding.Kind == BindingKind.Joystick));
        Assert.Equal(profile.Camera.ActivationHotkey, camera.Hotkey);
        Assert.Same(camera, sameCamera);
        Assert.Single(session.Bindings.Where(binding => binding.Kind == BindingKind.Aim));
        Assert.Equal("MouseLeft", mouse.Hotkey);
        Assert.All(session.Bindings, binding => Assert.NotEqual(Guid.Empty, binding.Model.Id));
    }

    [Fact]
    public void MoveResizeAndExport_ClampCompleteGeometryToProfile()
    {
        var session = new ControlEditorSession(EmptyProfile());
        var tap = session.AddBinding(BindingKind.Tap, -100, -100);
        Assert.Equal(0, tap.X);
        Assert.Equal(0, tap.Y);

        session.Move(tap, 100_000, 100_000);
        session.Resize(tap, 100_000, 100_000);
        var swipe = session.AddBinding(BindingKind.Swipe, 1919, 1079);
        session.Resize(swipe, 100_000, 100_000);

        var exported = session.ExportBindings();
        Assert.All(exported, binding =>
        {
            Assert.True(binding.X >= 0);
            Assert.True(binding.Y >= 0);
            Assert.True(binding.X + binding.Width <= 1920);
            Assert.True(binding.Y + binding.Height <= 1080);
        });
        Assert.True(swipe.X + swipe.Width < 1920);
    }

    [Fact]
    public void DeleteAndDuplicate_ProtectCameraAndUseNewIdForCopies()
    {
        var session = new ControlEditorSession(EmptyProfile());
        var camera = session.AddBinding(BindingKind.Aim, 900, 500);
        Assert.Null(session.DuplicateSelected());
        Assert.False(session.DeleteSelected());
        Assert.Contains(camera, session.Bindings);

        var tap = session.AddBinding(BindingKind.Tap, 400, 400);
        var duplicate = session.DuplicateSelected();

        Assert.NotNull(duplicate);
        Assert.NotEqual(tap.Model.Id, duplicate!.Model.Id);
        Assert.True(session.DeleteSelected());
        Assert.Contains(tap, session.Bindings);
    }

    [Fact]
    public void Duplicate_DoesNotCreateAdditionalJoystickOrUnsupportedLegacyBinding()
    {
        var profile = EmptyProfile();
        profile.Bindings =
        [
            new ControlBinding { Kind = BindingKind.Joystick, Hotkey = "WASD", Name = "Движение" },
            new ControlBinding { Kind = BindingKind.Macro, Hotkey = "Q", Name = "Старый макрос" }
        ];
        var session = new ControlEditorSession(profile);

        session.SelectedBinding = session.Bindings[0];
        Assert.Null(session.DuplicateSelected());
        session.SelectedBinding = session.Bindings[1];
        Assert.Null(session.DuplicateSelected());
        Assert.Equal(2, session.Bindings.Count);
    }

    [Fact]
    public void CopyProfileWithBindings_IsDeepAndPreservesProfileSettings()
    {
        var profile = MapperProfile.CreateDefault();
        profile.Gamepad.Enabled = true;
        profile.Window.WindowHandle = 12345;

        var copy = ControlEditorSession.CopyProfileWithBindings(profile, profile.Bindings);
        copy.Bindings[0].Name = "Изменено";
        copy.Camera.AnchorX += 10;
        copy.Gamepad.Enabled = false;
        copy.Window.WindowHandle = 42;

        Assert.NotEqual(copy.Bindings[0].Name, profile.Bindings[0].Name);
        Assert.NotEqual(copy.Camera.AnchorX, profile.Camera.AnchorX);
        Assert.True(profile.Gamepad.Enabled);
        Assert.Equal(12345, profile.Window.WindowHandle);
    }

    [Theory]
    [InlineData(BindingKind.Tap, "WASD")]
    [InlineData(BindingKind.MouseArea, "WASD")]
    [InlineData(BindingKind.Joystick, "Q")]
    [InlineData(BindingKind.Tap, "F8")]
    [InlineData(BindingKind.Tap, "F9")]
    [InlineData(BindingKind.Tap, "F10")]
    [InlineData(BindingKind.Tap, "F11")]
    public void SemanticValidation_RejectsNonWorkingHotkeys(BindingKind kind, string hotkey)
    {
        var profile = EmptyProfile();
        profile.Bindings.Add(new ControlBinding { Kind = kind, Hotkey = hotkey, Name = "Проверка" });

        Assert.NotEmpty(MainViewModel.ValidateEditorSemantics(profile));
    }

    [Fact]
    public void TinyProfile_SwipeEndpointRemainsInsideHalfOpenBounds()
    {
        var profile = EmptyProfile();
        profile.ResolutionWidth = 1;
        profile.ResolutionHeight = 1;
        var session = new ControlEditorSession(profile);

        var swipe = session.AddBinding(BindingKind.Swipe, 1, 1);
        var exported = session.ExportBindings().Single();

        Assert.True(swipe.X + swipe.Width < 1);
        Assert.True(exported.X + exported.Width < 1);
    }

    [Theory]
    [InlineData("MouseLeft", "ЛКМ")]
    [InlineData("MouseRight", "ПКМ")]
    [InlineData("MouseMiddle", "СКМ")]
    [InlineData("MouseX1", "Бок. 1")]
    [InlineData("MouseX2", "Бок. 2")]
    [InlineData("LeftCtrl", "Ctrl")]
    public void BindingDisplayHotkey_UsesReadableShortLabel(string hotkey, string expected)
    {
        var binding = new BindingViewModel(new ControlBinding { Hotkey = hotkey });
        Assert.Equal(expected, binding.DisplayHotkey);
    }

    private static MapperProfile EmptyProfile() => new()
    {
        Name = "Тестовая схема",
        ResolutionWidth = 1920,
        ResolutionHeight = 1080,
        Camera = new CameraSettings { ActivationHotkey = "LeftCtrl" },
        Bindings = []
    };

}
