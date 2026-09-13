namespace ILInspector.Decompiler.Tests;

public sealed class BooleanSlotIdentitySample
{
    public Type CreatedType { get; set; } = typeof(object);
    public bool DefaultCreatorNonPublic { get; set; }

    public void Initialize()
    {
        DefaultCreatorNonPublic = CreatedType.IsValueType
            ? false
            : CreatedType.GetConstructor(Type.EmptyTypes) is null;
    }
}
