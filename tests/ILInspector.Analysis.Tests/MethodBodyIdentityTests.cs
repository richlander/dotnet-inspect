using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace ILInspector.Analysis.Tests;

public sealed class MethodBodyIdentityTests
{
    [Fact]
    public void MethodBodyIdentity_ErasesModuleTokenAndGenericParameterNames()
    {
        MethodIdentity before = Method(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            0x06000001,
            TypeRef.MethodGenericParameter(0, "TValue"),
            "TValue");
        MethodIdentity after = Method(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            0x06000042,
            TypeRef.MethodGenericParameter(0, "T"),
            "T");

        Assert.True(MethodBodyIdentityFactory.TryCreate(
            before,
            out var beforeIdentity));
        Assert.True(MethodBodyIdentityFactory.TryCreate(
            after,
            out var afterIdentity));

        Assert.Equal(beforeIdentity, afterIdentity);
        Assert.Equal(
            beforeIdentity!.CanonicalIdentity,
            afterIdentity!.CanonicalIdentity);
    }

    [Fact]
    public void MethodBodyIdentity_PreservesGenericParameterOwner()
    {
        MethodIdentity typeParameter = Method(
            Guid.Empty,
            0x06000001,
            TypeRef.GenericParameter(0, "T"),
            "U");
        MethodIdentity methodParameter = Method(
            Guid.Empty,
            0x06000002,
            TypeRef.MethodGenericParameter(0, "U"),
            "U");

        Assert.True(MethodBodyIdentityFactory.TryCreate(
            typeParameter,
            out var typeIdentity));
        Assert.True(MethodBodyIdentityFactory.TryCreate(
            methodParameter,
            out var methodIdentity));

        Assert.NotEqual(typeIdentity, methodIdentity);
    }

    [Fact]
    public void MethodBodyIdentity_PreservesPhysicalReturnType()
    {
        MethodIdentity returnsString = Method(
            Guid.Empty,
            0x06000001,
            TypeRef.CoreLib("System", "Int32"),
            "T");
        MethodIdentity returnsInt32 = returnsString with
        {
            ReturnType = TypeRef.CoreLib("System", "Int32"),
            MetadataToken = 0x06000002,
        };

        Assert.True(MethodBodyIdentityFactory.TryCreate(
            returnsString,
            out var stringIdentity));
        Assert.True(MethodBodyIdentityFactory.TryCreate(
            returnsInt32,
            out var int32Identity));

        Assert.NotEqual(stringIdentity, int32Identity);
    }

    [Fact]
    public void MethodBodyIdentity_PreservesCustomModifierShape()
    {
        TypeRef modifier = TypeRef.Definition(
            "System.Runtime",
            "System.Runtime.CompilerServices",
            "IsVolatile");
        TypeRef unmodified = TypeRef.CoreLib("System", "Int32");
        MethodIdentity required = Method(
            Guid.Empty,
            0x06000001,
            TypeRef.UnsupportedModified(
                modifier,
                unmodified,
                isRequired: true),
            "T");
        MethodIdentity optional = Method(
            Guid.Empty,
            0x06000002,
            TypeRef.UnsupportedModified(
                modifier,
                unmodified,
                isRequired: false),
            "T");

        Assert.True(MethodBodyIdentityFactory.TryCreate(
            required,
            out var requiredIdentity));
        Assert.True(MethodBodyIdentityFactory.TryCreate(
            optional,
            out var optionalIdentity));

        Assert.NotEqual(requiredIdentity, optionalIdentity);
    }

    [Fact]
    public void StructuredAndMetadataFactoriesIssueTheSameIdentity()
    {
        string path =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        AssertFactoryParity(path, candidate =>
            candidate.DeclaringType.Name == "Box`1"
            && candidate.Name == "Store"
            && candidate.ParameterTypes is
                [{ Kind: TypeRefKind.GenericParameter }]);
    }

    [Fact]
    public void StructuredAndMetadataFactoriesPreserveFunctionPointers()
    {
        string path = FixtureCatalog
            .Get(FixtureIds.AnalysisCallFunctionPointerScope)
            .AssemblyPath();
        AssertFactoryParity(path, candidate =>
            candidate.DeclaringType.Name == "Target`1"
            && candidate.Name == "Invoke");
    }

    static void AssertFactoryParity(
        string path,
        Func<MethodIdentity, bool> predicate)
    {
        LibraryBodyIndex index = LibraryBodyIndex.Open(path);
        MethodIdentity method =
            index.DeclaredMethods.Single(predicate);
        Assert.True(MethodBodyIdentityFactory.TryCreate(
            method,
            out MethodBodyIdentity? structured));

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle methodHandle =
            MetadataTokens.MethodDefinitionHandle(
                method.MetadataToken & 0x00FFFFFF);
        Assert.True(MethodBodyIdentityFactory.TryCreate(
            reader,
            reader.GetMethodDefinition(methodHandle).GetDeclaringType(),
            methodHandle,
            method.IsExtension,
            out MethodBodyIdentity? metadata));

        Assert.Equal(structured, metadata);
        Assert.Equal(
            structured!.CanonicalIdentity,
            metadata!.CanonicalIdentity);
    }

    static MethodIdentity Method(
        Guid moduleVersionId,
        int metadataToken,
        TypeRef parameter,
        string genericParameterName)
        => new(
            "Sample",
            moduleVersionId,
            TypeRef.Definition("Sample", "Example", "Container`1"),
            "Transform",
            [parameter],
            TypeRef.CoreLib("System", "String"),
            metadataToken,
            IsStatic: true,
            IsExtension: false,
            GenericArity: 1,
            GenericParameterNames:
                ImmutableArray.Create(genericParameterName));
}
