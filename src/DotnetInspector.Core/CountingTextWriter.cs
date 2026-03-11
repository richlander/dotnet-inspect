using System.Text;

namespace DotnetInspector.Core;

/// <summary>
/// A TextWriter wrapper that counts characters written to the underlying writer.
/// Used by <see cref="InfoTracker"/> to measure output volume.
/// </summary>
public sealed class CountingTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private long _charCount;

    public CountingTextWriter(TextWriter inner)
    {
        _inner = inner;
    }

    public override Encoding Encoding => _inner.Encoding;

    public long CharCount => _charCount;

    public override void Write(char value)
    {
        _inner.Write(value);
        _charCount++;
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        _inner.Write(value);
        _charCount += value.Length;
    }

    public override void Write(char[] buffer, int index, int count)
    {
        _inner.Write(buffer, index, count);
        _charCount += count;
    }

    public override void WriteLine(string? value)
    {
        _inner.WriteLine(value);
        _charCount += (value?.Length ?? 0) + CoreNewLine.Length;
    }

    public override void WriteLine()
    {
        _inner.WriteLine();
        _charCount += CoreNewLine.Length;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync() => _inner.FlushAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Flush();
        base.Dispose(disposing);
    }
}
