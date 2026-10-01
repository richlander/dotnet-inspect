namespace ILInspector.Decompiler.Tests;

public static class IteratorUsingSamples
{
    public static IEnumerable<(string, string)> YieldZipWithEmpty(
        IEnumerable<string> first,
        IEnumerable<string> second)
    {
        using IEnumerator<string> enum1 = first.GetEnumerator();
        using IEnumerator<string> enum2 = second.GetEnumerator();
        bool has1 = false;
        bool has2 = false;
        while ((has1 = enum1.MoveNext()) | (has2 = enum2.MoveNext()))
            yield return (has1 ? enum1.Current : "", has2 ? enum2.Current : "");
    }
}
