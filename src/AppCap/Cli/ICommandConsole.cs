using System.IO;

namespace AppCap;

public interface ICommandConsole
{
    TextWriter Output { get; }

    TextWriter ErrorOutput { get; }
}