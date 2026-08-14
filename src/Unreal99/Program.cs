using System.Text;
using Unreal99;
using Unreal99.Game;
using Unreal99.Platform;
using Unreal99.UI;
using Unreal99.World;

// Console output includes Traditional Chinese status lines.
try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { /* redirected stdout */ }

Console.WriteLine($"{Loc.GameTitle} — {Loc.GameSubtitle} · {Loc.GameVersionLabel}");

// Installer mode: write the icon and the Start Menu entry, then exit without opening a window.
if (args.Contains("--install-shortcut")) return Installer.InstallStartMenuShortcut(args) ? 0 : 1;
if (args.Contains("--uninstall-shortcut")) return Installer.UninstallStartMenuShortcut() ? 0 : 1;
if (args.Contains("--aimtest"))
    return BotAimPrediction.RunSelfTest() | BotController.RunDifficultySelfTest();
if (args.Contains("--moderulestest")) return ObjectiveModeSelfTest.Run();
if (args.Contains("--vehiclecoverage")) return VehicleCoverageSelfTest.Run();
if (args.Contains("--vehiclecontrol")) return VehicleControlSelfTest.Run();
if (args.Contains("--weaponcoverage")) return WeaponCoverageSelfTest.Run();
if (args.Contains("--weaponmechanicstest")) return WeaponMechanicsSelfTest.Run();
if (args.Contains("--collisiontest")) return CollisionWorld.RunStepTraversalSelfTest();
if (args.Contains("--mapstats")) return MapStats.Run(args);
if (args.Contains("--hotplugtest"))
    return DeviceAssignment.RunSelfTest() | InputSystem.RunPointerResetSelfTest()
        | InputSystem.RunLookRoutingSelfTest();
if (args.Contains("--bindingtest"))
    return BindingProfile.RunSelfTest() | SettingsStore.RunPlayerThreeMigrationSelfTest()
        | SettingsStore.RunVehicleUseMigrationSelfTest() | RawInput.RunKeyNormalizationSelfTest()
        | SettingsStore.RunHoverboardMigrationSelfTest() | SettingsStore.RunTenSlotMigrationSelfTest()
        | Weapons.RunHudGroupSelfTest();
if (args.Contains("--gallerytest")) return Menu.RunGalleryPointerSelfTest();

// Only normal game sessions participate in the mutex; command-line diagnostics and installer
// helpers above remain usable while the game is open.
using var singleInstance = new Mutex(initiallyOwned: true, "Local\\Unreal99.Game", out bool firstInstance);
if (!firstInstance)
{
    Console.WriteLine("遊戲已在執行；不會開啟第二個執行個體。");
    return 0;
}

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
