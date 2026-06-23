namespace RunMc;

public static class HelpText
{
    public static string For(HelpTopic topic) => topic switch
    {
        HelpTopic.Click => "Usage: runmc click -x <pixels> -y <pixels> [--target <target>]",
        HelpTopic.Resize => "Usage: runmc resize --width|-w <pixels> --height|-h <pixels> [--target <target>]",
        HelpTopic.Screenshot => "Usage: runmc screenshot --output <path.png> [--target <target>]",
        _ => "Usage: runmc [--target <target>] [click|resize|screenshot] [--help]",
    };
}