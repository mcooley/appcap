using RunMc;
namespace RunMc.Windows;

using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Storage.Packaging.Appx;
using global::Windows.Win32.System.Threading;

public static class ProcessPackage
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

    public static unsafe bool TryGetPackageVersion(int processId, out Version? version)
    {
        version = null;

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
            WIN32_ERROR result = PInvoke.GetPackageId(processHandle, &length, null);
            if (result == WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE)
            {
                return false;
            }

            if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length <= 0)
            {
                return false;
            }

            byte[] buffer = new byte[length];
            fixed (byte* bufferPointer = buffer)
            {
                result = PInvoke.GetPackageId(processHandle, &length, bufferPointer);
                if (result != WIN32_ERROR.NO_ERROR)
                {
                    return false;
                }

                PACKAGE_ID* packageId = (PACKAGE_ID*)bufferPointer;
                PACKAGE_VERSION packageVersion = packageId->version;
                version = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
                return true;
            }
        }
        finally
        {
            _ = PInvoke.CloseHandle(processHandle);
        }
    }
}