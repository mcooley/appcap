using System.Diagnostics;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;

namespace AppCap.Windows;

internal sealed record ScreenshotMetadata(string CapturedFrom)
{
    public static ScreenshotMetadata? TryCreate(TargetWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        HWND hwnd = new(window.Handle);
        string? title = GetWindowTitle(hwnd);
        _ = PInvoke.GetWindowThreadProcessId(hwnd, out uint processId);
        string? version = GetApplicationVersion((int)processId);
        string capturedFrom = FormatCapturedFrom(title, version);
        return capturedFrom.Length > 0 ? new ScreenshotMetadata(capturedFrom) : null;
    }

    private static string FormatCapturedFrom(string? title, string? version)
    {
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(version))
        {
            return $"Captured from {title} {version}";
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            return $"Captured from {title}";
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            return $"Captured from {version}";
        }

        return string.Empty;
    }

    private static string? GetWindowTitle(HWND hwnd)
    {
        int length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return null;
        }

        char[] buffer = new char[length + 1];
        int copied = PInvoke.GetWindowText(hwnd, buffer);
        if (copied > 0)
        {
            return new string(buffer, 0, copied);
        }

        return null;
    }

    private static string? GetApplicationVersion(int processId)
    {
        if (processId != 0 && ProcessPackage.TryGetPackageVersion(processId, out Version? packageVersion))
        {
            return packageVersion?.ToString();
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            string? fileVersion = process.MainModule?.FileVersionInfo.FileVersion;
            if (!string.IsNullOrWhiteSpace(fileVersion))
            {
                return fileVersion;
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        return null;
    }
}