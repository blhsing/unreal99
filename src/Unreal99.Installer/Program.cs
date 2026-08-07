using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.Text;

namespace Unreal99.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--render-preview")
        {
            ApplicationConfiguration.Initialize();
            using var form = new InstallerForm();
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, form.ClientRectangle);
            bitmap.Save(Path.GetFullPath(args[1]), ImageFormat.Png);
            form.Close();
            return 0;
        }

        if (args.Length == 0)
        {
            NativeConsole.Detach();
            ApplicationConfiguration.Initialize();
            Application.Run(new InstallerForm());
            return 0;
        }

        NativeConsole.AttachToParent();
        try
        {
            return RunCliAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"安裝失敗：{ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCliAsync(string[] args)
    {
        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            PrintHelp();
            return 0;
        }

        bool uninstall = args.Any(a => a.Equals("uninstall", StringComparison.OrdinalIgnoreCase)
            || a == "--uninstall");
        string installDirectory = ValueAfter(args, "--install-dir") ?? InstallService.DefaultInstallDirectory;
        string source = ValueAfter(args, "--source");
        bool startMenu = !args.Contains("--no-start-menu");
        var progress = new ConsoleProgress();

        if (uninstall)
        {
            await InstallService.UninstallAsync(installDirectory, progress);
            Console.WriteLine("移除完成。");
        }
        else
        {
            source = InstallService.FindPayload(source);
            await InstallService.InstallAsync(new InstallOptions(installDirectory, source, startMenu), progress);
            Console.WriteLine($"安裝完成：{Path.GetFullPath(installDirectory)}");
        }
        return 0;
    }

    private static string ValueAfter(string[] args, string option)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private sealed class ConsoleProgress : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value)
            => Console.WriteLine($"[{value.Percent,3}%] {value.Message}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            虛幻競技場 99 安裝程式

            圖形介面：
              Unreal99Installer.exe

            命令列：
              Unreal99Installer.exe install [--install-dir <路徑>] [--no-start-menu] [--source <payload>]
              Unreal99Installer.exe uninstall [--install-dir <路徑>]
              Unreal99Installer.exe --help

            預設安裝於目前使用者的 LocalAppData\Programs\Unreal99，不需要系統管理員權限。
            """);
    }
}

internal static class NativeConsole
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static void AttachToParent()
    {
        if (!OperatingSystem.IsWindows()) return;
        AttachConsole(AttachParentProcess);
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException) { }
    }

    public static void Detach()
    {
        if (OperatingSystem.IsWindows()) FreeConsole();
    }
}
