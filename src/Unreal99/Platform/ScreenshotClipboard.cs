using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Unreal99.Platform;

/// <summary>
/// Copies an OpenGL RGBA framebuffer to the Windows clipboard as a 32-bit CF_DIB. Print Screen
/// normally asks the desktop compositor for pixels, which can be blank for fullscreen OpenGL;
/// publishing the game's own completed framebuffer preserves the familiar clipboard workflow.
/// </summary>
public static class ScreenshotClipboard
{
    private const uint GmemMoveable = 0x0002;
    private const uint CfDib = 8;
    private const int BitmapInfoHeaderSize = 40;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);

    public static bool TrySetRgba(nint owner, int width, int height, ReadOnlySpan<byte> rgba,
        out string error)
    {
        error = "";
        if (!OperatingSystem.IsWindows())
        {
            error = "此平台沒有 Windows 剪貼簿。";
            return false;
        }
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
        {
            error = "截圖像素尺寸無效。";
            return false;
        }

        int imageBytes = checked(width * height * 4);
        byte[] dib = new byte[BitmapInfoHeaderSize + imageBytes];
        Span<byte> header = dib.AsSpan(0, BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[0..4], BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], width);
        // Positive height means bottom-up, exactly matching glReadPixels row order.
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], height);
        BinaryPrimitives.WriteInt16LittleEndian(header[12..14], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..16], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], 0); // BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(header[20..24], imageBytes);

        Span<byte> bgra = dib.AsSpan(BitmapInfoHeaderSize);
        for (int i = 0; i < imageBytes; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = 255;
        }

        nint memory = GlobalAlloc(GmemMoveable, (nuint)dib.Length);
        if (memory == 0)
        {
            error = $"GlobalAlloc 失敗（{Marshal.GetLastWin32Error()}）。";
            return false;
        }

        bool clipboardOpen = false;
        try
        {
            nint target = GlobalLock(memory);
            if (target == 0)
            {
                error = $"GlobalLock 失敗（{Marshal.GetLastWin32Error()}）。";
                return false;
            }
            try { Marshal.Copy(dib, 0, target, dib.Length); }
            finally { GlobalUnlock(memory); }

            // Clipboard viewers can hold the mutex briefly after Print Screen. A few short retries
            // keep the key reliable without delaying the render loop perceptibly.
            for (int attempt = 0; attempt < 6 && !clipboardOpen; attempt++)
            {
                clipboardOpen = OpenClipboard(owner);
                if (!clipboardOpen) Thread.Sleep(8);
            }
            if (!clipboardOpen)
            {
                error = $"OpenClipboard 失敗（{Marshal.GetLastWin32Error()}）。";
                return false;
            }
            if (!EmptyClipboard())
            {
                error = $"EmptyClipboard 失敗（{Marshal.GetLastWin32Error()}）。";
                return false;
            }
            if (SetClipboardData(CfDib, memory) == 0)
            {
                error = $"SetClipboardData 失敗（{Marshal.GetLastWin32Error()}）。";
                return false;
            }

            // Windows owns the HGLOBAL after SetClipboardData succeeds.
            memory = 0;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (clipboardOpen) CloseClipboard();
            if (memory != 0) GlobalFree(memory);
        }
    }
}
