using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Microsoft.CodeAnalysis;

namespace ILInspector.Decompiler.Tests;

public sealed partial class SelectedPropertySourceTests
{
    [Theory]
    [InlineData("ConstructorGetterList`1", "Items", "items")]
    [InlineData("ConstructorGetterCounter", "Value", "value")]
    [InlineData("ConstructorGetterComputed", "Value", "value")]
    [InlineData("ConstructorGetterLogged", "Value", "value")]
    [InlineData("ConstructorGetterExpressionAttribute", "Value", "value")]
    [InlineData("ConstructorGetterParameterName", "Value", "Value")]
    [InlineData("ConstructorGetterKeywordParameter", "Value", "@event")]
    [InlineData("ConstructorGetterOptional", "Value", "value")]
    [InlineData("ConstructorGetterReturnAttribute", "Value", "value")]
    [InlineData("ConstructorGetterAnnotationName", "Value", "arg")]
    public void SelectedInitializerCarriesCompilableContainingContext(
        string typeName, string propertyName, string parameter)
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path, $"ILInspector.Decompiler.Fixtures.{typeName}", propertyName, "get");
        var attempt = ProduceSelectedSource(path, type, accessor);

        Assert.Equal(CSharpDecompilationStatus.Available, attempt.Status);
        Assert.Contains($"= {parameter};", attempt.Text);
        Assert.Equal(2, attempt.BodyProjectionsAttempted);
        Assert.Single(attempt.BodyProjections, body => body.ContributesToOutput);
        Assert.Contains(attempt.BodyProjections,
            body => body.Kind == CSharpBodyProjectionKind.FieldInitializerProbe && !body.ContributesToOutput);
        var compilation = AssertCompiles(attempt.Text!);
        AssertGetterInstructionsMatch(compilation, path, accessor, normalizeFrameworkFacades: true);
        AssertInitializationStore(compilation, path, accessor);

        string fragment = MemberBodyProducer.ProduceMember(type, accessor, path, pdbPath: null).Text!;
        Assert.DoesNotContain($"= {parameter};", fragment);
        Assert.DoesNotContain("namespace ", fragment);
        string containing = MemberBodyProducer.Project(
            Extract(path, type.FullName), path, pdbPath: null).Output!;
        Assert.DoesNotContain($"}} = {parameter};", containing);
        Assert.Equal(1, containing.Split($"struct {typeName.Split('`')[0]}", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, containing.Split($"public {typeName.Split('`')[0]}(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void PublishedDocoptSelectedInitializerPreservesConstructorAndGetter()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", "FieldGetter", "DocoptNet.dll");
        var (type, accessor) = Select(path, "DocoptNet.Internals.ReadOnlyList`1", "List", "get");
        var attempt = ProduceSelectedSource(path, type, accessor);

        Assert.Equal(CSharpDecompilationStatus.Available, attempt.Status);
        Assert.Contains("readonly struct ReadOnlyList<T>(IList<T> list)", attempt.Text);
        Assert.Contains("get => field ?? Array.Empty<T>();", attempt.Text);
        Assert.Contains("} = list;", attempt.Text);
        Assert.DoesNotContain("get_List(", attempt.Text);
        Assert.DoesNotContain("this.List", attempt.Text);
        var compilation = AssertCompiles(attempt.Text!);
        AssertGetterInstructionsMatch(compilation, path, accessor, normalizeFrameworkFacades: true);
        AssertInitializationStore(compilation, path, accessor);
    }

    [Theory]
    [InlineData("ConstructorGetterPrivate", "Value")]
    [InlineData("ConstructorGetterAttributed", "Value")]
    [InlineData("ConstructorGetterImplementation", "Value")]
    [InlineData("ConstructorGetterTypeName`1", "Items")]
    [InlineData("ConstructorGetterTypeParameter`1", "Value")]
    [InlineData("ConstructorGetterReturnAttributeCollision", "Value")]
    [InlineData("ConstructorGetterCalculated", "Value")]
    [InlineData("ConstructorGetterConditional", "Value")]
    [InlineData("ConstructorGetterOverloads", "Value")]
    [InlineData("ConstructorGetterOtherStorage", "Value")]
    [InlineData("ConstructorGetterClass", "Value")]
    [InlineData("ConstructorGetterUnusedParameter", "Value")]
    [InlineData("ConstructorGetterContainer.Nested", "Value")]
    public void SelectedInitializerDeclinesUnsupportedConstruction(string typeName, string propertyName)
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path, $"ILInspector.Decompiler.Fixtures.{typeName}", propertyName, "get");
        var attempt = ProduceSelectedSource(path, type, accessor);

        Assert.Equal(CSharpDecompilationStatus.Available, attempt.Status);
        Assert.DoesNotContain("namespace ", attempt.Text);
        Assert.DoesNotContain("} =", attempt.Text);
        Assert.Contains(propertyName, attempt.Text);
    }

    [Fact]
    public void SelectedInitializerProjectionFailureRemainsVisibleToOtherViews()
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path, "ILInspector.Decompiler.Fixtures.ConstructorGetterComputed", "Value", "get");
        using var source = MetadataSource.OpenWithoutSymbols(path);
        var property = Assert.IsType<SelectedPropertyAccessorSource>(
            SelectedPropertyAccessorSource.Create(
                source, accessor.MetadataToken!.Value, accessor, includeContainingContext: true));
        var handle = MetadataTokens.MethodDefinitionHandle(accessor.MetadataToken.Value & 0x00ffffff);
        var produced = MemberBodyProducer.ProduceBody(source, MetadataMethodAddress.Create(source.Reader, handle));
        Assert.NotNull(produced.Body);
        property.BindInitializationContext(DecompilerResult.Failure("TEST001", "Getter projection unavailable."));

        var failure = Assert.Throws<InvalidOperationException>(() => property.Format(type, produced.Body));
        Assert.Contains("Getter projection unavailable.", failure.Message);
    }

    [Fact]
    public void SelectedInitializerConstructorProbeConsumesTheServiceBudget()
    {
        string path = FixturePath(false);
        var (type, accessor) = Select(path, "ILInspector.Decompiler.Fixtures.ConstructorGetterComputed", "Value", "get");
        var attempt = ProduceSelectedSource(path, type, accessor, maxBodyProjections: 1);
        Assert.Equal(CSharpDecompilationStatus.Incomplete, attempt.Status);
        Assert.Equal(1, attempt.BodyProjectionsAttempted);
        Assert.Null(attempt.Text);
    }

    static CSharpDecompilationAttempt ProduceSelectedSource(
        string path, ApiType type, ApiMember accessor, int maxBodyProjections = 1024)
        => CSharpDecompilerService.ProduceMember(
            type, accessor,
            ResolvedAssemblyReference.CreateFromPath(path,
                AssemblyResolutionProvenance.Local("SelectedPropertySourceTests")),
            new AssemblyReferenceBindingPolicy(MetadataSource.DefaultAssemblyReferenceResolver(path)),
            maxBodyProjections: maxBodyProjections,
            cancellationToken: TestContext.Current.CancellationToken);

    static void AssertInitializationStore(Compilation compilation, string path, ApiMember accessor)
    {
        using var image = new MemoryStream();
        Assert.True(compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        image.Position = 0;
        using var pe = new PEReader(image);
        var reader = pe.GetMetadataReader();
        var constructor = Assert.Single(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == ".ctor");
        var getter = Assert.Single(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == accessor.Name);
        var constructorBody = ILInspector.Instructions.MethodInstructions.Decode(
            pe.GetMethodBody(reader.GetMethodDefinition(constructor).RelativeVirtualAddress));
        Assert.Equal([ILOpCode.Ldarg_0, ILOpCode.Ldarg_1, ILOpCode.Stfld, ILOpCode.Ret],
            constructorBody.Instructions.Select(instruction => instruction.OpCode));
        var getterBody = ILInspector.Instructions.MethodInstructions.Decode(
            pe.GetMethodBody(reader.GetMethodDefinition(getter).RelativeVirtualAddress));
        int fieldToken = (int)constructorBody.Instructions[2].OperandValue;
        Assert.Contains(getterBody.Instructions, instruction =>
            instruction.OpCode == ILOpCode.Ldfld && (int)instruction.OperandValue == fieldToken);
        using var original = new PEReader(File.OpenRead(path));
        var originalReader = original.GetMetadataReader();
        var originalMethod = originalReader.GetMethodDefinition(
            MetadataTokens.MethodDefinitionHandle(accessor.MetadataToken!.Value & 0x00ffffff));
        var originalType = originalReader.GetTypeDefinition(originalMethod.GetDeclaringType());
        var projectedType = reader.GetTypeDefinition(reader.GetMethodDefinition(getter).GetDeclaringType());
        Assert.Equal(originalReader.GetFieldDefinition(Assert.Single(originalType.GetFields())).Attributes,
            reader.GetFieldDefinition(Assert.Single(projectedType.GetFields())).Attributes);
    }
}
