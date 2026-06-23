using System.IO;

namespace RunMc;

public interface ICommandConsole
{
    TextWriter Output { get; }

    TextWriter ErrorOutput { get; }
}