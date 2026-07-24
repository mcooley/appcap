using System.CommandLine;
using System.CommandLine.Invocation;

namespace AppCap;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        TargetCatalog catalog,
        ICommandRunner runner,
        ICommandConsole console,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(console);

        if (CommandParser.CanInvokeWithoutConfiguration(args) && !CommandParser.StartsWithDirective(args))
        {
            return await RunWithoutConfigurationAsync(args, console, cancellationToken).ConfigureAwait(false);
        }

        RootCommand rootCommand = CommandParser.CreateRootCommand(
            catalog,
            async (command, token) =>
            {
                CommandExecutionResult result = await runner.RunAsync(command, token).ConfigureAwait(false);
                if (result.Output.Length > 0)
                {
                    await console.Output.WriteLineAsync(result.Output).ConfigureAwait(false);
                }
            });
        return await InvokeAsync(rootCommand, args, console, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> RunWithoutConfigurationAsync(
        string[] args,
        ICommandConsole console,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(console);

        RootCommand rootCommand = CommandParser.CreateRootCommandForConfigurationlessInvocation();
        return await InvokeAsync(rootCommand, args, console, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> InvokeAsync(
        RootCommand rootCommand,
        IReadOnlyList<string> args,
        ICommandConsole console,
        CancellationToken cancellationToken)
    {
        System.CommandLine.ParseResult parseResult = rootCommand.Parse(args);
        InvocationConfiguration configuration = new()
        {
            EnableDefaultExceptionHandler = false,
            Output = console.Output,
            Error = console.ErrorOutput,
        };

        try
        {
            int exitCode = await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
            return parseResult.Errors.Count > 0 ? ExitCodes.UsageError : exitCode;
        }
        catch (AppCapException exception)
        {
            console.ErrorOutput.WriteLine(exception.Message);
            return exception.ExitCode;
        }
    }
}