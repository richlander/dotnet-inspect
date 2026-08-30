using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

internal static class OwnedResourceCleanup
{
    internal static void DisposeAfterFailure(
        IDisposable? resource,
        Exception primaryFailure)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        DisposeWithoutReplacingOutcome(resource);
    }

    internal static void DisposeWithoutReplacingOutcome(
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

    internal static void DisposeAfterFailure(
        ref PEReader? peReader,
        Exception primaryFailure)
    {
        DisposeAfterFailure(peReader, primaryFailure);
        peReader = null;
    }

    internal static void DisposeAfterFailure(
        ref PEReader? peReader,
        ref Stream? stream,
        Exception primaryFailure)
    {
        DisposeAfterFailure(ref peReader, primaryFailure);
        DisposeAfterFailure(stream, primaryFailure);
        stream = null;
    }

    internal static void DisposeWithoutReplacingOutcome<TStream>(
        ref PEReader? peReader,
        ref TStream? stream)
        where TStream : Stream
    {
        DisposeWithoutReplacingOutcome(peReader);
        peReader = null;
        DisposeWithoutReplacingOutcome(stream);
        stream = null;
    }
}
