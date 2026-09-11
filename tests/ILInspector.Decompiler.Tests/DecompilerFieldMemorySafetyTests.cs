using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Fixtures;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using ChainB = ILInspector.Decompiler.Fixtures.UnsafeChainB.LibraryB;
using LegacyFixtures =
    ILInspector.Decompiler.Fixtures.LegacyUnsafe.FieldMemorySafetyFixtures;
using NewFixtures =
    ILInspector.Decompiler.Fixtures.NewUnsafe.FieldMemorySafetyFixtures;
using MethodDefinitionHandle =
    System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

public class DecompilerFieldMemorySafetyTests
{
    [Theory]
    [InlineData(nameof(NewFixtures.ReadUnsafeField), "return unsafe(FieldMemorySafetyFixtures.UnsafeField);", false)]
    [InlineData(nameof(NewFixtures.WriteUnsafeField), "FieldMemorySafetyFixtures.UnsafeField = value", true)]
    [InlineData(nameof(NewFixtures.AddressUnsafeField), "return ref unsafe(FieldMemorySafetyFixtures.UnsafeField);", false)]
    [InlineData(nameof(NewFixtures.ReadOrInitializeUnsafeField), "unsafe(FieldMemorySafetyFixtures.UnsafeTextField", false)]
    [InlineData(nameof(NewFixtures.ReadGenericUnsafeField), "return unsafe(GenericUnsafeFieldHolder<T>.Value);", false)]
    [InlineData(nameof(NewFixtures.ReadInstanceUnsafeField), "return unsafe(holder.UnsafeField);", false)]
    [InlineData(nameof(NewFixtures.WriteInstanceUnsafeField), "holder.UnsafeField = value", true)]
    [InlineData(nameof(NewFixtures.AddressInstanceUnsafeField), "return ref unsafe(holder.UnsafeField);", false)]
    public void UpdatedFieldContract_UsesSmallestValidContext(
        string method,
        string expected,
        bool expectsBlock)
    {
        DecompilerResult result = DecompileNew(method);

        Assert.Contains(expected, result.Output);
        Assert.Equal(expectsBlock, result.Output!.Contains("unsafe\n{", StringComparison.Ordinal));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData(nameof(NewFixtures.ReadSafeField))]
    [InlineData(nameof(NewFixtures.ReadSafePointerField))]
    [InlineData(nameof(NewFixtures.ReadInstanceSafeField))]
    public void UpdatedPositiveNoContract_DoesNotInferFromFieldShape(
        string method)
    {
        DecompilerResult result = DecompileNew(method);

        Assert.DoesNotContain("unsafe", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Theory]
    [InlineData(nameof(ChainB.ReadContractField), "return unsafe(LibraryA.ContractField);", false)]
    [InlineData(nameof(ChainB.WriteContractField), "LibraryA.ContractField = value", true)]
    [InlineData(nameof(ChainB.AddressContractField), "return ref unsafe(LibraryA.ContractField);", false)]
    public void CrossAssemblyFieldContract_UsesExactResolvedFieldDef(
        string method,
        string expected,
        bool expectsBlock)
    {
        DecompilerResult result = Decompile(
            typeof(ChainB).Assembly.Location,
            typeof(ChainB).FullName!,
            method);

        Assert.Contains(expected, result.Output);
        Assert.Equal(expectsBlock, result.Output!.Contains("unsafe\n{", StringComparison.Ordinal));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void CrossAssemblyPositiveNoContract_RemainsSafe()
    {
        DecompilerResult result = Decompile(
            typeof(ChainB).Assembly.Location,
            typeof(ChainB).FullName!,
            nameof(ChainB.ReadSafeField));

        Assert.DoesNotContain("unsafe", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void CrossAssemblyFieldContract_MatchesForwardedSignatureType()
    {
        using var deployment = new ForwardedFieldDeployment();

        DecompilerResult result = Decompile(
            deployment.CallerPath,
            "ILInspector.Decompiler.Fixtures.ForwardedFieldCaller.FieldCaller",
            "Read");

        Assert.Contains("return unsafe(Holder.Self);", result.Output);
        Assert.DoesNotContain("unsafe\n{", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void LegacyImplicitPointerContract_IsEnforcedByUpdatedSimulation()
    {
        using var source =
            MetadataSource.Open(typeof(LegacyFixtures).Assembly.Location);
        source.SimulateNewRules = true;
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                typeof(LegacyFixtures).FullName!,
                nameof(LegacyFixtures.ReadLegacyPointerField)));

        DecompilerResult result = CSharpPrinter.PrintRaised(function);

        Assert.Contains(
            "return unsafe(FieldMemorySafetyFixtures.LegacyPointerField);",
            result.Output);
        Assert.DoesNotContain("unsafe\n{", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void ExplicitUpdatedContract_IsNotEnforcedForLegacyCaller()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var owner = TypeRef.Definition(
            "Synthetic",
            "Fixtures",
            "Holder");
        var field = new FieldRef(owner, "Value", int32)
        {
            HasNormalizedMemorySafetyContract = true,
            RequiresUnsafe = true,
            RequiresUnsafeFact = MetadataFactState.Yes,
            MemorySafetyRulesState = MemorySafetyRulesState.Updated,
        };
        var block = new Block();
        block.Add(new Return(new LoadField(field, instance: null)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Read",
            owner,
            new MethodSignature(
                int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.DoesNotContain("unsafe", result.Output);
        Assert.False(result.RequiresUnsafeBodyModifier);
    }

    [Fact]
    public void AwaitBoundary_UsesSameFieldContractAsPrinter()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var taskType = TypeRef.CoreLib(
            "System.Threading.Tasks",
            "Task");
        var owner = TypeRef.Definition(
            "Synthetic",
            "Fixtures",
            "Holder");
        var field = new FieldRef(owner, "Gate", boolType)
        {
            HasNormalizedMemorySafetyContract = true,
            RequiresUnsafe = true,
            RequiresUnsafeFact = MetadataFactState.Yes,
            MemorySafetyRulesState = MemorySafetyRulesState.Updated,
        };
        var body = new Block();
        body.Add(new ExpressionStatement(
            new AwaitExpression(
                new LoadArgument(0, "task", taskType),
                resultType: null)));
        var statement = new IfStatement(
            new LoadField(field, instance: null),
            body,
            elseArm: null);

        Assert.True(UnsafeAwaitOperand.WouldPlaceAwaitInUnsafeContext(
            statement,
            usesUpdatedMemorySafetyRules: true));
    }

    [Fact]
    public void RuntimeAwaitBoundary_FieldContractDeclinesVisibly()
    {
        Type fixtureType = typeof(NewFixtures);
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var method = fixtureType.GetMethod(
            nameof(NewFixtures.AwaitUnsafeField));
        Assert.NotNull(method);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(
                source,
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                    method.MetadataToken)));

        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(
                method => IrImporter.Import(source, method)));
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.Contains(
            function.Descendants.OfType<UnsupportedNode>(),
            node => node.Opcode == "unsafe await boundary");
        Assert.DoesNotContain("unsafe\n{\n    return await", result.Output);
    }

    [Fact]
    public void RaisedFieldCarrier_UsesRetainedFieldContract()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var owner = TypeRef.Definition(
            "Synthetic",
            "Fixtures",
            "Holder");
        var field = new FieldRef(owner, "Value", stringType)
        {
            HasNormalizedMemorySafetyContract = true,
            RequiresUnsafe = true,
            RequiresUnsafeFact = MetadataFactState.Yes,
            MemorySafetyRulesState = MemorySafetyRulesState.Updated,
        };
        var block = new Block();
        block.Add(new Return(
            new NullCoalescingFieldAssignmentExpression(
                field,
                instance: null,
                new Constant("initialized", stringType))));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Read",
            owner,
            new MethodSignature(
                stringType,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Contains("return unsafe(Holder.Value ??= \"initialized\");", result.Output);
        Assert.DoesNotContain("unsafe\n{", result.Output);
    }

    [Fact]
    public void UnavailableFieldContract_DoesNotInventUnsafeContext()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var owner = TypeRef.Definition(
            "Dependency",
            "Fixtures",
            "Library");
        var field = new FieldRef(owner, "Value", int32)
        {
            MemorySafetyRulesState = MemorySafetyRulesState.Updated,
            MemorySafetyContractUnavailable = true,
        };
        var block = new Block();
        block.Add(new Return(new LoadField(field, instance: null)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Read",
            owner,
            new MethodSignature(
                int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.DoesNotContain("unsafe", result.Output);
        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
    }

    [Fact]
    public void LegacyCaller_NonPointerFieldDoesNotRequireUnavailableContract()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var owner = TypeRef.Definition(
            "Dependency",
            "Fixtures",
            "Library");
        var field = new FieldRef(owner, "Value", int32);
        var block = new Block();
        block.Add(new Return(new LoadField(field, instance: null)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "Read",
            owner,
            new MethodSignature(
                int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.DoesNotContain("unsafe", result.Output);
    }

    static DecompilerResult DecompileNew(string method)
        => Decompile(
            typeof(NewFixtures).Assembly.Location,
            typeof(NewFixtures).FullName!,
            method);

    static DecompilerResult Decompile(
        string assemblyPath,
        string typeFullName,
        string method)
    {
        using var source = MetadataSource.Open(assemblyPath);
        IrFunction function = Assert.IsType<IrFunction>(
            IrImporter.Import(source, typeFullName, method));
        DecompilerResult result = CSharpPrinter.PrintRaised(function);
        Assert.NotNull(result.Output);
        return result;
    }

    sealed class ForwardedFieldDeployment : IDisposable
    {
        readonly string _directory =
            Directory.CreateTempSubdirectory("forwarded-field-").FullName;

        internal ForwardedFieldDeployment()
        {
            CallerPath = Copy(FixtureCatalog.DecompilerForwardedFieldCaller);
            Copy(FixtureCatalog.DecompilerForwardedFieldTargetDeployment);
            Copy(FixtureCatalog.ServicesRouteLearningMiddle);
            Copy(FixtureCatalog.ServicesRouteLearningBase);
        }

        internal string CallerPath { get; }

        string Copy(FixtureDefinition fixture)
        {
            string destination = Path.Combine(
                _directory,
                fixture.AssemblyFileName);
            File.Copy(fixture.AssemblyPath(), destination);
            return destination;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

}
