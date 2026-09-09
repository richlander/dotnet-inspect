using ILInspector.Decompiler.Fixtures.ForwardedFieldTarget;

namespace ILInspector.Decompiler.Fixtures.ForwardedFieldCaller;

public static class FieldCaller
{
    public static object Read()
    {
        unsafe
        {
            return Holder.Self;
        }
    }
}
