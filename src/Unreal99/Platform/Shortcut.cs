using System.Runtime.InteropServices;
using System.Reflection;

namespace Unreal99.Platform;

/// <summary>
/// Creates the Start Menu entry.
///
/// Windows shortcuts are a COM-only format. Use WScript.Shell through explicit IDispatch
/// reflection: the native IShellLink vtable declaration previously used here produced a file
/// containing the target bytes, but Windows' independent shortcut readers could not resolve it.
/// The automation object creates the same interoperable Unicode link as Explorer.
/// </summary>
public static class Shortcut
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    private const int ShcneCreate = 0x00000002;      // a non-folder item was created
    private const int ShcneUpdateDir = 0x00001000;   // a directory's contents changed
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfPathW = 0x0005;
    private const uint ShcnfFlush = 0x1000;

    /// <summary>
    /// Tells the shell a shortcut appeared. Without this the Start Menu keeps serving its cached
    /// app list and the new entry stays invisible until Explorer happens to restart.
    /// </summary>
    private static void NotifyShell(string linkPath, string folder)
    {
        try
        {
            nint link = Marshal.StringToHGlobalUni(linkPath);
            nint dir = Marshal.StringToHGlobalUni(folder);
            try
            {
                SHChangeNotify(ShcneCreate, ShcnfPathW | ShcnfFlush, link, 0);
                SHChangeNotify(ShcneUpdateDir, ShcnfPathW | ShcnfFlush, dir, 0);
                SHChangeNotify(ShcneAssocChanged, ShcnfFlush, 0, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(link);
                Marshal.FreeHGlobal(dir);
            }
        }
        catch (Exception) { /* purely a refresh hint */ }
    }

    /// <summary>Per-user Start Menu programs folder. No administrator rights required.</summary>
    public static string StartMenuFolder => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    public static string DefaultPath(string name) => Path.Combine(StartMenuFolder, name + ".lnk");

    /// <summary>Diagnostic detail from the most recent failed attempt.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>Creates or replaces a Start Menu shortcut. Returns the path, or null on failure.</summary>
    public static string Create(string name, string targetPath, string arguments, string iconPath,
        string description)
    {
        if (!OperatingSystem.IsWindows()) { LastError = "僅支援 Windows"; return null; }

        object shell = null;
        object shortcut = null;
        string step = "初始化";
        string temporaryPath = null;
        try
        {
            Directory.CreateDirectory(StartMenuFolder);
            string linkPath = DefaultPath(name);
            string workingDirectory = Path.GetDirectoryName(targetPath) ?? "";
            // WScript.Shell writes a standards-compliant shortcut but its Save method still
            // converts the filename through the active ANSI code page. Create under a unique
            // ASCII name, verify it there, then rename the already-complete file to our Unicode
            // Traditional-Chinese Start-menu name.
            temporaryPath = Path.Combine(StartMenuFolder,
                $"Unreal99-shortcut-{Guid.NewGuid():N}.lnk");

            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);

            step = "取得 WScript.Shell";
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) { LastError = "無法取得 WScript.Shell 類別"; return null; }
            shell = Activator.CreateInstance(shellType);
            if (shell == null) { LastError = "無法啟動 WScript.Shell"; return null; }
            step = "建立捷徑物件";
            shortcut = Invoke(shellType, shell, "CreateShortcut", temporaryPath);
            if (shortcut == null) { LastError = "無法建立捷徑物件"; return null; }

            Type shortcutType = shortcut.GetType();
            step = "設定目標";
            Set(shortcutType, shortcut, "TargetPath", targetPath);
            step = "設定工作目錄";
            Set(shortcutType, shortcut, "WorkingDirectory", workingDirectory);
            step = "設定參數";
            Set(shortcutType, shortcut, "Arguments", arguments ?? "");
            step = "設定說明";
            Set(shortcutType, shortcut, "Description", Truncate(description ?? "", 260));
            step = "設定視窗樣式";
            Set(shortcutType, shortcut, "WindowStyle", 1);   // SW_SHOWNORMAL
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                step = "設定圖示";
                Set(shortcutType, shortcut, "IconLocation", $"{iconPath},0");
            }
            step = "儲存捷徑";
            Invoke(shortcutType, shortcut, "Save");

            if (!File.Exists(temporaryPath)) { LastError = "捷徑檔案未建立"; return null; }
            if (!Verify(temporaryPath, targetPath)) { LastError = "捷徑目標驗證失敗"; return null; }
            File.Move(temporaryPath, linkPath, true);
            temporaryPath = null;

            NotifyShell(linkPath, StartMenuFolder);
            return linkPath;
        }
        catch (Exception ex)
        {
            Exception detail = ex is TargetInvocationException { InnerException: not null } tie
                ? tie.InnerException : ex;
            LastError = $"{step}: {detail.GetType().Name}: {detail.Message}";
            return null;
        }
        finally
        {
            Release(shortcut);
            Release(shell);
            if (temporaryPath != null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Reads a link back and confirms it resolves to the intended target.</summary>
    public static bool Verify(string linkPath, string expectedTarget)
    {
        if (!File.Exists(linkPath)) return false;
        object shell = null;
        object shortcut = null;
        string probePath = null;
        try
        {
            // See Create(): WScript cannot open a Unicode shortcut filename either. The shortcut
            // payload is path-independent, so verify an exact temporary copy under an ASCII name.
            probePath = Path.Combine(StartMenuFolder,
                $"Unreal99-verify-{Guid.NewGuid():N}.lnk");
            File.Copy(linkPath, probePath, true);
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return false;
            shortcut = Invoke(shellType, shell, "CreateShortcut", probePath);
            if (shortcut == null) return false;
            object target = Get(shortcut.GetType(), shortcut, "TargetPath");
            return target is string path
                && string.Equals(path, expectedTarget, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Release(shortcut);
            Release(shell);
            if (probePath != null && File.Exists(probePath)) File.Delete(probePath);
        }
    }

    private static object Invoke(Type type, object instance, string member, params object[] args)
        => type.InvokeMember(member, BindingFlags.InvokeMethod, null, instance, args);

    private static void Set(Type type, object instance, string member, object value)
        => type.InvokeMember(member, BindingFlags.SetProperty, null, instance, [value]);

    private static object Get(Type type, object instance, string member)
        => type.InvokeMember(member, BindingFlags.GetProperty, null, instance, null);

    private static void Release(object instance)
    {
        if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
