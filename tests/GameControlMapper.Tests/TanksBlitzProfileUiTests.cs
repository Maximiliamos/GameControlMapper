using GameControlMapper.Models;
using Xunit;
namespace GameControlMapper.Tests;
public sealed class TanksBlitzProfileUiTests
{
 [Fact]public void DefaultProfile_MatchesImportedTanksBlitzLayout(){var p=MapperProfile.CreateDefault();Assert.Equal(1920,p.ResolutionWidth);Assert.Equal(1080,p.ResolutionHeight);Assert.Equal(12,p.Bindings.Count);Assert.Contains(p.Bindings,x=>x.Name=="Движение"&&x.Hotkey=="WASD"&&x.Kind==BindingKind.Joystick);Assert.Contains(p.Bindings,x=>x.Name=="Камера"&&x.Hotkey=="LeftCtrl"&&x.Kind==BindingKind.Aim);Assert.Contains(p.Bindings,x=>x.Name=="Огонь"&&x.Hotkey=="MouseLeft");Assert.Contains(p.Bindings,x=>x.Name=="Прицел"&&x.Hotkey=="MouseRight");Assert.All(p.Bindings,x=>Assert.True(x.CenterX>=0&&x.CenterX<1920&&x.CenterY>=0&&x.CenterY<1080));}
 [Fact]public void MainMenu_ContainsSetupHelpAndReadableRussianLabels(){var xaml=Source("src/GameControlMapper/UI/Views/MainWindow.xaml");Assert.Contains("Как настроить управление",xaml);Assert.Contains("Сохранить",xaml);Assert.Contains("Запустить",xaml);Assert.Contains("Остановить",xaml);Assert.Contains("F9 всегда останавливает управление",xaml);}
 private static string Source(string relative){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"GameControlMapper.sln")))d=d.Parent;return File.ReadAllText(Path.Combine(d!.FullName,relative.Replace('/',Path.DirectorySeparatorChar)));}
}
