namespace RunMc;

public static class WindowsProcessPackage
{
    public static bool TryGetPackageFamilyName(int processId, out string? packageFamilyName)
    {
        packageFamilyName = null;

        nint processHandle = WindowsNative.OpenProcess(
            WindowsNative.ProcessQueryLimitedInformation,
            inheritHandle: false,
            (uint)processId);
        if (processHandle == 0)
        {
            return false;
        }

        try
        {
            int length = 0;
            int result = WindowsNative.GetPackageFamilyName(processHandle, ref length, null);
            if (result is WindowsNative.AppModelErrorNoPackage)
            {
                return false;
            }

            if (result is not WindowsNative.ErrorInsufficientBuffer || length <= 0)
            {
                return false;
            }

            char[] buffer = new char[length];
            result = WindowsNative.GetPackageFamilyName(processHandle, ref length, buffer);
            if (result is not 0)
            {
                return false;
            }

            packageFamilyName = new string(buffer, 0, Math.Max(0, length - 1));
            return true;
        }
        finally
        {
            _ = WindowsNative.CloseHandle(processHandle);
        }
    }
}