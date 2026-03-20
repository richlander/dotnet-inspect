using ShellComplete;

/// <summary>
/// Shell completion using the shared ShellComplete library.
/// </summary>
static class CompletionCommand
{
    public static int Run(string shell) =>
        CompletionScripts.WriteToConsole("dotnet-inspect", shell);
}
