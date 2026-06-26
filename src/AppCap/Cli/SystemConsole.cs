namespace AppCap;

public sealed class SystemConsole : ICommandConsole
{
    public TextWriter Output => Console.Out;

    public TextWriter ErrorOutput => Console.Error;
}