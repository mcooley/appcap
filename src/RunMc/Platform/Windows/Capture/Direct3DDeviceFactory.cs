using RunMc;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Graphics.Direct3D;
using global::Windows.Win32.Graphics.Direct3D11;
using global::Windows.Win32.Graphics.Dxgi;
using WinRT;

namespace RunMc.Windows;

internal static partial class Direct3DDeviceFactory
{
    public static unsafe Direct3DDeviceLease CreateDevice()
    {
        ID3D11Device* d3dDevice = null;
        ID3D11DeviceContext* immediateContext = null;
        IDXGIDevice* dxgiDevice = null;
        global::Windows.Win32.System.WinRT.IInspectable* direct3DDevice = null;
        try
        {
            PInvoke.D3D11CreateDevice(
                null,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                [],
                D3D11SdkVersion,
                &d3dDevice,
                out _,
                &immediateContext).ThrowOnFailure();

            d3dDevice->QueryInterface(out dxgiDevice).ThrowOnFailure();

            PInvoke.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, &direct3DDevice).ThrowOnFailure();

            return new Direct3DDeviceLease(MarshalInterface<IDirect3DDevice>.FromAbi((nint)direct3DDevice));
        }
        finally
        {
            if (direct3DDevice is not null)
            {
                direct3DDevice->Release();
            }

            if (dxgiDevice is not null)
            {
                dxgiDevice->Release();
            }

            if (immediateContext is not null)
            {
                immediateContext->Release();
            }

            if (d3dDevice is not null)
            {
                d3dDevice->Release();
            }
        }
    }

    private const uint D3D11SdkVersion = 7;
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