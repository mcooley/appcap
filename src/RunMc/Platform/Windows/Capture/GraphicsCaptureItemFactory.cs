using System.Runtime.InteropServices;
using WinRT.Interop;
using Windows.Graphics.Capture;
using WinRT;

namespace RunMc;

internal static class GraphicsCaptureItemFactory
{
    private const string GraphicsCaptureItemRuntimeClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";
    private static readonly Guid GraphicsCaptureItemInterfaceId = Guid.Parse("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid GraphicsCaptureItemInteropInterfaceId = Guid.Parse("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    public static GraphicsCaptureItem CreateForWindow(nint windowHandle)
    {
        using ObjectReference<GraphicsCaptureItemInteropVftbl> factory = ActivationFactory.Get(GraphicsCaptureItemRuntimeClassName)
            .As<GraphicsCaptureItemInteropVftbl>(GraphicsCaptureItemInteropInterfaceId);
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
        ObjectReference<GraphicsCaptureItemInteropVftbl> factory,
        nint windowHandle,
        out nint itemPointer)
    {
        Guid itemInterfaceId = GraphicsCaptureItemInterfaceId;
        nint resultPointer = 0;
        int result = factory.Vftbl.CreateForWindow(factory.ThisPtr, windowHandle, &itemInterfaceId, &resultPointer);
        itemPointer = resultPointer;
        return result;
    }

#pragma warning disable CS0649
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    private unsafe struct GraphicsCaptureItemInteropVftbl
    {
        public IUnknownVftbl IUnknownVftbl;

        public delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int> CreateForWindow;

        public delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int> CreateForMonitor;
    }
#pragma warning restore CS0649
}
