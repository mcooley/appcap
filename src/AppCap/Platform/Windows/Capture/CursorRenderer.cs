using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Direct2D;
using global::Windows.Win32.Graphics.Direct2D.Common;
using global::Windows.Win32.Graphics.Direct3D11;
using global::Windows.Win32.Graphics.Dxgi;
using global::Windows.Win32.Graphics.Dxgi.Common;
using global::Windows.Win32.System.Com;
using global::Windows.Win32.System.WinRT;
using global::Windows.Win32.System.WinRT.Direct3D11;
using WinRT;

namespace AppCap.Windows;

internal sealed unsafe class CursorRenderer : IDisposable
{
    private readonly float x;
    private readonly float y;
    private readonly ComPtr<ID2D1Factory> d2dFactory;

    public CursorRenderer(float x, float y)
    {
        this.x = x;
        this.y = y;
        PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, typeof(ID2D1Factory).GUID, null, out void* d2dFactoryVoid).ThrowOnFailure();
        d2dFactory = new ComPtr<ID2D1Factory>((ID2D1Factory*)d2dFactoryVoid);
    }

    public IDirect3DSurface Render(IDirect3DSurface sourceSurface)
    {
        ArgumentNullException.ThrowIfNull(sourceSurface);

        using ComPtr<ID3D11Texture2D> sourceTexture = GetTexture(sourceSurface);
        sourceTexture.Get()->GetDesc(out D3D11_TEXTURE2D_DESC textureDescription);
        using ComPtr<ID3D11Device> device = GetDevice(sourceTexture.Get());
        using ComPtr<ID3D11DeviceContext> context = GetImmediateContext(device.Get());
        using ComPtr<ID3D11Texture2D> renderedTexture = CreateRenderableCopy(device.Get(), context.Get(), sourceTexture.Get(), textureDescription);
        using ComPtr<IDXGISurface> renderedDxgiSurface = QueryInterface<IDXGISurface>((IUnknown*)renderedTexture.Get());

        RenderCursor(renderedDxgiSurface.Get(), textureDescription.Format);

        global::Windows.Win32.System.WinRT.IInspectable* renderedInspectable = null;
        PInvoke.CreateDirect3D11SurfaceFromDXGISurface(renderedDxgiSurface.Get(), &renderedInspectable).ThrowOnFailure();
        try
        {
            return MarshalInterface<IDirect3DSurface>.FromAbi((nint)renderedInspectable);
        }
        finally
        {
            renderedInspectable->Release();
        }
    }

    public void Dispose() => d2dFactory.Dispose();

    private void RenderCursor(IDXGISurface* dxgiSurface, DXGI_FORMAT format)
    {
        D2D1_RENDER_TARGET_PROPERTIES renderTargetProperties = new()
        {
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = format,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
            },
            dpiX = 96,
            dpiY = 96,
            usage = D2D1_RENDER_TARGET_USAGE.D2D1_RENDER_TARGET_USAGE_NONE,
            minLevel = D2D1_FEATURE_LEVEL.D2D1_FEATURE_LEVEL_DEFAULT,
        };
        ID2D1RenderTarget* renderTargetPointer = null;
        d2dFactory.Get()->CreateDxgiSurfaceRenderTarget(dxgiSurface, renderTargetProperties, &renderTargetPointer).ThrowOnFailure();
        using ComPtr<ID2D1RenderTarget> renderTarget = new(renderTargetPointer);

        D2D1_COLOR_F black = new() { r = 0, g = 0, b = 0, a = 1 };
        ID2D1SolidColorBrush* brushPointer = null;
        renderTarget.Get()->CreateSolidColorBrush(black, null, &brushPointer).ThrowOnFailure();
        using ComPtr<ID2D1SolidColorBrush> brush = new(brushPointer);

        renderTarget.Get()->BeginDraw();
        renderTarget.Get()->FillRectangle(new D2D_RECT_F { left = x, top = y, right = x + 8, bottom = y + 8 }, (ID2D1Brush*)brush.Get());
        renderTarget.Get()->EndDraw().ThrowOnFailure();
    }

    private static ComPtr<ID3D11Texture2D> CreateRenderableCopy(ID3D11Device* device, ID3D11DeviceContext* context, ID3D11Texture2D* sourceTexture, D3D11_TEXTURE2D_DESC sourceDescription)
    {
        sourceDescription.BindFlags |= D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET;
        sourceDescription.CPUAccessFlags = 0;
        sourceDescription.Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT;
        sourceDescription.MiscFlags = 0;

        ID3D11Texture2D* renderedTexture = null;
        device->CreateTexture2D(sourceDescription, null, &renderedTexture).ThrowOnFailure();
        context->CopyResource((ID3D11Resource*)renderedTexture, (ID3D11Resource*)sourceTexture);
        return new ComPtr<ID3D11Texture2D>(renderedTexture);
    }

    private static ComPtr<ID3D11Device> GetDevice(ID3D11Texture2D* texture)
    {
        ID3D11Device* device = null;
        texture->GetDevice(&device);
        return new ComPtr<ID3D11Device>(device);
    }

    private static ComPtr<ID3D11DeviceContext> GetImmediateContext(ID3D11Device* device)
    {
        ID3D11DeviceContext* context = null;
        device->GetImmediateContext(&context);
        return new ComPtr<ID3D11DeviceContext>(context);
    }

    private static ComPtr<ID3D11Texture2D> GetTexture(IDirect3DSurface surface)
    {
        nint surfaceAbi = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            ((IUnknown*)surfaceAbi)->QueryInterface<IDirect3DDxgiInterfaceAccess>(out IDirect3DDxgiInterfaceAccess* accessPointer).ThrowOnFailure();
            using ComPtr<IDirect3DDxgiInterfaceAccess> access = new(accessPointer);

            access.Get()->GetInterface(out ID3D11Texture2D* texturePointer).ThrowOnFailure();
            return new ComPtr<ID3D11Texture2D>(texturePointer);
        }
        finally
        {
            MarshalInterface<IDirect3DSurface>.DisposeAbi(surfaceAbi);
        }
    }

    private static ComPtr<T> QueryInterface<T>(IUnknown* unknown)
        where T : unmanaged
    {
        unknown->QueryInterface(out T* result).ThrowOnFailure();
        return new ComPtr<T>(result);
    }
}