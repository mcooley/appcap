namespace RunMc.E2ETests;

public sealed class FocusCommandE2ETests : E2ETestBase
{
    [E2EFact]
    public void FocusFindsOrLaunchesTestApp()
    {
        Context.Run().AssertSuccess();
    }
}