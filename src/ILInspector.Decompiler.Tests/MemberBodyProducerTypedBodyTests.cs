using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class MemberBodyProducerTypedBodyTests
{
    static string AssemblyPath => typeof(MemberBodyProducerTypedBodyTests).Assembly.Location;

    [Fact]
    public void ProduceBody_ReturnsTypedBlockAndProjectionEvidence()
    {
        using var source = MetadataSource.OpenWithoutSymbols(AssemblyPath);
        var method = FindMethod(source.Reader, nameof(TypedBodySpecimen), nameof(TypedBodySpecimen.Increment));

        var result = ProduceBody(source, method);

        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.True(result.IsComplete);
        var body = Assert.IsType<CSharpBlockBody>(result.Body);
        Assert.Contains("return value + 1;", body.Source);
        Assert.Equal(result.Projection.Output!.TrimEnd(), body.Source);
        Assert.Equal(result.Projection.RequiresAsyncBodyModifier, body.RequiresAsyncModifier);
        Assert.Equal(result.Projection.RequiresUnsafeBodyModifier, body.RequiresUnsafeModifier);
        Assert.NotEqual(DecompilationFidelity.Failed, result.Projection.Fidelity);
    }

    [Fact]
    public void ProduceBody_ReturnsAbsentForAbstractMethod()
    {
        using var source = MetadataSource.OpenWithoutSymbols(AssemblyPath);
        var method = FindMethod(source.Reader, nameof(AbstractTypedBodySpecimen), nameof(AbstractTypedBodySpecimen.Missing));

        var result = ProduceBody(source, method);

        Assert.Equal(MemberBodyProductionStatus.Absent, result.Status);
        Assert.Null(result.Body);
        Assert.False(result.Projection.Succeeded);
        Assert.Contains(result.Projection.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.ContextUnavailable);
    }

    [Fact]
    public void ProduceBody_ReturnsFailedForHandleOutsideReader()
    {
        using var source = MetadataSource.OpenWithoutSymbols(AssemblyPath);
        int rowCount = source.Reader.GetTableRowCount(TableIndex.MethodDef);
        var invalid = MetadataTokens.MethodDefinitionHandle(rowCount + 1);

        var result = ProduceBody(source, invalid);

        Assert.Equal(MemberBodyProductionStatus.Failed, result.Status);
        Assert.Null(result.Body);
        Assert.Contains(result.Projection.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.ContextUnavailable);
    }

    [Fact]
    public void ProduceBody_ReturnsFailedForAddressFromAnotherReader()
    {
        using var source = MetadataSource.OpenWithoutSymbols(AssemblyPath);
        using var otherSource = MetadataSource.OpenWithoutSymbols(typeof(object).Assembly.Location);
        var otherMethod = otherSource.Reader.MethodDefinitions.First();
        var wrongReaderAddress = MemberBodyAddress.Create(otherSource, otherMethod);

        var result = MemberBodyProducer.ProduceBody(source, wrongReaderAddress);

        Assert.Equal(MemberBodyProductionStatus.Failed, result.Status);
        Assert.Null(result.Body);
        Assert.Contains(
            result.Projection.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.ContextUnavailable
                && diagnostic.Message.Contains("different metadata module", StringComparison.Ordinal));
    }

    [Fact]
    public void ProduceBody_AddressesIndividualPropertyAccessor()
    {
        using var source = MetadataSource.OpenWithoutSymbols(AssemblyPath);
        var getter = FindMethod(source.Reader, nameof(TypedBodySpecimen), "get_Doubled");

        var result = ProduceBody(source, getter);

        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        var body = Assert.IsType<CSharpBlockBody>(result.Body);
        Assert.Contains("return _value * 2;", body.Source);
    }

    [Fact]
    public void ProduceBody_CarriesConstructorInitializer()
    {
        using var source = MetadataSource.OpenWithoutSymbols(AssemblyPath);
        var constructor = FindMethod(
            source.Reader,
            nameof(TypedConstructorSpecimen),
            ".ctor",
            method => method.GetParameters().Count == 0);

        var result = ProduceBody(source, constructor);

        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Equal("this(42)", result.Projection.ConstructorChain);
        var body = Assert.IsType<CSharpBlockBody>(result.Body);
        Assert.NotNull(body.ConstructorInitializer);
        Assert.Equal(CSharpConstructorInitializerKind.This, body.ConstructorInitializer.Kind);
        Assert.Equal(["42"], body.ConstructorInitializer.Arguments);
    }

    static MethodDefinitionHandle FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName,
        Func<MethodDefinition, bool>? predicate = null)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == methodName
                    && (predicate is null || predicate(method)))
                    return methodHandle;
            }
        }

        throw new InvalidOperationException($"Method '{typeName}::{methodName}' was not found.");
    }

    static MemberBodyProductionResult ProduceBody(MetadataSource source, MethodDefinitionHandle method)
        => MemberBodyProducer.ProduceBody(source, MemberBodyAddress.Create(source, method));
}

public sealed class TypedBodySpecimen
{
    readonly int _value = 21;

    public int Doubled => _value * 2;

    public static int Increment(int value) => value + 1;
}

public abstract class AbstractTypedBodySpecimen
{
    public abstract int Missing();
}

public sealed class TypedConstructorSpecimen
{
    public TypedConstructorSpecimen() : this(42)
    {
    }

    TypedConstructorSpecimen(int value)
    {
        Value = value;
    }

    public int Value { get; }
}
