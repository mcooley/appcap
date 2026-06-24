using global::Windows.Win32.System.Com;

namespace RunMc.Windows;

internal sealed unsafe class ComPtr<T> : IDisposable
    where T : unmanaged
{
    private T* pointer;

    public ComPtr(T* pointer)
    {
        this.pointer = pointer;
    }

    public T* Get() => pointer;

    public void Dispose()
    {
        if (pointer is not null)
        {
            ((IUnknown*)pointer)->Release();
            pointer = null;
        }
    }
}