using System.ComponentModel;
using System.Diagnostics;

namespace DotnetInspector.Tests;

internal static class OutOfProcessCliProcess
{
    public static void KillAndWaitForExit(Process process, TimeSpan timeout)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(timeout);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timed wait and the kill.
        }
        catch (Win32Exception)
        {
        }
        catch (AggregateException)
        {
            // Some descendants could not be terminated. The caller preserves
            // their cache because descendant exit cannot be confirmed here.
        }
    }
}
