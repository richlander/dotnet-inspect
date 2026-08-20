using System.Text;

namespace DotnetInspector.Output;

internal sealed class LfTextWriter(TextWriter inner) : TextWriter
{
    private bool _pendingCarriageReturn;

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        if (_pendingCarriageReturn)
        {
            inner.Write('\n');
            _pendingCarriageReturn = false;
            if (value == '\n')
                return;
        }

        if (value == '\r')
            _pendingCarriageReturn = true;
        else
            inner.Write(value);
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
            inner.Write('\n');
            _pendingCarriageReturn = false;
        }

        inner.Flush();
    }

    public override Task FlushAsync()
    {
        Flush();
        return Task.CompletedTask;
    }
}
