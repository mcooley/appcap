namespace AppCap;

public sealed class AppCapException : Exception
{
    public AppCapException(string message, int exitCode = ExitCodes.OperationalError)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public AppCapException(string message, Exception innerException, int exitCode = ExitCodes.OperationalError)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}