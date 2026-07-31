using AppCap.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace AppCap;

public static class McpServerApplication
{
    public static async Task RunAsync(TargetCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(catalog);
        builder.Services.AddSingleton<ICommandRunner>(_ => CommandServices.CreateRunner(catalog));
        builder.Services.AddSingleton<McpCommandExecutor>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<McpTools>(McpToolSerialization.SerializerOptions);

        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class McpCommandExecutor : IDisposable
{
    private readonly ICommandRunner runner;
    private readonly SemaphoreSlim executionLock = new(1, 1);

    public McpCommandExecutor(ICommandRunner runner) => this.runner = runner;

    public async Task<string> RunAsync(AppCapCommand command, CancellationToken cancellationToken)
    {
        await executionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CommandExecutionResult result = await runner.RunAsync(command, cancellationToken).ConfigureAwait(false);
            return result.Output.Length is 0 ? "OK" : result.Output;
        }
        finally
        {
            executionLock.Release();
        }
    }

    public void Dispose() => executionLock.Dispose();
}