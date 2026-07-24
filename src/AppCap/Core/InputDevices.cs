using AppCap.Protocol;

namespace AppCap;

public readonly record struct InputDeviceType
{
    public static readonly InputDeviceType Touch = new("touch");
    public static readonly InputDeviceType Keyboard = new("keyboard");
    public static readonly InputDeviceType Mouse = new("mouse");

    public static IReadOnlyList<InputDeviceType> KnownTypes { get; } = [Touch, Keyboard, Mouse];

    public InputDeviceType(string value)
    {
        if (!TryNormalize(value, out string normalized))
        {
            throw new ArgumentException("Input device identifiers must use lowercase letters, digits, or '-'.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; } = string.Empty;

    public override string ToString() => Value;

    public static bool TryParse(string? value, out InputDeviceType deviceType)
    {
        if (!TryNormalize(value, out string normalized))
        {
            deviceType = default;
            return false;
        }

        deviceType = new InputDeviceType(normalized);
        return true;
    }

    public static InputDeviceType Parse(string value) =>
        TryParse(value, out InputDeviceType deviceType)
            ? deviceType
            : throw new ArgumentException("Invalid input device identifier.", nameof(value));

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim().ToLowerInvariant();
        if (!IsValidIdentifier(trimmed))
        {
            return false;
        }

        normalized = trimmed;
        return true;
    }

    private static bool IsValidIdentifier(string value)
    {
        if (value.Length is 0 || !IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
}

public sealed record InputDeviceStatus(InputDeviceType DeviceType, bool Attached);

internal sealed class InputDeviceAttachmentRegistry
{
    private readonly string targetName;
    private readonly InputDeviceType[] supportedDevices;
    private readonly HashSet<InputDeviceType> supportedDeviceSet;
    private readonly HashSet<InputDeviceType> attachedDevices = [];
    private readonly object gate = new();

    public InputDeviceAttachmentRegistry(string targetName, IReadOnlyList<InputDeviceType> supportedDevices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(supportedDevices);
        if (supportedDevices.Count is 0)
        {
            throw new ArgumentException("At least one supported input device is required.", nameof(supportedDevices));
        }

        this.targetName = targetName;
        this.supportedDevices = supportedDevices.ToArray();
        supportedDeviceSet = new HashSet<InputDeviceType>(this.supportedDevices);
    }

    public IReadOnlyList<InputDeviceType> SupportedDevices => supportedDevices;

    public bool HasAttachedDevices
    {
        get
        {
            lock (gate)
            {
                return attachedDevices.Count > 0;
            }
        }
    }

    public void Attach(InputDeviceType deviceType)
    {
        EnsureSupported(deviceType);

        lock (gate)
        {
            if (!attachedDevices.Add(deviceType))
            {
                throw new ProtocolErrorException(
                    JsonRpcErrorCodes.InputDeviceAlreadyAttached,
                    $"Input device '{deviceType}' is already attached for target '{targetName}'.");
            }
        }
    }

    public void Remove(InputDeviceType deviceType)
    {
        EnsureSupported(deviceType);

        lock (gate)
        {
            if (!attachedDevices.Remove(deviceType))
            {
                throw CreateNotAttachedError(deviceType);
            }
        }
    }

    public IReadOnlyList<InputDeviceStatus> List()
    {
        lock (gate)
        {
            return supportedDevices
                .Select(deviceType => new InputDeviceStatus(deviceType, attachedDevices.Contains(deviceType)))
                .ToArray();
        }
    }

    public InputDeviceType Select(InputDeviceType requiredDeviceType, InputDeviceType? requestedDeviceType, string commandName)
    {
        EnsureSupported(requiredDeviceType);

        lock (gate)
        {
            if (requestedDeviceType is InputDeviceType requestedDevice)
            {
                EnsureSupported(requestedDevice);
                if (requestedDevice != requiredDeviceType)
                {
                    throw new ProtocolErrorException(
                        JsonRpcErrorCodes.InvalidInputDeviceSelection,
                        $"The {commandName} command requires a '{requiredDeviceType}' input device, but '{requestedDevice}' was selected.");
                }

                if (!attachedDevices.Contains(requestedDevice))
                {
                    throw CreateNotAttachedError(requestedDevice);
                }

                return requestedDevice;
            }

            if (!attachedDevices.Contains(requiredDeviceType))
            {
                throw new ProtocolErrorException(
                    JsonRpcErrorCodes.InputDeviceNotAttached,
                    $"No '{requiredDeviceType}' input device is attached for target '{targetName}'. Run 'appcap --target {targetName} inputdevice attach {requiredDeviceType}' first.");
            }

            return requiredDeviceType;
        }
    }

    private void EnsureSupported(InputDeviceType deviceType)
    {
        if (supportedDeviceSet.Contains(deviceType))
        {
            return;
        }

        throw new ProtocolErrorException(
            JsonRpcErrorCodes.UnsupportedInputDevice,
            $"Input device '{deviceType}' is not supported for target '{targetName}'. Supported devices: {string.Join(", ", supportedDevices)}.");
    }

    private ProtocolErrorException CreateNotAttachedError(InputDeviceType deviceType) =>
        new(
            JsonRpcErrorCodes.InputDeviceNotAttached,
            $"Input device '{deviceType}' is not attached for target '{targetName}'. Run 'appcap --target {targetName} inputdevice attach {deviceType}' first.");
}
