using System.Text.Json;

namespace Unreal99.Platform;

/// <summary>
/// Where the game keeps things that must survive a restart: preferences, key bindings and
/// saved matches.
///
/// Everything lands under <c>%APPDATA%\Unreal99</c> rather than beside the executable, because
/// an install directory is frequently read-only and, when the game is launched from a packaged
/// container, writes next to the binary get silently redirected somewhere the user will never
/// find. The roaming profile is writable in both cases.
/// </summary>
public static class UserData
{
    public static string Root { get; } = ResolveRoot();
    public static string SavesDirectory => Path.Combine(Root, "saves");
    public static string SettingsPath => Path.Combine(Root, "settings.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    private static string ResolveRoot()
    {
        // Automated persistence tests need a real, process-local override. Windows may ignore
        // an APPDATA environment substitution because GetFolderPath resolves the registered
        // known folder instead; this explicit path keeps tests out of the player's profile.
        string overrideRoot = Environment.GetEnvironmentVariable("UNREAL99_USERDATA");
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return Path.GetFullPath(overrideRoot);
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData)) return Path.Combine(appData, "Unreal99");
        }
        catch { /* fall through to the executable directory */ }
        return Path.Combine(AppContext.BaseDirectory, "userdata");
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SavesDirectory);
    }

    /// <summary>
    /// Writes through a temporary file and then replaces the target, so a crash or a power cut
    /// midway leaves the previous file intact rather than a half-written one. A settings file
    /// truncated at byte zero would silently reset every binding the player had set.
    /// </summary>
    public static bool WriteAtomic(string path, string contents)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"寫入失敗 {path}: {ex.Message}");
            return false;
        }
    }

    public static bool WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) fs.Write(bytes);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"寫入失敗 {path}: {ex.Message}");
            return false;
        }
    }

    public static string ReadTextOrNull(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception ex) { Console.WriteLine($"讀取失敗 {path}: {ex.Message}"); return null; }
    }

    /// <summary>Deserialises, returning null on anything malformed rather than throwing.</summary>
    public static T ReadJsonOrNull<T>(string path) where T : class
    {
        string text = ReadTextOrNull(path);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<T>(text, Json); }
        catch (Exception ex) { Console.WriteLine($"解析失敗 {path}: {ex.Message}"); return null; }
    }

    public static bool WriteJson<T>(string path, T value)
    {
        try { return WriteAtomic(path, JsonSerializer.Serialize(value, Json)); }
        catch (Exception ex) { Console.WriteLine($"序列化失敗 {path}: {ex.Message}"); return false; }
    }

    public static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Console.WriteLine($"刪除失敗 {path}: {ex.Message}"); }
    }
}
