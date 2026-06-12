using DotnetInspector.Decompiler.Pipeline;

namespace DotnetInspector.Decompiler.Tests;

public class PipelineImporterTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;

    [Fact]
    public void Import_IsNullOrEmpty_SignatureAndBody()
    {
        using var source = MetadataSource.Open(CoreLibPath);
        var method = MethodImporter.Import(source, "System.String", "IsNullOrEmpty");

        Assert.NotNull(method);
        Assert.Equal("bool", method.Signature.ReturnType.ToDisplayString());
        var parameter = Assert.Single(method.Signature.Parameters);
        Assert.Equal("value", parameter.Name);
        Assert.Equal("string", parameter.Type.ToDisplayString());
        Assert.False(method.Signature.HasThis);
        Assert.Equal(15, method.Body.IL.Length);
        Assert.Empty(method.Body.Locals);
        Assert.Empty(method.Body.Handlers);
    }

    [Fact]
    public void ImportedMethod_IsFullyMaterialized_SourceDisposalIsSafe()
    {
        // The contract the old pipeline lacked (docs/decompiler-ir.md):
        // nothing in the importer's output may depend on the live readers.
        // Under the old MethodBodyContext this pattern was a use-after-free
        // that crashed the process.
        ImportedMethod method;
        using (var source = MetadataSource.Open(CoreLibPath))
        {
            method = MethodImporter.Import(source, "System.Collections.Generic.Dictionary`2", "ContainsValue")!;
        }

        Assert.Equal("Dictionary`2", method.DeclaringType.Name);
        Assert.Equal("bool", method.Signature.ReturnType.ToDisplayString());
        Assert.Equal("TValue", method.Signature.Parameters[0].Type.ToDisplayString());
        Assert.True(method.Body.IL.Length > 0);
        Assert.All(method.Body.Locals, local => Assert.NotEqual("", local.ToDisplayString()));
    }

    [Fact]
    public void Import_GenericMethod_RecoversParameterNames()
    {
        using var source = MetadataSource.Open(CoreLibPath);
        var method = MethodImporter.Import(source, "System.Collections.Generic.List`1", "Add");

        Assert.NotNull(method);
        var parameter = Assert.Single(method.Signature.Parameters);
        Assert.Equal(TypeRefKind.GenericParameter, parameter.Type.Kind);
        Assert.Equal("T", parameter.Type.GenericParameterName);
        Assert.Equal(0, parameter.Type.GenericParameterIndex);
    }

    [Fact]
    public void OverloadSelection_CountsBodylessOverloads()
    {
        // Legacy parity: overload indices count every name match. Selecting
        // the extern overload returns null; the bodied one sits at index 1.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        Assert.Null(MethodImporter.Import(
            source, typeof(CfgSampleClass).FullName!, "Overloaded", overloadIndex: 0));
        var bodied = MethodImporter.Import(
            source, typeof(CfgSampleClass).FullName!, "Overloaded", overloadIndex: 1);
        Assert.NotNull(bodied);
        Assert.Equal("double", bodied.Signature.Parameters[0].Type.ToDisplayString());
    }

    [Fact]
    public void Import_TryCatch_MaterializesHandlerRegions()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var method = MethodImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ChecksThenTry));

        Assert.NotNull(method);
        var handler = Assert.Single(method.Body.Handlers);
        Assert.Equal(HandlerKind.Catch, handler.Kind);
        Assert.NotNull(handler.CatchType);
        Assert.True(handler.TryLength > 0);
        Assert.True(handler.HandlerLength > 0);
    }
}

public class TypeRefTests
{
    [Fact]
    public void Equality_IsSemantic_NotTextual()
    {
        var intRef = TypeRef.CoreLib("System", "Int32");
        var listDef = TypeRef.CoreLib("System.Collections.Generic", "List`1");

        Assert.Equal(intRef, TypeRef.CoreLib("System", "Int32"));
        Assert.Equal(
            TypeRef.GenericInstance(listDef, [intRef]),
            TypeRef.GenericInstance(listDef, [TypeRef.CoreLib("System", "Int32")]));
        Assert.NotEqual(
            TypeRef.GenericInstance(listDef, [intRef]),
            TypeRef.GenericInstance(listDef, [TypeRef.CoreLib("System", "String")]));
        Assert.NotEqual(TypeRef.Pointer(intRef), TypeRef.SzArray(intRef));
        Assert.Equal(
            TypeRef.GenericInstance(listDef, [intRef]).GetHashCode(),
            TypeRef.GenericInstance(listDef, [TypeRef.CoreLib("System", "Int32")]).GetHashCode());
    }

    [Fact]
    public void Instantiate_SubstitutesGenericParametersThroughShapes()
    {
        var intRef = TypeRef.CoreLib("System", "Int32");
        var listDef = TypeRef.CoreLib("System.Collections.Generic", "List`1");
        var typeParam = TypeRef.GenericParameter(0, "T");
        var methodParam = TypeRef.MethodGenericParameter(0, "TResult");

        Assert.Equal(intRef, typeParam.Instantiate([intRef], []));
        Assert.Equal(intRef, methodParam.Instantiate([], [intRef]));
        Assert.Equal(
            TypeRef.GenericInstance(listDef, [intRef]),
            TypeRef.GenericInstance(listDef, [typeParam]).Instantiate([intRef], []));
        Assert.Equal(
            TypeRef.ByRef(intRef),
            TypeRef.ByRef(methodParam).Instantiate([], [intRef]));
        // No substitution: same instance back, no churn.
        Assert.Same(intRef, intRef.Instantiate([TypeRef.CoreLib("System", "String")], []));
    }

    [Fact]
    public void Equality_GenericParameterName_IsNamingAidNotIdentity()
    {
        Assert.Equal(TypeRef.GenericParameter(0, "T"), TypeRef.GenericParameter(0, "TKey"));
        Assert.NotEqual(TypeRef.GenericParameter(0, "T"), TypeRef.GenericParameter(1, "T"));
        Assert.NotEqual(TypeRef.GenericParameter(0, "T"), TypeRef.MethodGenericParameter(0, "T"));
    }

    [Fact]
    public void FacadeCanonicalization_SameIdentityAcrossSpellings()
    {
        // The same metadata signature decoded from a System.Runtime-scoped
        // reference and from System.Private.CoreLib must compare equal.
        Assert.Equal(TypeRefDecoder.Canonical("System.Runtime"), TypeRefDecoder.Canonical("System.Private.CoreLib"));
        Assert.Equal(TypeRefDecoder.Canonical("mscorlib"), TypeRefDecoder.Canonical("netstandard"));
        Assert.NotEqual(TypeRefDecoder.Canonical("Newtonsoft.Json"), TypeRefDecoder.Canonical("System.Runtime"));
    }

    [Fact]
    public void DisplayStrings_RenderCSharpStyle()
    {
        var intRef = TypeRef.CoreLib("System", "Int32");
        var listDef = TypeRef.CoreLib("System.Collections.Generic", "List`1");

        Assert.Equal("int", intRef.ToDisplayString());
        Assert.Equal("List<int>", TypeRef.GenericInstance(listDef, [intRef]).ToDisplayString());
        Assert.Equal("int[]", TypeRef.SzArray(intRef).ToDisplayString());
        Assert.Equal("int[,]", TypeRef.MdArray(intRef, 2).ToDisplayString());
        Assert.Equal("ref int", TypeRef.ByRef(intRef).ToDisplayString());
        Assert.Equal("int*", TypeRef.Pointer(intRef).ToDisplayString());
    }

    [Fact]
    public void UnsupportedShapes_PropagateToContainingTypes()
    {
        var unsupported = TypeRef.Unsupported("function pointer");
        var listDef = TypeRef.CoreLib("System.Collections.Generic", "List`1");

        Assert.True(unsupported.ContainsUnsupported);
        Assert.True(TypeRef.SzArray(unsupported).ContainsUnsupported);
        Assert.True(TypeRef.GenericInstance(listDef, [unsupported]).ContainsUnsupported);
        Assert.False(TypeRef.GenericInstance(listDef, [TypeRef.CoreLib("System", "Int32")]).ContainsUnsupported);
    }
}
