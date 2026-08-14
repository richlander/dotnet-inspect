namespace ILInspector.Decompiler.Tests;

// Issue #1841 (#1202 residual): foreach over a struct array element takes the
// element address (ldelema); the collection must render as `arr[i]`, not the
// invalid `ref arr[i]`. ImmutableArray<int> is a struct, so `foreach (x in
// Rows[i])` enumerates by address.
public class ForeachElementProbe
{
    public System.Collections.Immutable.ImmutableArray<int>[] Rows = new System.Collections.Immutable.ImmutableArray<int>[1];
    public int Sum(int i)
    {
        int total = 0;
        foreach (int x in Rows[i])
        {
            total += x;
        }
        return total;
    }
}
