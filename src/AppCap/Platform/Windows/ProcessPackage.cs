using AppCap;
using System.Runtime.InteropServices;

namespace AppCap.Windows;

using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Storage.Packaging.Appx;
using global::Windows.Win32.System.Threading;

public static class ProcessPackage
{
    public static bool TryGetPackageFamilyName(int processId, out string? packageFamilyName)
    {
        packageFamilyName = null;

        using Microsoft.Win32.SafeHandles.SafeFileHandle processHandle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            (uint)processId);
        if (processHandle.IsInvalid)
        {
            return false;
        }

        uint length = 0;
        WIN32_ERROR result = PInvoke.GetPackageFamilyName(processHandle, ref length);
        if (result == WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE)
        {
            return false;
        }

        if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length <= 0)
        {
            return false;
        }

        char[] buffer = new char[length];
        result = PInvoke.GetPackageFamilyName(processHandle, ref length, buffer);
        if (result != WIN32_ERROR.NO_ERROR)
        {
            return false;
        }

        packageFamilyName = new string(buffer, 0, Math.Max(0, checked((int)length) - 1));
        return true;
    }

    public static bool TryGetPackageVersion(int processId, out Version? version)
    {
        version = null;

        using Microsoft.Win32.SafeHandles.SafeFileHandle processHandle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            (uint)processId);
        if (processHandle.IsInvalid)
        {
            return false;
        }

        uint length = 0;
        WIN32_ERROR result = PInvoke.GetPackageId(processHandle, ref length);
        if (result == WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE)
        {
            return false;
        }

        if (result != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || length <= 0)
        {
            return false;
        }

        byte[] buffer = new byte[length];
        result = PInvoke.GetPackageId(processHandle, ref length, buffer);
        if (result != WIN32_ERROR.NO_ERROR)
        {
            return false;
        }

        PACKAGE_ID packageId = MemoryMarshal.Read<PACKAGE_ID>(buffer);
        PACKAGE_VERSION packageVersion = packageId.version;
        version = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        return true;
    }
}