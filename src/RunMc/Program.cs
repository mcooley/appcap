using RunMc;

return await CliApplication.RunAsync(
	args,
	WindowsCommandServices.CreatePhaseOneRunner(),
	new SystemConsole()).ConfigureAwait(false);
