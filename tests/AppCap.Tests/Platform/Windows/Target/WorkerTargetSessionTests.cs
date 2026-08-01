using AppCap.Protocol;
using AppCap.Windows;

namespace AppCap.Tests;

public sealed class WorkerTargetSessionTests
{
    private static readonly TargetApplication Application = new() { Name = "target", Id = "Package_family!App" };
    private static readonly TargetWindow Window = new(Application, 123);

    [Fact]
    public async Task CurrentInputDevicesAreAttachedWithTargetSession()
    {
        using WorkerTargetSession session = CreateSession();

        IReadOnlyList<InputDeviceStatus> devices = await session.ListInputDevicesAsync(CancellationToken.None);

        Assert.Collection(
            devices,
            device => Assert.Equal(new InputDeviceStatus(InputDeviceType.Touch, true), device),
            device => Assert.Equal(new InputDeviceStatus(InputDeviceType.Keyboard, true), device),
            device => Assert.Equal(new InputDeviceStatus(InputDeviceType.Mouse, true), device));
    }

    [Fact]
    public async Task InputCommandsDoNotReattachRemovedDevices()
    {
        using WorkerTargetSession session = CreateSession();
        await session.RemoveInputDeviceAsync(InputDeviceType.Touch, CancellationToken.None);
        await session.RemoveInputDeviceAsync(InputDeviceType.Mouse, CancellationToken.None);
        await session.RemoveInputDeviceAsync(InputDeviceType.Keyboard, CancellationToken.None);

        await AssertNotAttachedAsync(() => session.TapAsync(10, 20, deviceType: null, CancellationToken.None));
        await AssertNotAttachedAsync(() => session.MoveMouseAsync(10, 20, deviceType: null, CancellationToken.None));
        await AssertNotAttachedAsync(() => session.ClickMouseAsync(10, 20, deviceType: null, CancellationToken.None));
        await AssertNotAttachedAsync(() => session.TypeAsync("test", deviceType: null, CancellationToken.None));

        Assert.All(await session.ListInputDevicesAsync(CancellationToken.None), device => Assert.False(device.Attached));
    }

    private static async Task AssertNotAttachedAsync(Func<Task> command)
    {
        ProtocolErrorException exception = await Assert.ThrowsAsync<ProtocolErrorException>(command);
        Assert.Equal(JsonRpcErrorCodes.InputDeviceNotAttached, exception.ErrorCode);
    }

    private static WorkerTargetSession CreateSession() =>
        new(
            Application,
            new TestTargetResolver(),
            new TestWindowController(),
            new TestInputInjector(),
            new TestKeyboardInputInjector());

    private sealed class TestTargetResolver : ITargetResolver
    {
        public Task<TargetWindow> ResolveAsync(TargetApplication target, CancellationToken cancellationToken) =>
            Task.FromResult(Window);

        public Task<TargetWindow> ResolveRunningAsync(TargetApplication target, CancellationToken cancellationToken) =>
            Task.FromResult(Window);
    }

    private sealed class TestWindowController : IWindowController
    {
        public Task BringToForegroundAsync(TargetWindow window, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WindowBounds> GetBoundsAsync(TargetWindow window, CancellationToken cancellationToken) =>
            Task.FromResult(new WindowBounds(0, 0, 640, 480));

        public Task ResizeAsync(TargetWindow window, int width, int height, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestInputInjector : IInputInjector
    {
        public Task TapAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MoveMouseAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClickMouseAsync(TargetWindow window, int screenX, int screenY, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestKeyboardInputInjector : IKeyboardInputInjector
    {
        public Task TypeAsync(TargetWindow window, IReadOnlyList<KeyboardAction> actions, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
