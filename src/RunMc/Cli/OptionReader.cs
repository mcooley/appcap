namespace RunMc;

public sealed class OptionReader
{
    private readonly IReadOnlyList<string> args;
    private int index;

    public OptionReader(IReadOnlyList<string> args)
    {
        this.args = args;
    }

    public bool TryReadOption(out string? name, out string? value)
    {
        name = null;
        value = null;

        if (index >= args.Count)
        {
            return false;
        }

        name = args[index++];
        if (index >= args.Count)
        {
            return true;
        }

        value = args[index++];
        return true;
    }
}