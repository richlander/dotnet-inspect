using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Validity")]
public class PrimaryConstructorStorageTests
{
    [Theory]
    [InlineData("ExplicitSafePrimaryStorage", false, true)]
    [InlineData("ExplicitUnsafePrimaryStorage", true, false)]
    public void LayoutStorage_RetainsFieldAndOrdinaryConstructor(
        string name, bool requiresUnsafe, bool isReadOnly)
    {
        string path = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        using var pe = new PEReader(File.OpenRead(path));
        var type = Assert.Single(ApiSurfaceExtractor.Extract(pe).Types,
            t => t.FullName == $"ILInspector.Decompiler.Fixtures.NewUnsafe.{name}");
        Assert.Equal(ApiTypeLayout.Explicit, type.Layout);
        Assert.Equal(MemorySafetyRulesState.Updated,
            Assert.IsType<MemorySafetyRulesResult.Available>(type.MemorySafety!.Rules).State);
        var storage = Assert.Single(type.Members, m => m.Kind == "field" && m.Name == "Value");
        var facts = Assert.IsType<ApiMemberMemorySafetyFacts>(storage.MemorySafety);
        Assert.Equal(MemorySafetyPointerEvidence.Absent, facts.SignaturePointer);
        if (requiresUnsafe)
            Assert.IsType<MemorySafetyMemberContractResult.Explicit>(facts.CallerContract);
        else
            Assert.IsType<MemorySafetyMemberContractResult.None>(facts.CallerContract);

        var declaration = Project(type, path);
        Assert.Null(declaration.ParameterList);
        var field = Assert.Single(declaration.Members.OfType<FieldDeclarationSyntax>());
        var variable = Assert.Single(field.Declaration.Variables);
        Assert.Equal("Value", variable.Identifier.ValueText);
        Assert.Null(variable.Initializer);
        Assert.Equal(isReadOnly, field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword));
        Assert.Contains(field.AttributeLists.SelectMany(list => list.Attributes),
            attribute => attribute.ToString() == "FieldOffset(0)");

        var constructor = Assert.Single(declaration.Members.OfType<ConstructorDeclarationSyntax>());
        Assert.Equal(name, constructor.Identifier.ValueText);
        Assert.Equal("value", Assert.Single(constructor.ParameterList.Parameters).Identifier.ValueText);
        Assert.Contains(constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>(),
            assignment => assignment.Left.ToString() is "Value" or "this.Value"
                && assignment.Right is IdentifierNameSyntax { Identifier.ValueText: "value" });
    }

    [Fact]
    public void OrdinaryCaptures_KeepTheirExistingLoweredShape()
    {
        string path = FixtureCatalog.DecompilerLadderRung5.AssemblyPath();
        using var pe = new PEReader(File.OpenRead(path));
        var type = Assert.Single(ApiSurfaceExtractor.Extract(pe).Types,
            t => t.FullName == "LadderRung5.PrimaryCounter");

        var declaration = Project(type, path);
        Assert.Null(declaration.ParameterList);
        var fields = declaration.Members.OfType<FieldDeclarationSyntax>().ToArray();
        Assert.Equal(["seed", "step"],
            fields.SelectMany(field => field.Declaration.Variables)
                .Select(variable => variable.Identifier.ValueText));
        Assert.All(fields, field =>
            Assert.Null(Assert.Single(field.Declaration.Variables).Initializer));
        var constructor = Assert.Single(declaration.Members.OfType<ConstructorDeclarationSyntax>());
        Assert.Equal(["seed", "step"],
            constructor.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText));
        Assert.Equal(["this.seed = seed", "this.step = step"],
            constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Select(assignment => assignment.ToString()));
    }

    static TypeDeclarationSyntax Project(ApiType type, string path)
    {
        var result = MemberBodyProducer.Project(type, path, pdbPath: null);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Output);
        var tree = CSharpSyntaxTree.ParseText(result.Output);
        return Assert.Single(tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>());
    }
}
