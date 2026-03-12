using System.Text;

namespace DotnetInspector.Output;

/// <summary>
/// A TextWriter that buffers all output and emits only the last N lines on dispose, like <c>tail -n</c>.
/// </summary>
internal sealed class TailLineLimitingTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly int _maxLines;
    private readonly StringBuilder _buffer = new();

    public TailLineLimitingTextWriter(TextWriter inner, int maxLines)
    {
        _inner = inner;
        _maxLines = maxLines;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value) => _buffer.Append(value);

    public override void Write(string? value)
    {
        if (value is not null)
            _buffer.Append(value);
    }

    public override void WriteLine(string? value)
    {
        if (value is not null)
            _buffer.Append(value);
        _buffer.Append('\n');
    }

    public override void WriteLine() => _buffer.Append('\n');

    public override void Flush() { }

    private bool _flushed;

    /// <summary>
    /// Writes the last N lines to the inner writer.
    /// </summary>
    public void FlushTail()
    {
        if (_flushed) return;
        _flushed = true;

        var text = _buffer.ToString();
        var lines = text.Split('\n');

        // If the text ends with \n, Split produces a trailing empty element — exclude it
        int lineCount = lines.Length;
        if (lineCount > 0 && lines[^1].Length == 0)
            lineCount--;

        int skip = Math.Max(0, lineCount - _maxLines);
        for (int i = skip; i < lineCount; i++)
        {
            _inner.WriteLine(lines[i]);
        }

        _inner.Flush();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            FlushTail();
        base.Dispose(disposing);
    }
}
