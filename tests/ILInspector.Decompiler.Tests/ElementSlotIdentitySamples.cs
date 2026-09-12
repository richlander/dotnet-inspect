namespace ILInspector.Decompiler.Tests;

public enum ElementSlotKind { Value, Ref, Out }

public static class ElementSlotIdentitySamples
{
    // The constant enum conditional retained by ReadParameterRefKinds in
    // dotnet-inspect.any 0.14.0. A non-Boolean pair keeps current csc from
    // replacing the conditional with a comparison's 0/1 result.
    public static void StoreKind(ElementSlotKind[] kinds, int index, int[] input)
        => kinds[index] = input[index] == 1 ? ElementSlotKind.Ref : ElementSlotKind.Out;
}
