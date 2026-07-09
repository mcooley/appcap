namespace AppCap;

public static class HelpText
{
    public static string For(HelpTopic topic) => topic switch
    {
        HelpTopic.Click => "Usage: appcap click -x <pixels> -y <pixels> [--target <target>]",
        HelpTopic.Hover => "Usage: appcap hover -x <pixels> -y <pixels> [--target <target>]",
        HelpTopic.Type => "Usage: appcap type <text-and-keys> [--target <target>]\nBracketed keys use WebDriver/Playwright-style key names, for example [Escape], [Enter], [Shift+F2], [Control+A].",
        HelpTopic.Resize => "Usage: appcap resize --width|-w <pixels> --height|-h <pixels> [--target <target>]",
        HelpTopic.Screenshot => "Usage: appcap screenshot --output <path.png> [--exclude-cursor] [--caption <text>] [--target <target>]",
        HelpTopic.Record => "Usage: appcap record start --output <path.mp4> [--time-limit <minutes>] [--exclude-cursor] [--target <target>]\n       appcap record caption <text> [--target <target>]\n       appcap record stop [--target <target>]\n       appcap record cancel [--target <target>]\n\nThe cursor is included by default. Use --exclude-cursor to omit it. Captions fade out after 3 seconds and can be added repeatedly while recording. Recordings stop and save after 30 minutes by default. Use --time-limit to set a longer limit; fractional minutes are supported.",
        _ => "Usage: appcap [--target <target>] [click|hover|type|resize|screenshot|record] [--help]",
    };
}