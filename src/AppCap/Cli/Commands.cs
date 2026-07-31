namespace AppCap;

public abstract record AppCapCommand;

public sealed record TargetAttachCommand(TargetApplication? Target, bool Launch) : AppCapCommand;

public sealed record TargetDetachCommand(TargetApplication? Target) : AppCapCommand;

public sealed record TargetListCommand : AppCapCommand;

public sealed record InputDeviceAttachCommand(TargetApplication? Target, InputDeviceType DeviceType) : AppCapCommand;

public sealed record InputDeviceRemoveCommand(TargetApplication? Target, InputDeviceType DeviceType) : AppCapCommand;

public sealed record InputDeviceListCommand(TargetApplication? Target) : AppCapCommand;

public sealed record TapCommand(TargetApplication? Target, int X, int Y, InputDeviceType? DeviceType = null) : AppCapCommand;

public sealed record MouseMoveCommand(TargetApplication? Target, int X, int Y, InputDeviceType? DeviceType = null) : AppCapCommand;

public sealed record MouseClickCommand(TargetApplication? Target, int X, int Y, InputDeviceType? DeviceType = null) : AppCapCommand;

public sealed record TypeCommand(
    TargetApplication? Target,
    string TextAndKeys,
    IReadOnlyList<KeyboardAction> Actions,
    InputDeviceType? DeviceType = null) : AppCapCommand;

public sealed record ResizeCommand(TargetApplication? Target, int Width, int Height) : AppCapCommand;

public sealed record ScreenshotCommand(TargetApplication? Target, string OutputPath, bool ExcludeCursor, string? Caption, CropRectangle? Crop = null) : AppCapCommand;

public sealed record RecordStartCommand(TargetApplication? Target, string OutputPath, TimeSpan TimeLimit, bool ExcludeCursor, CropRectangle? Crop = null) : AppCapCommand;

public sealed record RecordCaptionCommand(TargetApplication? Target, string Caption) : AppCapCommand;

public sealed record RecordStopCommand(TargetApplication? Target) : AppCapCommand;

public sealed record RecordCancelCommand(TargetApplication? Target) : AppCapCommand;

public sealed record RecordStatusCommand(TargetApplication? Target) : AppCapCommand;
