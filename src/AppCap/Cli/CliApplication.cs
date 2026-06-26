namespace AppCap;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        ICommandRunner runner,
        ICommandConsole console,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(console);

        ParseResult parseResult = CommandParser.Parse(args);
        if (!parseResult.Success)
        {
            console.ErrorOutput.WriteLine(parseResult.ErrorMessage);
            return ExitCodes.UsageError;
        }

        if (parseResult.Command is HelpCommand helpCommand)
        {
            console.Output.WriteLine(HelpText.For(helpCommand.Topic));
            return ExitCodes.Success;
        }

        try
        {
            await runner.RunAsync(parseResult.Command, cancellationToken).ConfigureAwait(false);
            return ExitCodes.Success;
        }
        catch (AppCapException exception)
        {
            console.ErrorOutput.WriteLine(exception.Message);
            return exception.ExitCode;
        }
    }
}