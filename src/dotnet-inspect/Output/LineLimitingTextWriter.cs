using System.Text;

namespace DotnetInspector.Output;

/// <summary>
/// A TextWriter that limits output to a specified number of lines, like <c>head -n</c>.
/// After the limit is reached, all further writes are silently discarded.
/// </summary>
internal sealed class LineLimitingTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly int _maxLines;
    private int _lineCount;
    private bool _limitReached;

    public LineLimitingTextWriter(TextWriter inner, int maxLines)
    {
        _inner = inner;
        _maxLines = maxLines;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        if (_limitReached) return;

        _inner.Write(value);

        if (value == '\n')
        {
            _lineCount++;
            if (_lineCount >= _maxLines)
                _limitReached = true;
        }
    }

    public override void Write(string? value)
    {
        if (_limitReached || value is null) return;

        foreach (var ch in value)
        {
            if (_limitReached) return;
            _inner.Write(ch);
            if (ch == '\n')
            {
                _lineCount++;
                if (_lineCount >= _maxLines)
                    return;
            }
        }
    }

    public override void WriteLine(string? value)
    {
        if (_limitReached) return;

        if (value is not null)
        {
            // Write the string content (may contain embedded newlines)
            foreach (var ch in value)
            {
                if (_limitReached) return;
                _inner.Write(ch);
                if (ch == '\n')
                {
                    _lineCount++;
                    if (_lineCount >= _maxLines)
                        return;
                }
            }
        }

        if (_limitReached) return;

        _inner.WriteLine();
        _lineCount++;
        if (_lineCount >= _maxLines)
            _limitReached = true;
    }

    public override void WriteLine()
    {
        if (_limitReached) return;

        _inner.WriteLine();
        _lineCount++;
        if (_lineCount >= _maxLines)
            _limitReached = true;
    }

    public override void Flush() => _inner.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Flush();
        base.Dispose(disposing);
    }
}
