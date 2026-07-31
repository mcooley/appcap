using AppCap;
using global::Windows.Graphics.DirectX.Direct3D11;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Direct2D;
using global::Windows.Win32.Graphics.Direct2D.Common;
using global::Windows.Win32.Graphics.DirectWrite;
using global::Windows.Win32.Graphics.Direct3D11;
using global::Windows.Win32.Graphics.Dxgi;
using global::Windows.Win32.Graphics.Dxgi.Common;
using global::Windows.Win32.System.Com;
using global::Windows.Win32.System.WinRT;
using global::Windows.Win32.System.WinRT.Direct3D11;
using WinRT;

namespace AppCap.Windows;

internal sealed unsafe class CaptionRenderer : IDisposable
{
    private readonly uint width;
    private readonly uint height;
    private readonly float maxWidth;
    private readonly float maxHeight;
    private readonly ComPtr<ID2D1Factory> d2dFactory;
    private readonly ComPtr<IDWriteFactory> writeFactory;
    private readonly ComPtr<IDWriteTextFormat> textFormat;
    private readonly ComPtr<IDWriteInlineObject> ellipsis;
    private readonly ComPtr<IDWriteTextLayout> textLayout;

    public CaptionRenderer(uint width, uint height, string caption)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);

        this.width = width;
        this.height = height;
        float fontSize = Math.Clamp(height / 24f, 18f, 32f);
        maxWidth = Math.Max(1, width - 80f);
        maxHeight = fontSize * 1.4f;

        PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, typeof(ID2D1Factory).GUID, null, out void* d2dFactoryVoid).ThrowOnFailure();
        d2dFactory = new ComPtr<ID2D1Factory>((ID2D1Factory*)d2dFactoryVoid);

        PInvoke.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED, out IDWriteFactory* writeFactoryPointer).ThrowOnFailure();
        writeFactory = new ComPtr<IDWriteFactory>(writeFactoryPointer);

        IDWriteTextFormat* textFormatPointer = null;
        writeFactory.Get()->CreateTextFormat(
            "Trebuchet MS",
            null,
            DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD,
            DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
            fontSize,
            "en-us",
            &textFormatPointer).ThrowOnFailure();
        textFormat = new ComPtr<IDWriteTextFormat>(textFormatPointer);

        textFormat.Get()->SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER).ThrowOnFailure();
        textFormat.Get()->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER).ThrowOnFailure();

        IDWriteInlineObject* ellipsisPointer = null;
        writeFactory.Get()->CreateEllipsisTrimmingSign(textFormat.Get(), &ellipsisPointer).ThrowOnFailure();
        ellipsis = new ComPtr<IDWriteInlineObject>(ellipsisPointer);

        DWRITE_TRIMMING trimming = new()
        {
            granularity = DWRITE_TRIMMING_GRANULARITY.DWRITE_TRIMMING_GRANULARITY_CHARACTER,
        };
        textFormat.Get()->SetTrimming(trimming, ellipsis.Get()).ThrowOnFailure();

        IDWriteTextLayout* textLayoutPointer = null;
        writeFactory.Get()->CreateTextLayout(caption, (uint)caption.Length, textFormat.Get(), maxWidth, maxHeight, &textLayoutPointer).ThrowOnFailure();
        textLayout = new ComPtr<IDWriteTextLayout>(textLayoutPointer);
        textLayout.Get()->SetMaxWidth(maxWidth).ThrowOnFailure();
        textLayout.Get()->SetMaxHeight(maxHeight).ThrowOnFailure();
    }

    public IDirect3DSurface Render(IDirect3DSurface sourceSurface, float opacity = 1)
    {
        ArgumentNullException.ThrowIfNull(sourceSurface);
        ArgumentOutOfRangeException.ThrowIfLessThan(opacity, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, 1);

        using ComPtr<ID3D11Texture2D> sourceTexture = GetTexture(sourceSurface);
        sourceTexture.Get()->GetDesc(out D3D11_TEXTURE2D_DESC textureDescription);
        using ComPtr<ID3D11Device> device = GetDevice(sourceTexture.Get());
        using ComPtr<ID3D11DeviceContext> context = GetImmediateContext(device.Get());
        using ComPtr<ID3D11Texture2D> renderedTexture = CreateRenderableCopy(device.Get(), context.Get(), sourceTexture.Get(), textureDescription);
        using ComPtr<IDXGISurface> renderedDxgiSurface = QueryInterface<IDXGISurface>((IUnknown*)renderedTexture.Get());

        RenderCaption(renderedDxgiSurface.Get(), textureDescription.Format, opacity);

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

    public static IDirect3DSurface Copy(IDirect3DSurface sourceSurface)
    {
        ArgumentNullException.ThrowIfNull(sourceSurface);

        using ComPtr<ID3D11Texture2D> sourceTexture = GetTexture(sourceSurface);
        sourceTexture.Get()->GetDesc(out D3D11_TEXTURE2D_DESC textureDescription);
        using ComPtr<ID3D11Device> device = GetDevice(sourceTexture.Get());
        using ComPtr<ID3D11DeviceContext> context = GetImmediateContext(device.Get());
        using ComPtr<ID3D11Texture2D> copiedTexture = CreateRenderableCopy(device.Get(), context.Get(), sourceTexture.Get(), textureDescription);
        return CreateSurfaceFromTexture(copiedTexture.Get());
    }

    public static IDirect3DSurface Crop(IDirect3DSurface sourceSurface, CropRectangle crop)
    {
        ArgumentNullException.ThrowIfNull(sourceSurface);

        using ComPtr<ID3D11Texture2D> sourceTexture = GetTexture(sourceSurface);
        sourceTexture.Get()->GetDesc(out D3D11_TEXTURE2D_DESC textureDescription);
        crop.ValidateWithin((int)textureDescription.Width, (int)textureDescription.Height);
        using ComPtr<ID3D11Device> device = GetDevice(sourceTexture.Get());
        using ComPtr<ID3D11DeviceContext> context = GetImmediateContext(device.Get());
        using ComPtr<ID3D11Texture2D> croppedTexture = CreateRenderableCrop(device.Get(), context.Get(), sourceTexture.Get(), textureDescription, crop);
        return CreateSurfaceFromTexture(croppedTexture.Get());
    }

    public static IDirect3DSurface Fit(IDirect3DSurface sourceSurface, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(sourceSurface);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        using ComPtr<ID3D11Texture2D> sourceTexture = GetTexture(sourceSurface);
        sourceTexture.Get()->GetDesc(out D3D11_TEXTURE2D_DESC textureDescription);
        using ComPtr<ID3D11Device> device = GetDevice(sourceTexture.Get());
        using ComPtr<ID3D11Texture2D> fittedTexture = CreateRenderableTexture(device.Get(), textureDescription, (uint)width, (uint)height);
        using ComPtr<IDXGISurface> sourceDxgiSurface = QueryInterface<IDXGISurface>((IUnknown*)sourceTexture.Get());
        using ComPtr<IDXGISurface> fittedDxgiSurface = QueryInterface<IDXGISurface>((IUnknown*)fittedTexture.Get());

        RenderFit(sourceDxgiSurface.Get(), fittedDxgiSurface.Get(), textureDescription, width, height);
        return CreateSurfaceFromTexture(fittedTexture.Get());
    }

    public void Dispose()
    {
        textLayout.Dispose();
        ellipsis.Dispose();
        textFormat.Dispose();
        writeFactory.Dispose();
        d2dFactory.Dispose();
    }

    private void RenderCaption(IDXGISurface* dxgiSurface, DXGI_FORMAT format, float opacity)
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

        D2D1_COLOR_F shadow = new()
        {
            r = 0,
            g = 0,
            b = 0,
            a = 0.75f * opacity,
        };
        D2D1_COLOR_F white = new()
        {
            r = 1,
            g = 1,
            b = 1,
            a = opacity,
        };
        ID2D1SolidColorBrush* shadowBrushPointer = null;
        renderTarget.Get()->CreateSolidColorBrush(shadow, null, &shadowBrushPointer).ThrowOnFailure();
        using ComPtr<ID2D1SolidColorBrush> shadowBrush = new(shadowBrushPointer);
        ID2D1SolidColorBrush* textBrushPointer = null;
        renderTarget.Get()->CreateSolidColorBrush(white, null, &textBrushPointer).ThrowOnFailure();
        using ComPtr<ID2D1SolidColorBrush> textBrush = new(textBrushPointer);

        float originX = (width - maxWidth) / 2f;
        float originY = Math.Max(0, height - maxHeight - Math.Max(24f, height / 32f));
        renderTarget.Get()->BeginDraw();
        renderTarget.Get()->DrawTextLayout(new D2D_POINT_2F { x = originX + 2, y = originY + 2 }, textLayout.Get(), (ID2D1Brush*)shadowBrush.Get(), D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NO_SNAP);
        renderTarget.Get()->DrawTextLayout(new D2D_POINT_2F { x = originX, y = originY }, textLayout.Get(), (ID2D1Brush*)textBrush.Get(), D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NO_SNAP);
        renderTarget.Get()->EndDraw().ThrowOnFailure();
    }

    private static void RenderFit(
        IDXGISurface* sourceSurface,
        IDXGISurface* destinationSurface,
        D3D11_TEXTURE2D_DESC sourceDescription,
        int width,
        int height)
    {
        PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED, typeof(ID2D1Factory).GUID, null, out void* factoryPointer).ThrowOnFailure();
        using ComPtr<ID2D1Factory> factory = new((ID2D1Factory*)factoryPointer);
        D2D1_RENDER_TARGET_PROPERTIES properties = new()
        {
            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = sourceDescription.Format,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_IGNORE,
            },
            dpiX = 96,
            dpiY = 96,
            usage = D2D1_RENDER_TARGET_USAGE.D2D1_RENDER_TARGET_USAGE_NONE,
            minLevel = D2D1_FEATURE_LEVEL.D2D1_FEATURE_LEVEL_DEFAULT,
        };
        ID2D1RenderTarget* renderTargetPointer = null;
        factory.Get()->CreateDxgiSurfaceRenderTarget(destinationSurface, properties, &renderTargetPointer).ThrowOnFailure();
        using ComPtr<ID2D1RenderTarget> renderTarget = new(renderTargetPointer);

        D2D1_BITMAP_PROPERTIES bitmapProperties = new()
        {
            pixelFormat = properties.pixelFormat,
            dpiX = 96,
            dpiY = 96,
        };
        ID2D1Bitmap* bitmapPointer = null;
        Guid surfaceId = typeof(IDXGISurface).GUID;
        renderTarget.Get()->CreateSharedBitmap(&surfaceId, sourceSurface, &bitmapProperties, &bitmapPointer).ThrowOnFailure();
        using ComPtr<ID2D1Bitmap> bitmap = new(bitmapPointer);

        float scale = Math.Min(width / (float)sourceDescription.Width, height / (float)sourceDescription.Height);
        float fittedWidth = sourceDescription.Width * scale;
        float fittedHeight = sourceDescription.Height * scale;
        D2D_RECT_F destination = new()
        {
            left = (width - fittedWidth) / 2,
            top = (height - fittedHeight) / 2,
            right = (width + fittedWidth) / 2,
            bottom = (height + fittedHeight) / 2,
        };
        D2D1_COLOR_F black = new() { r = 0, g = 0, b = 0, a = 1 };
        renderTarget.Get()->BeginDraw();
        renderTarget.Get()->Clear(&black);
        renderTarget.Get()->DrawBitmap(bitmap.Get(), &destination, 1, D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, null);
        renderTarget.Get()->EndDraw().ThrowOnFailure();
    }

    private static ComPtr<ID3D11Texture2D> CreateRenderableCopy(ID3D11Device* device, ID3D11DeviceContext* context, ID3D11Texture2D* sourceTexture, D3D11_TEXTURE2D_DESC sourceDescription)
    {
        ComPtr<ID3D11Texture2D> renderedTexture = CreateRenderableTexture(device, sourceDescription, sourceDescription.Width, sourceDescription.Height);
        context->CopyResource((ID3D11Resource*)renderedTexture.Get(), (ID3D11Resource*)sourceTexture);
        return renderedTexture;
    }

    private static ComPtr<ID3D11Texture2D> CreateRenderableCrop(
        ID3D11Device* device,
        ID3D11DeviceContext* context,
        ID3D11Texture2D* sourceTexture,
        D3D11_TEXTURE2D_DESC sourceDescription,
        CropRectangle crop)
    {
        ComPtr<ID3D11Texture2D> renderedTexture = CreateRenderableTexture(device, sourceDescription, (uint)crop.Width, (uint)crop.Height);
        D3D11_BOX sourceRegion = new()
        {
            left = (uint)crop.X,
            top = (uint)crop.Y,
            front = 0,
            right = (uint)(crop.X + crop.Width),
            bottom = (uint)(crop.Y + crop.Height),
            back = 1,
        };
        context->CopySubresourceRegion(
            (ID3D11Resource*)renderedTexture.Get(),
            0,
            0,
            0,
            0,
            (ID3D11Resource*)sourceTexture,
            0,
            &sourceRegion);
        return renderedTexture;
    }

    private static ComPtr<ID3D11Texture2D> CreateRenderableTexture(ID3D11Device* device, D3D11_TEXTURE2D_DESC sourceDescription, uint width, uint height)
    {
        sourceDescription.Width = width;
        sourceDescription.Height = height;
        sourceDescription.BindFlags |= D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET;
        sourceDescription.CPUAccessFlags = 0;
        sourceDescription.Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT;
        sourceDescription.MiscFlags = 0;

        ID3D11Texture2D* renderedTexture = null;
        device->CreateTexture2D(sourceDescription, null, &renderedTexture).ThrowOnFailure();
        return new ComPtr<ID3D11Texture2D>(renderedTexture);
    }

    private static IDirect3DSurface CreateSurfaceFromTexture(ID3D11Texture2D* texture)
    {
        using ComPtr<IDXGISurface> copiedDxgiSurface = QueryInterface<IDXGISurface>((IUnknown*)texture);
        global::Windows.Win32.System.WinRT.IInspectable* copiedInspectable = null;
        PInvoke.CreateDirect3D11SurfaceFromDXGISurface(copiedDxgiSurface.Get(), &copiedInspectable).ThrowOnFailure();
        try
        {
            return MarshalInterface<IDirect3DSurface>.FromAbi((nint)copiedInspectable);
        }
        finally
        {
            copiedInspectable->Release();
        }
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
