namespace RunMc;

public static class HelpText
{
    public static string For(HelpTopic topic) => topic switch
    {
        HelpTopic.Click => "Usage: runmc click -x <pixels> -y <pixels> [--target <target>]",
        HelpTopic.Hover => "Usage: runmc hover -x <pixels> -y <pixels> [--target <target>]",
        HelpTopic.Type => "Usage: runmc type <text-and-keys> [--target <target>]\nBracketed keys use WebDriver/Playwright-style key names, for example [Escape], [Enter], [Shift+F2], [Control+A].",
        HelpTopic.Resize => "Usage: runmc resize --width|-w <pixels> --height|-h <pixels> [--target <target>]",
        HelpTopic.Screenshot => "Usage: runmc screenshot --output <path.png> [--include-cursor] [--caption <text>] [--target <target>]",
        HelpTopic.Record => "Usage: runmc record start --output <path.mp4> [--target <target>]\n       runmc record stop [--target <target>]",
        _ => "Usage: runmc [--target <target>] [click|hover|type|resize|screenshot|record] [--help]",
    };
}