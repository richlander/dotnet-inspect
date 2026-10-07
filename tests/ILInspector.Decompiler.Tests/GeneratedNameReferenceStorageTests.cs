using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates for value-typed-emission.md, "Generated-name reference storage"
/// (#9371 slice 2): named reference storage materializes when the only
/// explicit-spelling defect of its complete type is a compiler-generated
/// metadata name (display class, state machine, anonymous type, collection
/// expression type), because the residual and typed-local paths render the
/// same type text and the fidelity diagnostic reports the name either way.
/// Ordinary value storage keeps the full gate. Exact struct <c>this</c> aliases
/// are retired earlier by <see cref="ValueTypeReceiverAliasPass"/>, before
/// spelling or value-local storage is considered. Every other spelling defect
/// still defers.
/// </summary>
[Trait("Area", "Pass")]
public class GeneratedNameReferenceStorageTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef GenericReference = TypeRef.Definition("Samples", "Samples", "Reference`1");

    [Theory]
    [InlineData("<>c__DisplayClass4_0")]
    [InlineData("<Iterate>d__7")]
    [InlineData("<>f__AnonymousType0`1")]
    [InlineData("<>z__ReadOnlyArray`1")]
    public void GeneratedReferenceNameMaterializes(string name)
    {
        var definition = TypeRef.Definition("Samples", "Samples", name);
        var type = name.Contains('`') ? TypeRef.GenericInstance(definition, [Int32]) : definition;
        var function = ExactWeb(type, definition, TypeShape.Reference);

        var decision = Assert.Single(SlotMaterializationPass.Analyze(function));
        Assert.Equal(SlotMaterializationVeto.None, decision.Vetoes);
        Assert.True(decision.WillMaterialize);
        new SlotMaterializationPass().Run(function, PassContext.None);
        Assert.Equal(type, Assert.Single(function.Locals));
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    public void GeneratedNameNestedInASpellableConstructionMaterializes()
    {
        // `Reference<<>f__AnonymousType0<int>>`: the generated name is a type
        // argument of a spellable reference definition.
        var anonymous = TypeRef.Definition("Samples", "Samples", "<>f__AnonymousType0`1");
        var type = TypeRef.GenericInstance(GenericReference, [TypeRef.GenericInstance(anonymous, [Int32])]);
        var function = ExactWeb(type, GenericReference, TypeShape.Reference);

        Assert.True(Assert.Single(SlotMaterializationPass.Analyze(function)).WillMaterialize);
    }

    [Fact]
    public void GeneratedValueTypeNameRemainsDeferred()
    {
        // A struct state machine: value storage keeps the name gate.
        var definition = TypeRef.Definition("Samples", "Samples", "<RunAsync>d__3");
        var function = ExactWeb(definition, definition, TypeShape.ValueType);

        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    [InlineData("unspellable-name")]
    [InlineData("unknown-shape")]
    [InlineData("open-generic")]
    [InlineData("wrong-arity")]
    public void OtherSpellingDefectsStillDefer(string shape)
    {
        var generated = TypeRef.Definition("Samples", "Samples", "<>f__AnonymousType0`1");
        var (type, definition, typeShape) = shape switch
        {
            // A generated name does not excuse a second, non-generated defect.
            "unspellable-name" => (TypeRef.GenericInstance(GenericReference, [TypeRef.Definition("Samples", "Samples", "Bad-Name")]), GenericReference, TypeShape.Reference),
            "unknown-shape" => (TypeRef.Definition("Samples", "Samples", "<>c__DisplayClass1_0"), TypeRef.Definition("Samples", "Samples", "<>c__DisplayClass1_0"), TypeShape.Unknown),
            "open-generic" => (generated, generated, TypeShape.Reference),
            "wrong-arity" => (TypeRef.GenericInstance(generated, [Int32, Int32]), generated, TypeShape.Reference),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var function = ExactWeb(type, definition, typeShape);

        Assert.Equal(SlotMaterializationVeto.OutsideCoercionDomain,
            Assert.Single(SlotMaterializationPass.Analyze(function)).Vetoes);
        AssertRetained(function);
    }

    [Theory]
    // Newtonsoft.Json 13.0.4: a closure display class and a reference iterator
    // state machine, each spilled to a slot and bound by the residual policy
    // before this slice.
    [InlineData("ReferenceConditional/Newtonsoft.Json.dll", "Newtonsoft.Json.Utilities.ReflectionUtils", "GetChildPrivateProperties", 0)]
    [InlineData("ReferenceConditional/Newtonsoft.Json.dll", "Newtonsoft.Json.Linq.JsonPath.ArraySliceFilter.<ExecuteFilter>d__12", "MoveNext", 1)]
    // dotnet-inspect 0.14.0: a collection-expression `<>z__ReadOnlyArray<string>`
    // and a `List<anonymous type>`.
    [InlineData("SwitchSection/dotnet-inspect.dll", "DotnetInspector.Commands.PackageCommand", "AppendAggregatedSection", 3)]
    [InlineData("SwitchSection/dotnet-inspect.dll", "DotnetInspector.Commands.PackageCommand", "AppendAggregatedSection", 23)]
    public void RealGeneratedNameWebsMaterialize(string asset, string typeName, string methodName, int slot)
    {
        var function = Run(asset, typeName, methodName, out _);
        Assert.DoesNotContain(function.ResidualSlotBindings.Values, binding => binding.Slot == slot);
    }

    [Fact]
    public void RealStructStateMachineThisAliasRetiresBeforeStorage()
    {
        // Newtonsoft.Json 13.0.4: the struct async state machine's `this`
        // spill is an exact managed receiver alias, not value storage.
        var function = Run(
            "ReferenceConditional/Newtonsoft.Json.dll",
            "Newtonsoft.Json.JsonTextReader.<ParsePropertyAsync>d__31",
            "MoveNext",
            out string output);
        Assert.DoesNotContain(function.ResidualSlotBindings.Values,
            binding => binding.Slot == 0);
        Assert.DoesNotContain(" = this;", output);
    }

    static IrFunction ExactWeb(TypeRef type, TypeRef definition, TypeShape shape)
    {
        var function = Function(type,
            new StoreStackSlot(0, new Constant(null, type)),
            new Return(new LoadStackSlot(0, type)));
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [definition] = shape };
        return function;
    }

    static IrFunction Run(string asset, string typeName, string methodName, out string output)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "RealAssets", asset);
        // The corpus metadata context resolves framework definitions, so the
        // shape map classes List<T>, StringBuilder, and CultureInfo as the
        // census and the CLI see them.
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        output = CSharpPrinter.Print(function).Output ?? "";
        return function;
    }
}
