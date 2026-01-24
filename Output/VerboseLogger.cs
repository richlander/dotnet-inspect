namespace DotnetInspector.Output;

/// <summary>
/// Logger that writes progress messages to stderr when verbose mode is enabled.
/// </summary>
public class VerboseLogger(bool enabled)
{
    public bool Enabled { get; } = enabled;

    public void Log(string message)
    {
        if (Enabled)
        {
            Console.Error.WriteLine(message);
        }
    }

    public void Log(string format, params object[] args)
    {
        if (Enabled)
        {
            Console.Error.WriteLine(format, args);
        }
    }
}
