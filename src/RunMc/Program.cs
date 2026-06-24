using RunMc;
using RunMc.Windows;
using Windows.Win32;
using Windows.Win32.UI.HiDpi;
using WinRT;

_ = PInvoke.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
ComWrappersSupport.InitializeComWrappers();

return await CliApplication.RunAsync(
	args,
	CommandServices.CreateRunner(),
	new SystemConsole()).ConfigureAwait(false);
