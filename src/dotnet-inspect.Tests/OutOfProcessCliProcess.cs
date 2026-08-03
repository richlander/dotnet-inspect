using System.ComponentModel;
using System.Diagnostics;

namespace DotnetInspector.Tests;

internal static class OutOfProcessCliProcess
{
    public static bool KillAndWaitForExit(Process process, TimeSpan timeout)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            return process.WaitForExit(timeout);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timed wait and the kill.
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
