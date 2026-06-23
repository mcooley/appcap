using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace RunMc;

internal static partial class Direct3DDeviceFactory
{
    private static readonly Guid DxgiDeviceInterfaceId = Guid.Parse("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public static Direct3DDeviceLease CreateDevice()
    {
        nint d3dDevice = 0;
        nint dxgiDevice = 0;
        nint direct3DDevice = 0;
        try
        {
            int result = D3D11CreateDevice(
                0,
                D3DDriverType.Hardware,
                0,
                D3D11CreateDeviceFlags.BgraSupport,
                0,
                0,
                D3D11SdkVersion,
                out d3dDevice,
                out _,
                out nint immediateContext);
            if (immediateContext != 0)
            {
                Marshal.Release(immediateContext);
            }

            Marshal.ThrowExceptionForHR(result);

            result = Marshal.QueryInterface(d3dDevice, DxgiDeviceInterfaceId, out dxgiDevice);
            Marshal.ThrowExceptionForHR(result);

            result = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out direct3DDevice);
            Marshal.ThrowExceptionForHR(result);

            return new Direct3DDeviceLease(MarshalInterface<IDirect3DDevice>.FromAbi(direct3DDevice));
        }
        finally
        {
            if (direct3DDevice != 0)
            {
                Marshal.Release(direct3DDevice);
            }

            if (dxgiDevice != 0)
            {
                Marshal.Release(dxgiDevice);
            }

            if (d3dDevice != 0)
            {
                Marshal.Release(d3dDevice);
            }
        }
    }

    private const uint D3D11SdkVersion = 7;

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(
        nint adapter,
        D3DDriverType driverType,
        nint software,
        D3D11CreateDeviceFlags flags,
        nint featureLevels,
        uint featureLevelsCount,
        uint sdkVersion,
        out nint device,
        out D3DFeatureLevel featureLevel,
        out nint immediateContext);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    private enum D3DDriverType : uint
    {
        Hardware = 1,
    }

    [Flags]
    private enum D3D11CreateDeviceFlags : uint
    {
        BgraSupport = 0x20,
    }

    private enum D3DFeatureLevel : uint
    {
    }
}

internal sealed class Direct3DDeviceLease : IDisposable
{
    public Direct3DDeviceLease(IDirect3DDevice device)
    {
        Device = device;
    }

    public IDirect3DDevice Device { get; }

    public void Dispose()
    {
        (Device as IDisposable)?.Dispose();
    }
}