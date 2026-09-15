using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class TypeOfRenderingTests
{
    [Fact]
    public void TypeOfFoldPreservesCallOrigin()
    {
        TypeRef target = TypeRef.CoreLib("System", "String");
        var call = new Call(new MethodRef(TypeRef.CoreLib("System", "Type"), "GetTypeFromHandle",
            TypeRef.CoreLib("System", "Type"), [TypeRef.CoreLib("System", "RuntimeTypeHandle")], false),
            false, [new LoadToken(RuntimeTokenKind.Type, target, "string")]);
        call.SetSourceOffset(25);
        IrFunction function = Returning(call);
        new TypeOfFoldingPass().Run(function, PassContext.None);
        function.CheckInvariant();
        Assert.Equal(25, Assert.Single(function.Descendants.OfType<TypeOf>()).SourceOffset);
    }

    [Fact]
    public void TypeGetTypeFromHandle_UserLookalike_IsNotFolded()
    {
        var owner = TypeRef.Definition("Synthetic", "Tests", "Owner");
        var target = TypeRef.Definition("Synthetic", "Tests", "Target");
        var getTypeFromHandle = new MethodRef(
            TypeRef.Definition("UserAssembly", "System", "Type"),
            "GetTypeFromHandle",
            TypeRef.CoreLib("System", "Object"),
            [TypeRef.CoreLib("System", "RuntimeTypeHandle")],
            HasThis: false);
        var function = Returning(
            new Call(
                getTypeFromHandle,
                isVirtual: false,
                [new LoadToken(RuntimeTokenKind.Type, target, "Synthetic.Tests.Target")]),
            returnType: TypeRef.CoreLib("System", "Object"),
            owner);

        new TypeOfFoldingPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<TypeOf>());
        Assert.Single(function.Descendants.OfType<Call>());
        function.CheckInvariant();
    }

    [Fact]
    public void TypeOf_OpenGenericDefinition_RendersUnboundGenericType()
    {
        var output = CSharpPrinter.Print(Returning(new TypeOf(TypeRef.CoreLib("System.Collections.Generic", "List`1")))).Output;

        Assert.Contains("return typeof(List<>);", output);
        Assert.DoesNotContain("typeof(List)", output);
    }

    [Fact]
    public void TypeOf_OpenGenericDefinitionArityTwo_RendersAllCommas()
    {
        var output = CSharpPrinter.Print(Returning(new TypeOf(TypeRef.CoreLib("System.Collections.Generic", "Dictionary`2")))).Output;

        Assert.Contains("return typeof(Dictionary<,>);", output);
        Assert.DoesNotContain("typeof(Dictionary)", output);
    }

    [Fact]
    public void TypeOf_ZeroArityGenericInstance_IsNotFullFidelity()
    {
        var malformed = TypeRef.GenericInstance(
            TypeRef.Definition("Synthetic", "Tests", "Plain"),
            [TypeRef.CoreLib("System", "Int32")]);

        var result = CSharpPrinter.PrintRaised(
            Returning(new TypeOf(malformed)));

        Assert.Equal("Plain<int>", malformed.ToDisplayString());
        Assert.NotEqual(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void TypeOf_MissingArityUsesTrustedGenericOwnership()
    {
        MetadataTypeDefinitionName exact =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Tests",
                    ["Widget"]))
            .Name;
        var definition = TypeRef.DefinitionWithResolution(
            "Synthetic",
            "Tests",
            "Widget",
            ValueTypeHint.Unknown,
            MetadataFactState.Unknown,
            enclosingType: null,
            definitionName: exact,
            resolutionAssembly: null,
            introducedTypeParameterCounts: [1]);
        var constructed = TypeRef.GenericInstance(
            definition,
            [TypeRef.CoreLib("System", "Int32")]);

        var result = CSharpPrinter.PrintRaised(
            Returning(new TypeOf(constructed)));

        Assert.Equal("Widget<int>", constructed.ToDisplayString());
        Assert.Contains("typeof(Widget<int>)", result.Output);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void TypeOf_PerSegmentArityMismatch_PreservesRawEvidence()
    {
        MetadataTypeDefinitionName exact =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Tests",
                    ["Outer`2", "Inner"]))
            .Name;
        var definition = TypeRef.DefinitionWithResolution(
            "Synthetic",
            "Tests",
            "Outer`2+Inner",
            ValueTypeHint.Unknown,
            MetadataFactState.Unknown,
            enclosingType: null,
            definitionName: exact,
            resolutionAssembly: null,
            introducedTypeParameterCounts: [1, 1]);
        var malformed = TypeRef.GenericInstance(
            definition,
            [
                TypeRef.CoreLib("System", "Int32"),
                TypeRef.CoreLib("System", "String"),
            ]);

        var result = CSharpPrinter.PrintRaised(
            Returning(new TypeOf(malformed)));

        Assert.Contains("typeof(Outer`2.Inner)", result.Output);
        Assert.NotEqual(DecompilationFidelity.Full, result.Fidelity);
    }

    [Fact]
    public void TypeOf_BareDefinitionArityMismatch_IsNotFullFidelity()
    {
        MetadataTypeDefinitionName exact =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Tests",
                    ["Widget`2"]))
            .Name;
        var definition = TypeRef.DefinitionWithResolution(
            "Synthetic",
            "Tests",
            "Widget`2",
            ValueTypeHint.Unknown,
            MetadataFactState.Unknown,
            enclosingType: null,
            definitionName: exact,
            resolutionAssembly: null,
            introducedTypeParameterCounts: [1]);

        var result = CSharpPrinter.PrintRaised(
            Returning(new TypeOf(definition)));

        Assert.Contains("typeof(Widget`2", result.Output);
        Assert.NotEqual(DecompilationFidelity.Full, result.Fidelity);
    }

    static IrFunction Returning(IrExpression expression, TypeRef? returnType = null, TypeRef? owner = null)
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new Return(expression));
        body.Add(block);
        return new IrFunction(
            "M",
            owner ?? TypeRef.Definition("Synthetic", "Tests", "Owner"),
            new MethodSignature(returnType ?? TypeRef.CoreLib("System", "Type"), [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
