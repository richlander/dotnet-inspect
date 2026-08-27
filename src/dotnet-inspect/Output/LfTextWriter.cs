using System.Text;

namespace DotnetInspector.Output;

internal sealed class LfTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private bool _pendingCarriageReturn;

    public LfTextWriter(TextWriter inner)
    {
        _inner = inner;
        NewLine = "\n";
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        if (_pendingCarriageReturn)
        {
            _inner.Write('\n');
            _pendingCarriageReturn = false;
            if (value == '\n')
                return;
        }

        if (value == '\r')
            _pendingCarriageReturn = true;
        else
            _inner.Write(value);
    }

    public override void Write(string? value)
    {
        if (value is null)
            return;

        foreach (char character in value)
            Write(character);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - index < count)
            throw new ArgumentException("The buffer range is invalid.");

        for (int i = index; i < index + count; i++)
            Write(buffer[i]);
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    public override void WriteLine() => Write('\n');

    public override void Flush()
    {
        if (_pendingCarriageReturn)
        {
            _inner.Write('\n');
            _pendingCarriageReturn = false;
        }

        _inner.Flush();
    }

    public override Task FlushAsync()
    {
        Flush();
        return Task.CompletedTask;
    }
}
