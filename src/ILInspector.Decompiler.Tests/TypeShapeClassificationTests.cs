using System.Reflection.Metadata;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class TypeShapeClassificationTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;

    // typeof(...) emits TypeReference rows into THIS test assembly, so opening it
    // and classifying those references exercises the cross-assembly resolution path.
    static readonly Type[] CrossAssemblyAnchors =
        [typeof(DateTime), typeof(DayOfWeek), typeof(Action), typeof(IDisposable), typeof(string)];

    static TypeShapeKind ClassifyDefinition(MetadataSource source, string fullTypeName)
    {
        foreach (var handle in source.Reader.TypeDefinitions)
        {
            if (source.Reader.GetFullTypeName(source.Reader.GetTypeDefinition(handle)) == fullTypeName)
                return source.ClassifyType(handle);
        }
        return TypeShapeKind.Unknown;
    }

    static TypeShapeKind ClassifyReference(MetadataSource source, string fullTypeName)
    {
        foreach (var handle in source.Reader.TypeReferences)
        {
            if (source.Reader.GetFullTypeName(source.Reader.GetTypeReference(handle)) == fullTypeName)
                return source.ClassifyType((EntityHandle)handle);
        }
        return TypeShapeKind.Unknown;
    }

    [Theory]
    [InlineData("System.String", TypeShapeKind.Class)]
    [InlineData("System.DateTime", TypeShapeKind.Struct)]
    [InlineData("System.DayOfWeek", TypeShapeKind.Enum)]
    [InlineData("System.IDisposable", TypeShapeKind.Interface)]
    [InlineData("System.Action", TypeShapeKind.Delegate)]
    public void ClassifyType_SameAssembly_ResolvesEveryKind(string fullName, TypeShapeKind expected)
    {
        using var source = MetadataSource.Open(CoreLibPath);
        Assert.Equal(expected, ClassifyDefinition(source, fullName));
    }

    [Theory]
    [InlineData("System.DateTime", TypeShapeKind.Struct)]
    [InlineData("System.DayOfWeek", TypeShapeKind.Enum)]
    [InlineData("System.Action", TypeShapeKind.Delegate)]
    [InlineData("System.IDisposable", TypeShapeKind.Interface)]
    [InlineData("System.String", TypeShapeKind.Class)]
    public void ClassifyType_CrossAssembly_ResolvesCorelibShape(string fullName, TypeShapeKind expected)
    {
        Assert.Equal(5, CrossAssemblyAnchors.Length); // keep the typeof anchors emitted
        // The default sibling resolver cannot find corelib beside the test assembly,
        // so use the trusted-platform resolver (as the other cross-assembly tests do).
        using var source = MetadataSource.Open(
            typeof(TypeShapeClassificationTests).Assembly.Location,
            null,
            TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());
        Assert.Equal(expected, ClassifyReference(source, fullName));
    }
}
