using System.Buffers;

namespace DotnetInspect.Cli.Output;

internal static class Utf8StandardOutput
{
    private static readonly AsyncLocal<
        Action<ReadOnlyMemory<byte>>?> s_redirect = new();

    internal static void Write(
        Action<IBufferWriter<byte>> render)
    {
        ArgumentNullException.ThrowIfNull(render);

        var buffer = new ArrayBufferWriter<byte>();
        render(buffer);

        if (s_redirect.Value is { } redirected)
        {
            redirected(buffer.WrittenMemory);
            return;
        }

        Console.Out.Flush();
        Stream output = Console.OpenStandardOutput();
        output.Write(buffer.WrittenSpan);
        output.Flush();
    }

    internal static IDisposable Redirect(
        Action<ReadOnlyMemory<byte>> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        Action<ReadOnlyMemory<byte>>? previous = s_redirect.Value;
        s_redirect.Value = write;
        return new RedirectScope(previous);
    }

    private sealed class RedirectScope(
        Action<ReadOnlyMemory<byte>>? previous)
        : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            s_redirect.Value = previous;
            _disposed = true;
        }
    }
}
