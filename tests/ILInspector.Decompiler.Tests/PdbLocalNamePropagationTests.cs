using CSharpText.Tests;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public sealed class PdbLocalNamePropagationTests
{
    static readonly DecompilerFidelityCause ImportLoss = new(
        DiagnosticIds.UnrepresentableMetadataName,
        DecompilerFidelityLocation.AtLocal(0),
        nameof(PdbLocalDeclaration),
        "PDB LocalVariable row 1, LocalScope row 1, slot 0",
        "Scoped declaration cannot be bound to independent storage",
        DecompilerFidelityDiscriminators.ScopedLocalNameUnavailable);

    [Theory]
    [InlineData(nameof(PdbScopeFixtures.LambdaScopes))]
    [InlineData(nameof(PdbScopeFixtures.LocalFunctionScopes))]
    public void NestedBody_PreservesImportedNameLoss(string method)
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = RaiseWithImportLoss(source, typeof(PdbScopeFixtures).FullName!, method);

        if (method == nameof(PdbScopeFixtures.LambdaScopes))
            Assert.Contains(ImportLoss, Assert.Single(function.Descendants.OfType<Lambda>()).LocalNameImportCauses);
        else
            Assert.Contains(ImportLoss, Assert.Single(function.Descendants.OfType<LocalFunctionStatement>()).LocalNameImportCauses);
        AssertLoss(function);
    }

    [Theory]
    [InlineData(nameof(PdbScopeFixtures.LambdaScopes))]
    [InlineData(nameof(PdbScopeFixtures.LocalFunctionScopes))]
    public void NestedBody_PreservesSuccessfulLocalBindings(string method)
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(PdbScopeFixtures).FullName!, method)!;

        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            member => IrImporter.Import(source, member)));

        var bindings = method == nameof(PdbScopeFixtures.LambdaScopes)
            ? Assert.Single(function.Descendants.OfType<Lambda>()).LocalDeclarationBindings
            : Assert.Single(function.Descendants.OfType<LocalFunctionStatement>())
                .LocalDeclarationBindings;
        var exact = bindings.Where(static binding => binding?.Name == "same").ToArray();
        Assert.True(exact.Length >= 2);
        Assert.True(exact.Select(static binding => binding!.VariableRowId).Distinct().Count() >= 2);
        Assert.All(exact, static binding => Assert.NotNull(binding));
        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int same", output);
        Assert.Contains("string same", output);
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.YieldTwo))]
    [InlineData(nameof(CfgSampleClass.YieldGrid))]
    public void ReconstructedIterator_PreservesImportedNameLoss(string method)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = RaiseWithImportLoss(source, typeof(CfgSampleClass).FullName!, method);

        Assert.NotEmpty(function.Descendants.OfType<YieldReturn>());
        AssertCrossMethodLoss(function);
        AssertLoss(function);
    }

    [Fact]
    public void ReconstructedIterator_PreservesSuccessfulLocalBindings()
    {
        using var source = MetadataSource.Open(typeof(PdbScopeFixtures).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(PdbScopeFixtures).FullName!,
            nameof(PdbScopeFixtures.IteratorScopes))!;

        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            member => IrImporter.Import(source, member)));

        var bindings = function.LocalDeclarationBindings
            .Where(static binding => binding?.Name == "same")
            .ToArray();
        Assert.True(bindings.Length >= 2);
        Assert.True(bindings.Select(static binding => binding!.VariableRowId).Distinct().Count() >= 2);
        Assert.All(bindings, static binding => Assert.NotNull(binding));
        Assert.All(bindings, binding =>
            Assert.True(function.IsLocalDeclaredInNestedScope(
                function.LocalDeclarationBindings.IndexOf(binding))));
        string text = CSharpPrinter.Print(function).Output!;
        Assert.Contains("int same", text);
        Assert.Contains("string same", text);
    }

    [Fact]
    public void ReconstructedClassicAsync_PreservesImportedNameLoss()
    {
        using var source = MetadataSource.Open(FixtureCatalog.DecompilerClassicAsync.AssemblyPath());
        var function = RaiseWithImportLoss(source,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.NamedAwaitResultSamples", "NamedReceiver");

        Assert.True(function.RequiresAsyncBodyModifier);
        Assert.Single(function.Descendants.OfType<AwaitExpression>());
        AssertCrossMethodLoss(function);
        AssertLoss(function);
    }

    [Fact]
    public void ReconstructedClassicAsync_PreservesSuccessfulLocalBinding()
    {
        using var source = MetadataSource.Open(FixtureCatalog.DecompilerClassicAsync.AssemblyPath());
        var function = IrImporter.Import(source,
            "ILInspector.Decompiler.Fixtures.ClassicAsync.NamedAwaitResultSamples", "NamedReceiver")!;

        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(
            member => IrImporter.Import(source, member)));

        var binding = Assert.Single(function.LocalDeclarationBindings);
        Assert.Equal("result", binding?.Name);
        Assert.Equal("result", Assert.Single(function.LocalNames));
    }

    static IrFunction RaiseWithImportLoss(MetadataSource source, string type, string method)
    {
        var function = IrImporter.Import(source, type, method)!;
        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(member =>
        {
            var nested = IrImporter.Import(source, member);
            if (nested is not null)
                nested.LocalNameImportCauses = [ImportLoss];
            return nested;
        }));
        function.CheckInvariant();
        return function;
    }

    static void AssertLoss(IrFunction function)
    {
        Assert.Contains(FidelityRemarks.CollectCauses(function),
            static cause => cause.Discriminator
                == DecompilerFidelityDiscriminators.ScopedLocalNameUnavailable);
        Assert.Equal(DecompilationFidelity.Partial, CSharpPrinter.Print(function).Fidelity);
    }

    static void AssertCrossMethodLoss(IrFunction function)
    {
        var cause = Assert.Single(function.LocalNameImportCauses,
            static cause => cause.Discriminator
                == DecompilerFidelityDiscriminators.ScopedLocalNameUnavailable);
        Assert.Equal(ImportLoss.Node, cause.Node);
        Assert.Equal(DecompilerFidelityLocationKind.Unknown, cause.Location.Kind);
    }
}
