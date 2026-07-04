namespace ILInspector.MetadataPrimitives;

public enum TypeAnchorFormat
{
    FullName,
}

public sealed record TypeAnchor(string TypeFullName)
{
    public string Format(TypeAnchorFormat format)
        => TypeFullName;
}
