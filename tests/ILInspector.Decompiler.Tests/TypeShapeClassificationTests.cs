using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class TypeShapeClassificationTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;

    // A TypeSpec-parented constructor (newobj Nullable<int>::.ctor): the local
    // signature carries ELEMENT_TYPE_VALUETYPE, exercising the hint fallback when
    // the definition cannot be resolved cross-assembly.
    static class NullableCtorFixture
    {
        public static int? Make() => new int?(1);
    }

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

    [Fact]
    public void ClassifyConstructedType_TypeSpecValueType_UsesLocalHintWithoutResolver()
    {
        // GPT-5.5 review probe: new int?(1) constructs Nullable<int> via a TypeSpec-parented
        // ctor token. With no platform resolver the corelib definition cannot be located, so the
        // classifier must fall back to the local ELEMENT_TYPE_VALUETYPE hint rather than Unknown.
        string path = typeof(TypeShapeClassificationTests).Assembly.Location;
        using var source = MetadataSource.Open(path, null, TestAssemblyReferenceResolvers.None);
        using var pe = new PEReader(File.OpenRead(path));
        var reader = pe.GetMetadataReader();

        int token = FindNewobjToken(reader, pe, nameof(NullableCtorFixture), nameof(NullableCtorFixture.Make));
        Assert.Equal(TypeShapeKind.Struct, source.ClassifyConstructedType(token));
    }

    static int FindNewobjToken(MetadataReader reader, PEReader pe, string typeName, string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(typeDef.Name) != typeName)
                continue;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName || method.RelativeVirtualAddress == 0)
                    continue;
                var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                var bytes = il.ReadBytes(il.Length);
                for (int i = 0; i + 4 < bytes.Length; i++)
                {
                    if (bytes[i] != 0x73) // newobj
                        continue;
                    int candidate = BitConverter.ToInt32(bytes, i + 1);
                    if (MetadataTokens.EntityHandle(candidate).Kind is HandleKind.MemberReference or HandleKind.MethodDefinition)
                        return candidate;
                }
            }
        }
        throw new InvalidOperationException($"newobj token not found in {typeName}.{methodName}.");
    }
}
