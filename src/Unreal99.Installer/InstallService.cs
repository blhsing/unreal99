using System.Diagnostics;
using System.Text.Json;
using Unreal99.Platform;
using GameShortcut = Unreal99.Platform.Shortcut;

namespace Unreal99.Setup;

internal sealed record InstallOptions(string InstallDirectory, string SourceDirectory, bool CreateStartMenu);
internal sealed record InstallProgress(int Percent, string Message);
internal sealed record InstallManifest(string Product, string Version, DateTimeOffset InstalledAt,
    bool StartMenuShortcut, List<string> Files);

internal static class InstallService
{
    public const string ProductName = "虛幻競技場 99";
    public const string ManifestName = ".unreal99-install.json";
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Unreal99");

    public static string FindPayload(string explicitSource = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitSource)) candidates.Add(explicitSource);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "payload"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "dist"));
        candidates.Add(Path.Combine(Environment.CurrentDirectory, "dist"));

        DirectoryInfo cursor = new(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && cursor != null; i++, cursor = cursor.Parent)
            candidates.Add(Path.Combine(cursor.FullName, "dist"));

        foreach (string candidate in candidates)
        {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(full, "Unreal99.exe"))) return full;
        }
        throw new DirectoryNotFoundException(
            "找不到遊戲安裝檔。請使用完整發行套件，或以 --source 指定包含 Unreal99.exe 的目錄。");
    }

    public static async Task InstallAsync(InstallOptions options, IProgress<InstallProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("安裝程式僅支援 Windows。");
        string source = Path.GetFullPath(options.SourceDirectory);
        string target = Path.GetFullPath(options.InstallDirectory);
        ValidateDirectories(source, target);

        InstallManifest previous = null;
        string previousManifestPath = Path.Combine(target, ManifestName);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            if (!File.Exists(previousManifestPath))
                throw new InvalidOperationException("選擇的安裝目錄不是空的。請選擇空白目錄或現有的虛幻競技場安裝位置。");
            previous = JsonSerializer.Deserialize<InstallManifest>(
                await File.ReadAllTextAsync(previousManifestPath, cancellationToken), JsonOptions)
                ?? throw new InvalidDataException("現有安裝紀錄已損毀，無法安全更新。");
        }

        string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        if (files.Length == 0) throw new InvalidOperationException("安裝檔目錄是空的。");
        Directory.CreateDirectory(target);
        var installed = new List<string>(files.Length + 1);

        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(source, files[i]);
            string destination = SafeDestination(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = destination + ".installing";
            try
            {
                await using (FileStream input = File.OpenRead(files[i]))
                await using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous))
                    await input.CopyToAsync(output, cancellationToken);
                File.Move(temporary, destination, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            installed.Add(relative);
            progress?.Report(new InstallProgress((i + 1) * 88 / files.Length,
                $"複製 {Path.GetFileName(relative)}"));
        }

        string iconPath = Path.Combine(target, "Unreal99.ico");
        AppIcon.WriteIco(iconPath);
        if (!installed.Contains("Unreal99.ico", StringComparer.OrdinalIgnoreCase)) installed.Add("Unreal99.ico");
        progress?.Report(new InstallProgress(92, "建立應用程式圖示"));

        // Remove files owned by an older package that no longer exist in the new payload.
        if (previous != null)
        {
            var currentFiles = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
            foreach (string stale in previous.Files.Where(f => !currentFiles.Contains(f)))
            {
                string stalePath = SafeDestination(target, stale);
                if (File.Exists(stalePath)) File.Delete(stalePath);
            }
        }

        if (options.CreateStartMenu)
        {
            string executable = Path.Combine(target, "Unreal99.exe");
            string link = GameShortcut.Create(ProductName, executable, "", iconPath,
                "虛幻競技場 99 — 重製版");
            if (link == null) throw new InvalidOperationException($"無法建立開始選單捷徑。{GameShortcut.LastError}");
        }
        else if (previous?.StartMenuShortcut == true)
        {
            string oldShortcut = GameShortcut.DefaultPath(ProductName);
            if (File.Exists(oldShortcut)) File.Delete(oldShortcut);
        }

        string version = FileVersionInfo.GetVersionInfo(Path.Combine(target, "Unreal99.exe")).FileVersion ?? "1.0";
        var manifest = new InstallManifest(ProductName, version, DateTimeOffset.Now,
            options.CreateStartMenu, installed);
        await File.WriteAllTextAsync(Path.Combine(target, ManifestName),
            JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        progress?.Report(new InstallProgress(100, "安裝完成"));
    }

    public static async Task UninstallAsync(string installDirectory,
        IProgress<InstallProgress> progress = null, CancellationToken cancellationToken = default)
    {
        string target = Path.GetFullPath(installDirectory);
        string manifestPath = Path.Combine(target, ManifestName);
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("此目錄中找不到安裝紀錄。", manifestPath);
        InstallManifest manifest = JsonSerializer.Deserialize<InstallManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("安裝紀錄已損毀。");

        string shortcut = GameShortcut.DefaultPath(ProductName);
        if (File.Exists(shortcut)) File.Delete(shortcut);
        for (int i = 0; i < manifest.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = SafeDestination(target, manifest.Files[i]);
            if (File.Exists(path)) File.Delete(path);
            progress?.Report(new InstallProgress((i + 1) * 94 / Math.Max(1, manifest.Files.Count),
                $"移除 {Path.GetFileName(path)}"));
        }
        File.Delete(manifestPath);
        foreach (string directory in Directory.GetDirectories(target, "*", SearchOption.AllDirectories)
                     .OrderByDescending(p => p.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
        progress?.Report(new InstallProgress(100, "移除完成"));
    }

    public static bool IsInstalled(string directory)
        => File.Exists(Path.Combine(Path.GetFullPath(directory), ManifestName));

    private static void ValidateDirectories(string source, string target)
    {
        if (!File.Exists(Path.Combine(source, "Unreal99.exe")))
            throw new FileNotFoundException("來源目錄中找不到 Unreal99.exe。");
        string prefix = source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (target.Equals(source, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安裝位置不可位於安裝檔來源目錄內。");
    }

    private static string SafeDestination(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"安裝檔包含不安全的路徑：{relative}");
        return full;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
