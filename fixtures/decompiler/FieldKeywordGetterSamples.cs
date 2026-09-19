namespace ILInspector.Decompiler.Fixtures.FieldKeyword;

public static class @field
{
    public static FieldValue? Keep(FieldValue? value) => value;
    public static int Keep(int value) => value;
}

public static class @field<T>
{
    public static int Keep(int value) => value;
}

public sealed class FieldValue
{
    public FieldValue? Keep(FieldValue? value) => new();
}

#pragma warning disable CS9258 // Intentionally combine a type named field with accessor backing storage.
public class FieldKeywordGetterSamples
{
    public FieldValue? Value => @field.Keep(field);
    public int Count => @field.Keep(field);
    public static int StaticCount => @field.Keep(field);
}

public class GenericFieldKeywordGetterSamples<T>
{
    public int Count => @field<T>.Keep(field);
}
#pragma warning restore CS9258
