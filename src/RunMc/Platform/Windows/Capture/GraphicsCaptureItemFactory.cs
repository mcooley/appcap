using System.Runtime.InteropServices;
using WinRT.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.System.WinRT.Graphics.Capture;
using Windows.Graphics.Capture;
using WinRT;

namespace RunMc;

internal static class GraphicsCaptureItemFactory
{
    private const string GraphicsCaptureItemRuntimeClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid GraphicsCaptureItemInterfaceId = Guid.Parse("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateForWindow(nint windowHandle)
    {
        using ObjectReference<IGraphicsCaptureItemInterop.Vtbl> factory = ActivationFactory.Get(GraphicsCaptureItemRuntimeClassName)
            .As<IGraphicsCaptureItemInterop.Vtbl>(IGraphicsCaptureItemInterop.IID_Guid);
        nint itemPointer = 0;
        try
        {
            int result = CreateCaptureItemForWindow(factory, windowHandle, out itemPointer);
            Marshal.ThrowExceptionForHR(result);
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            if (itemPointer != 0)
            {
                Marshal.Release(itemPointer);
            }
        }
    }

    private static unsafe int CreateCaptureItemForWindow(
        ObjectReference<IGraphicsCaptureItemInterop.Vtbl> factory,
        nint windowHandle,
        out nint itemPointer)
    {
        Guid itemInterfaceId = GraphicsCaptureItemInterfaceId;
        void* resultPointer = null;
        int result = factory.Vftbl.CreateForWindow_4(
            (IGraphicsCaptureItemInterop*)factory.ThisPtr,
            new HWND(windowHandle),
            &itemInterfaceId,
            &resultPointer);
        itemPointer = (nint)resultPointer;
        return result;
    }
}
