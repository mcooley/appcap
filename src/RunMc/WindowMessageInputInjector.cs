namespace RunMc;

public sealed class WindowMessageInputInjector : IInputInjector
{
    public Task ClickAsync(MinecraftWindow window, int x, int y, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        nint coordinates = MakeLParam(x, y);
        _ = WindowsNative.SendMessageW(window.Handle, WindowsNative.WmMouseMove, 0, coordinates);
        _ = WindowsNative.SendMessageW(window.Handle, WindowsNative.WmLButtonDown, (nint)WindowsNative.MkLButton, coordinates);
        _ = WindowsNative.SendMessageW(window.Handle, WindowsNative.WmLButtonUp, 0, coordinates);

        return Task.CompletedTask;
    }

    private static nint MakeLParam(int x, int y) => (nint)((y << 16) | (x & 0xFFFF));
}