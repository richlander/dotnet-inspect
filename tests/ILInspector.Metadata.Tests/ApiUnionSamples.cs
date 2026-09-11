namespace ILInspector.Metadata.Tests;

public union NativeMetadataUnion(MetadataUnionCat, MetadataUnionDog);

public union NativeGenericMetadataUnion<T>(T, string);

[ApiUnionSamples.Union]
public sealed class OtherUnionAttributeSample;

public static class ApiUnionSamples
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class UnionAttribute : Attribute;
}
