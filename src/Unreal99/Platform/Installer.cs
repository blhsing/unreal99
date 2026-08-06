using Unreal99.UI;

namespace Unreal99.Platform;

/// <summary>
/// Start Menu installation. Run the game with <c>--install-shortcut</c> to generate the icon
/// and place a shortcut under the current user's Start Menu; <c>--uninstall-shortcut</c> removes it.
/// Everything is per-user, so no administrator rights are needed.
/// </summary>
public static class Installer
{
    public static bool InstallStartMenuShortcut(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("捷徑安裝僅支援 Windows。");
            return false;
        }

        try
        {
            string exePath = ResolveLaunchTarget(out string arguments);
            if (exePath == null)
            {
                Console.Error.WriteLine("找不到可執行檔，無法建立捷徑。");
                return false;
            }

            string iconDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            string iconPath = Path.Combine(iconDirectory, "Unreal99.ico");
            try
            {
                AppIcon.WriteIco(iconPath);
                Console.WriteLine($"已產生圖示: {iconPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"圖示產生失敗（將使用預設圖示）: {ex.Message}");
                iconPath = null;
            }

            // Extra flags after --install-shortcut are baked into the shortcut, so the Start Menu
            // entry can carry launch options such as a preferred quality level.
            string extra = string.Join(' ', args
                .SkipWhile(a => a != "--install-shortcut")
                .Skip(1)
                .Where(a => a != "--uninstall-shortcut"));
            if (extra.Length > 0) arguments = (arguments + " " + extra).Trim();

            string link = Shortcut.Create(Loc.GameTitle, exePath, arguments, iconPath,
                $"{Loc.GameTitle} — {Loc.GameSubtitle}");
            if (link == null)
            {
                Console.Error.WriteLine($"建立捷徑失敗。{Shortcut.LastError}");
                return false;
            }

            Console.WriteLine($"開始選單捷徑已建立: {link}");
            Console.WriteLine($"  目標: {exePath}{(arguments.Length > 0 ? " " + arguments : "")}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"建立捷徑時發生錯誤: {ex.Message}");
            return false;
        }
    }

    public static bool UninstallStartMenuShortcut()
    {
        try
        {
            string path = Shortcut.DefaultPath(Loc.GameTitle);
            if (File.Exists(path))
            {
                File.Delete(path);
                Console.WriteLine($"已移除捷徑: {path}");
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"移除捷徑失敗: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds what the shortcut should launch. A published apphost is preferred; when running
    /// from a plain `dotnet run` build the shortcut targets the dotnet host with the dll instead,
    /// so the entry works either way.
    /// </summary>
    private static string ResolveLaunchTarget(out string arguments)
    {
        arguments = "";
        string baseDirectory = AppContext.BaseDirectory;

        string apphost = Path.Combine(baseDirectory, "Unreal99.exe");
        if (File.Exists(apphost)) return apphost;

        string dll = Path.Combine(baseDirectory, "Unreal99.dll");
        if (File.Exists(dll))
        {
            string dotnet = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(dotnet) && File.Exists(dotnet))
            {
                arguments = $"\"{dll}\"";
                return dotnet;
            }
        }
        return null;
    }
}
