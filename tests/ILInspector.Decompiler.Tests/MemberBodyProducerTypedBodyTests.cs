using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.MetadataPrimitives;
using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
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
        var wrongReaderAddress = MetadataMethodAddress.Create(otherSource.Reader, otherMethod);

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

    [Theory]
    [InlineData("SelectedAutoPropertySamples", "ComputedCount", false, "return field + 1;")]
    [InlineData("SelectedAutoPropertySamples", "Count", true, "return field;")]
    [InlineData("SelectedFieldPropertySamples", "SharedCount", false, "return field + 2;")]
    public void ProduceBody_OptsIntoProvenGetterStorageOnlyWithPropertyContext(
        string typeName, string propertyName, bool automatic, string expectedBody)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var method = FindMethod(source.Reader, typeName, $"get_{propertyName}");
        var property = Assert.IsType<SelectedPropertyAccessorSource>(
            SelectedPropertyAccessorSource.Create(source, method, out bool automaticGetterBody));
        Assert.Equal(automatic, automaticGetterBody);
        var address = MetadataMethodAddress.Create(source.Reader, method);

        var independent = MemberBodyProducer.ProduceBody(source, address);
        var scoped = MemberBodyProducer.ProduceBody(source, address, property);
        var independentAgain = MemberBodyProducer.ProduceBody(source, address);

        Assert.Equal(MemberBodyProductionStatus.Complete, independent.Status);
        Assert.Equal(MemberBodyProductionStatus.Complete, scoped.Status);
        Assert.Equal(MemberBodyProductionStatus.Complete, independentAgain.Status);
        Assert.Equal(expectedBody, scoped.Body!.Source);
        Assert.DoesNotContain("field", independent.Body!.Source);
        Assert.Equal(independent.Body.Source, independentAgain.Body!.Source);
    }

    [Theory]
    [InlineData("ChangingCount")]
    [InlineData("LazyLabel")]
    [InlineData("MutableCount")]
    [InlineData("InitialCount")]
    [InlineData("DescribedCount")]
    [InlineData("NestedCount")]
    [InlineData("ProtectedCount")]
    public void ProduceBody_MetadataGetterContextKeepsStorageDeclineBoundaries(string propertyName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var method = FindMethod(source.Reader, "SelectedFieldPropertySamples", $"get_{propertyName}");

        Assert.Null(SelectedPropertyAccessorSource.Create(source, method));
    }

    [Theory]
    [InlineData("DescribedCount")]
    [InlineData("DebugCount")]
    public void ProduceBody_TrivialGetterProofDoesNotClaimDeclarationAttributeSupport(string propertyName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var method = FindMethod(source.Reader, "SelectedAutoPropertySamples", $"get_{propertyName}");

        Assert.Null(SelectedPropertyAccessorSource.Create(source, method, out bool automaticGetterBody));
        Assert.True(automaticGetterBody);
    }

    [Theory]
    [InlineData("SelectedUnsafeAutoPropertySamples", "Pointer")]
    [InlineData("SelectedUnsafeAutoPropertySamples", "SharedPointer")]
    [InlineData("SelectedUnsafeAutoPropertySamples", "FunctionPointer")]
    [InlineData("SelectedUnsafeAutoPropertySamples", "SharedFunctionPointer")]
    [InlineData("SelectedLayoutAutoPropertySamples", "Count")]
    public void ProduceBody_TrivialGetterProofIsIndependentOfSelectedDeclarationEligibility(
        string typeName, string propertyName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var method = FindMethod(source.Reader, typeName, $"get_{propertyName}");

        Assert.Null(SelectedPropertyAccessorSource.Create(source, method, out bool automaticGetterBody));
        Assert.True(automaticGetterBody);
    }

    [Theory]
    [InlineData("ConstructorGetterList`1", "Items", true)]
    [InlineData("ConstructorGetterCounter", "Value", true)]
    [InlineData("ConstructorGetterComputed", "Value", true)]
    [InlineData("ConstructorGetterParameterName", "Value", true)]
    [InlineData("ConstructorGetterKeywordParameter", "Value", true)]
    [InlineData("ConstructorGetterCalculated", "Value", false)]
    [InlineData("ConstructorGetterConditional", "Value", false)]
    [InlineData("ConstructorGetterOverloads", "Value", false)]
    [InlineData("ConstructorGetterOtherStorage", "Value", false)]
    [InlineData("ConstructorGetterClass", "Value", false)]
    [InlineData("ConstructorGetterUnusedParameter", "Value", false)]
    public void PropertyInitializationConstructorUsesExactStorage(
        string typeName, string propertyName, bool expected)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var getter = FindMethod(source.Reader, typeName, $"get_{propertyName}");
        var property = Assert.IsType<SelectedPropertyAccessorSource>(
            SelectedPropertyAccessorSource.Create(source, getter));
        var constructor = property.FindInitializationConstructor(source);

        Assert.Equal(expected, constructor is not null);
        if (constructor is { Address: var address })
        {
            Assert.True(address.BelongsTo(source.Reader));
            Assert.Equal(".ctor", source.Reader.GetString(
                source.Reader.GetMethodDefinition(address.Handle).Name));
            Assert.Equal(MemberBodyProductionStatus.Complete,
                MemberBodyProducer.ProduceBody(source, address).Status);
        }
    }

    [Theory]
    [InlineData("ConstructorGetterList`1", "Items", "items")]
    [InlineData("ConstructorGetterCounter", "Value", "value")]
    [InlineData("ConstructorGetterComputed", "Value", "value")]
    [InlineData("ConstructorGetterParameterName", "Value", "Value")]
    [InlineData("ConstructorGetterKeywordParameter", "Value", "@event")]
    [InlineData("ConstructorGetterOptional", "Value", "value")]
    [InlineData("ConstructorGetterPrivate", "Value", null)]
    [InlineData("ConstructorGetterAttributed", "Value", null)]
    [InlineData("ConstructorGetterImplementation", "Value", null)]
    [InlineData("ConstructorGetterTypeName`1", "Items", null)]
    [InlineData("ConstructorGetterTypeParameter`1", "Value", null)]
    [InlineData("ConstructorGetterReturnAttributeCollision", "Value", null)]
    [InlineData("ConstructorGetterReturnAttribute", "Value", "value")]
    [InlineData("ConstructorGetterPropertyAttribute", "Value", "System")]
    public void PropertyInitializerUsesProvenParameterWithoutWideningItsScope(
        string typeName, string propertyName, string? expected)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var getter = FindMethod(source.Reader, typeName, $"get_{propertyName}");
        var property = Assert.IsType<SelectedPropertyAccessorSource>(
            SelectedPropertyAccessorSource.Create(source, getter));
        var body = MemberBodyProducer.ProduceBody(
            source, MetadataMethodAddress.Create(source.Reader, getter), property);
        Assert.Equal(MemberBodyProductionStatus.Complete, body.Status);
        var constructor = Assert.IsType<SelectedPropertyAccessorSource.PropertyInitializationConstructor>(
            property.FindInitializationConstructor(source));
        var initializer = constructor.GetInitializerSource(body.Body!.Source);

        Assert.Equal(expected, initializer?.Expression);
        if (initializer is not null)
        {
            Assert.Equal(expected!.TrimStart('@'), initializer.Parameter.Name);
            Assert.Equal(typeName == "ConstructorGetterOptional", initializer.Parameter.HasDefault);
        }
    }

    [Theory]
    [InlineData("ConstructorGetterComputed", "field + 1")]
    [InlineData("ConstructorGetterExpressionAttribute", "field + 1")]
    [InlineData("ConstructorGetterLogged", null)]
    public void ProduceBody_CarriesSingleLineGetterExpressionWithoutChangingBlock(
        string typeName, string? expression)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var getter = FindMethod(source.Reader, typeName, "get_Value");
        var property = Assert.IsType<SelectedPropertyAccessorSource>(
            SelectedPropertyAccessorSource.Create(source, getter));
        var result = MemberBodyProducer.ProduceBody(
            source, MetadataMethodAddress.Create(source.Reader, getter), property);

        Assert.Equal(MemberBodyProductionStatus.Complete, result.Status);
        Assert.Equal(expression, result.SingleLineExpression);
        Assert.Contains("return ", result.Body!.Source);
        if (expression is null)
            Assert.Contains("Console.WriteLine(field);", result.Body.Source);
        else
            Assert.Equal($"return {expression};", result.Body.Source);
    }

    [Theory]
    [InlineData("ConstructorGetterExplicitAutomatic")]
    [InlineData("ConstructorGetterExplicitComputed")]
    public void PropertyInitializationConstructorDeclinesExplicitInterface(string typeName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath());
        var getter = FindMethod(source.Reader, typeName,
            "ILInspector.Decompiler.Fixtures.IConstructorGetterValue.get_Value");
        var property = Assert.IsType<SelectedPropertyAccessorSource>(
            SelectedPropertyAccessorSource.Create(source, getter));

        Assert.Null(property.FindInitializationConstructor(source));
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
        => MemberBodyProducer.ProduceBody(source, MetadataMethodAddress.Create(source.Reader, method));
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
