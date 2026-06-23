namespace RunMc;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

public static class WindowsProcessPackage
{
    public static unsafe bool TryGetPackageFamilyName(int processId, out string? packageFamilyName)
    {
        packageFamilyName = null;

        HANDLE processHandle = PInvoke.OpenProcess(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            (uint)processId);
        if (processHandle.IsNull)
        {
            return false;
        }

        try
        {
            uint length = 0;
            WIN32_ERROR result = PInvoke.GetPackageFamilyName(processHandle, &length, default);
            if (result == WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE)
            {
                return false;
            }

            if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length <= 0)
            {
                return false;
            }

            char[] buffer = new char[length];
            fixed (char* bufferPointer = buffer)
            {
                result = PInvoke.GetPackageFamilyName(processHandle, &length, new PWSTR(bufferPointer));
            }

            if (result != WIN32_ERROR.NO_ERROR)
            {
                return false;
            }

            packageFamilyName = new string(buffer, 0, Math.Max(0, checked((int)length) - 1));
            return true;
        }
        finally
        {
            _ = PInvoke.CloseHandle(processHandle);
        }
    }
}