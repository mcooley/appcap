namespace RunMc;

public sealed class RunMcException : Exception
{
    public RunMcException(string message, int exitCode = ExitCodes.OperationalError)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public RunMcException(string message, Exception innerException, int exitCode = ExitCodes.OperationalError)
        : base(message, innerException)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}