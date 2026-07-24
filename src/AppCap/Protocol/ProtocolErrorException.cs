namespace AppCap.Protocol;

internal sealed class ProtocolErrorException : Exception
{
    public ProtocolErrorException(int errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}
