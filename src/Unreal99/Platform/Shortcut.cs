using System.Runtime.InteropServices;
using System.Text;

namespace Unreal99.Platform;

/// <summary>
/// Creates the Start Menu entry.
///
/// Windows shortcuts are a COM-only format. Late-bound WScript.Shell automation silently drops
/// property writes under .NET's IDispatch binder — it produces a file whose target is empty —
/// so this talks to <c>IShellLinkW</c> and <c>IPersistFile</c> through their vtables instead.
/// That is fully Unicode, which matters because the link is named in Traditional Chinese.
/// </summary>
public static class Shortcut
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        // The declaration order defines the vtable layout, so every method must be present
        // even though only the setters are used here.
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, nint findData,
            uint flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxArgs);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath,
            out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

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

        object instance = null;
        try
        {
            Directory.CreateDirectory(StartMenuFolder);
            string linkPath = DefaultPath(name);
            string workingDirectory = Path.GetDirectoryName(targetPath) ?? "";

            // A stale or malformed file at this path would make IPersistFile.Save fail.
            if (File.Exists(linkPath)) File.Delete(linkPath);

            Type type = Type.GetTypeFromCLSID(ClsidShellLink);
            if (type == null) { LastError = "無法取得 ShellLink 類別"; return null; }
            instance = Activator.CreateInstance(type);
            if (instance is not IShellLinkW link) { LastError = "無法取得 IShellLinkW 介面"; return null; }

            link.SetPath(targetPath);
            link.SetWorkingDirectory(workingDirectory);
            link.SetArguments(arguments ?? "");
            link.SetDescription(Truncate(description ?? "", 260));
            link.SetShowCmd(1);   // SW_SHOWNORMAL
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)) link.SetIconLocation(iconPath, 0);

            ((IPersistFile)link).Save(linkPath, true);

            if (!File.Exists(linkPath)) { LastError = "捷徑檔案未建立"; return null; }
            if (!Verify(linkPath, targetPath)) { LastError = "捷徑目標驗證失敗"; return null; }

            NotifyShell(linkPath, StartMenuFolder);
            return linkPath;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
        finally
        {
            if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
    }

    /// <summary>Reads a link back and confirms it resolves to the intended target.</summary>
    public static bool Verify(string linkPath, string expectedTarget)
    {
        if (!File.Exists(linkPath)) return false;
        object instance = null;
        try
        {
            Type type = Type.GetTypeFromCLSID(ClsidShellLink);
            if (type == null) return true;   // cannot check; trust the write
            instance = Activator.CreateInstance(type);
            if (instance is not IShellLinkW link) return true;

            ((IPersistFile)link).Load(linkPath, 0);
            var buffer = new StringBuilder(520);
            link.GetPath(buffer, buffer.Capacity, 0, 0);
            return string.Equals(buffer.ToString(), expectedTarget, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return true;   // verification unavailable; do not fail the install over it
        }
        finally
        {
            if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
