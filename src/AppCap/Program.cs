using AppCap;
using AppCap.Windows;
using Windows.Win32;
using Windows.Win32.UI.HiDpi;
using WinRT;

_ = PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
ComWrappersSupport.InitializeComWrappers();

if (WorkerHost.IsWorkerInvocation(args))
{
	return await WorkerHost.RunAsync(args, CancellationToken.None).ConfigureAwait(false);
}

SystemConsole console = new();

if (CommandParser.IsVerbLessInvocation(args))
{
	return CliApplication.WriteHelp(console, HelpTopic.Root);
}

TargetCatalog catalog;
try
{
	catalog = ConfigLoader.Load(AppContext.BaseDirectory);
}
catch (AppCapException exception)
{
	console.ErrorOutput.WriteLine(exception.Message);
	return exception.ExitCode;
}

return await CliApplication.RunAsync(
	args,
	catalog,
	CommandServices.CreateRunner(),
	console).ConfigureAwait(false);
