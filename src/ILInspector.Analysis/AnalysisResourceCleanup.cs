using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis;

internal static class AnalysisResourceCleanup
{
    internal static void DisposeAfterFailure(
        ref PEReader? peReader,
        ref Stream? stream,
        Exception primaryFailure)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        DisposeWithoutReplacingOutcome(peReader);
        peReader = null;
        DisposeWithoutReplacingOutcome(stream);
        stream = null;
    }

    internal static void DisposeWithoutReplacingOutcome(
        ref PEReader? peReader,
        ref Stream? stream)
    {
        DisposeWithoutReplacingOutcome(peReader);
        peReader = null;
        DisposeWithoutReplacingOutcome(stream);
        stream = null;
    }

    static void DisposeWithoutReplacingOutcome(
        IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch
        {
        }
    }
}
