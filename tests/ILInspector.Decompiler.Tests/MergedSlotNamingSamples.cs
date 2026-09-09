namespace ILInspector.Decompiler.Tests;

// Issue #1767: a value produced at a stack slot (the object-merged `cond ? null
// : value` ternary) and consumed at a control-flow merge where the slot is typed
// `string` (a field store) must share ONE local name. Keying slot names on
// (slot, type) split them into `object S_1` (store) and `string S_1_1` (load),
// so the consumer read an unassigned local (CS0165) and the value was dropped.
public static class MergedSlotStrU
{
    public static bool IsNullOrEmpty(string s) => s == null || s.Length == 0;
}

public class MergedSlotProbe
{
    private string? _fmt;

    public string? MergedFormat
    {
        get => _fmt;
        set => _fmt = MergedSlotStrU.IsNullOrEmpty(value!) ? null : value;
    }
}
