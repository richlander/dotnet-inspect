namespace DotnetInspector.CSharpBodySlicer.Tests;

internal sealed class CSharp14OperatorCorpusFixture
{
    private int _value;

    public void operator +=(int value)
    {
        _value = _value + value;
    }

    public void operator checked +=(long value)
    {
        _value = checked(_value + (int)value);
    }

    public void operator +=(long value)
    {
        _value = _value + (int)value;
    }

    public void operator ++()
    {
        _value = _value + 1;
    }

    public void operator checked ++()
    {
        _value = checked(_value + 1);
    }
}
