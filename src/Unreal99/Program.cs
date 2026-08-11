using System.Text;
using Unreal99;
using Unreal99.Game;
using Unreal99.Platform;
using Unreal99.UI;

// Console output includes Traditional Chinese status lines.
try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { /* redirected stdout */ }

Console.WriteLine($"{Loc.GameTitle} — {Loc.GameSubtitle} · {Loc.GameVersionLabel}");

// Installer mode: write the icon and the Start Menu entry, then exit without opening a window.
if (args.Contains("--install-shortcut")) return Installer.InstallStartMenuShortcut(args) ? 0 : 1;
if (args.Contains("--uninstall-shortcut")) return Installer.UninstallStartMenuShortcut() ? 0 : 1;
if (args.Contains("--aimtest")) return BotAimPrediction.RunSelfTest();
if (args.Contains("--moderulestest")) return ObjectiveModeSelfTest.Run();
if (args.Contains("--vehiclecoverage")) return VehicleCoverageSelfTest.Run();
if (args.Contains("--weaponcoverage")) return WeaponCoverageSelfTest.Run();
if (args.Contains("--bindingtest"))
    return BindingProfile.RunSelfTest() | SettingsStore.RunPlayerThreeMigrationSelfTest();

using var app = new App();
try
{
    app.Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine("執行時發生錯誤：");
    Console.Error.WriteLine(ex);
    return 1;
}
return app.ExitCode;
