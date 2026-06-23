using RunMc;
using WinRT;

ComWrappersSupport.InitializeComWrappers();

return await CliApplication.RunAsync(
	args,
	WindowsCommandServices.CreatePhaseOneRunner(),
	new SystemConsole()).ConfigureAwait(false);
