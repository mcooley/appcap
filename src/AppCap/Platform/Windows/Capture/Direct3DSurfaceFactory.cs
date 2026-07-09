using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Graphics.Direct3D;
using global::Windows.Win32.Graphics.Direct3D11;
using global::Windows.Win32.Graphics.Dxgi;
using global::Windows.Win32.Graphics.Dxgi.Common;
using WinRT;

namespace AppCap.Windows;

// Builds a Direct3D surface from raw BGRA8 pixels. The client uses this to turn the
// raw image data returned by the capture protocol back into a surface so it can reuse
// the existing D2D caption renderer before saving the PNG. Keeping the produced device
// alive alongside the surface is the caller's responsibility via the returned lease.
internal static unsafe class Direct3DSurfaceFactory
{
    private const uint D3D11SdkVersion = 7;

    public static Direct3DImageSurface CreateFromBgraPixels(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* texture = null;
        IDXGISurface* dxgiSurface = null;
        global::Windows.Win32.System.WinRT.IInspectable* inspectable = null;
        try
        {
            PInvoke.D3D11CreateDevice(
                null,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                [],
                D3D11SdkVersion,
                &device,
                out _,
                &context).ThrowOnFailure();

            D3D11_TEXTURE2D_DESC description = new()
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET,
                CPUAccessFlags = 0,
                MiscFlags = 0,
            };

            fixed (byte* pixelPointer = pixels)
            {
                D3D11_SUBRESOURCE_DATA initialData = new()
                {
                    pSysMem = pixelPointer,
                    SysMemPitch = (uint)(width * 4),
                    SysMemSlicePitch = (uint)(width * height * 4),
                };
                device->CreateTexture2D(description, initialData, &texture).ThrowOnFailure();
            }

            texture->QueryInterface(out dxgiSurface).ThrowOnFailure();
            PInvoke.CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface, &inspectable).ThrowOnFailure();

            IDirect3DSurface surface = MarshalInterface<IDirect3DSurface>.FromAbi((nint)inspectable);
            return new Direct3DImageSurface(surface, device);
        }
        catch
        {
            if (device is not null)
            {
                device->Release();
            }

            throw;
        }
        finally
        {
            if (inspectable is not null)
            {
                inspectable->Release();
            }

            if (dxgiSurface is not null)
            {
                dxgiSurface->Release();
            }

            if (texture is not null)
            {
                texture->Release();
            }

            if (context is not null)
            {
                context->Release();
            }
        }
    }
}

// Owns a Direct3D surface built from raw pixels together with the D3D device that backs
// it, keeping the device alive for as long as the surface (and any surface derived from
// it, such as a captioned copy) is in use.
internal sealed unsafe class Direct3DImageSurface : IDisposable
{
    private ID3D11Device* device;

    public Direct3DImageSurface(IDirect3DSurface surface, ID3D11Device* device)
    {
        Surface = surface;
        this.device = device;
    }

    public IDirect3DSurface Surface { get; }

    public void Dispose()
    {
        (Surface as IDisposable)?.Dispose();
        if (device is not null)
        {
            device->Release();
            device = null;
        }
    }
}
