using System.Text;
using System.Text.Json;

using DotnetInspector.Fixtures;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

internal enum FixtureSourcePopulation
{
    Built,
    Generated,
    Dynamic,
}

internal sealed record DecompilerFixtureSourceRow(
    FixtureSourcePopulation Population,
    string Id,
    FixtureSourceApplicability Applicability,
    FixtureSourceInventoryStatus Status,
    int DocumentCount,
    int TargetCount,
    string? Reason);

internal sealed record DecompilerFixtureSourceReport(
    IReadOnlyList<DecompilerFixtureSourceRow> Rows)
{
    public int SourceDiscovered => Rows.Count(row =>
        row.Status == FixtureSourceInventoryStatus.SourceDiscovered);

    public int Unresolved => Rows.Count(row =>
        row.Status is FixtureSourceInventoryStatus.Unclassified
            or FixtureSourceInventoryStatus.SourceMissing);
}

internal static class DecompilerFixtureSourceInventory
{
    internal static IReadOnlySet<string> ClassifiedDynamicCompilationSites { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "src/ILInspector.Decompiler.Tests/CSharpPrinterReceiverTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/CatchEntryFoldingTests.cs::AssertCompiles#1",
            "src/ILInspector.Decompiler.Tests/CharElementStorePrinterTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/ClosureDiagnosticEvidenceTests.cs::Extract_IncludesObjectForCompleteInterfaceReceiverCompatibility#1",
            "src/ILInspector.Decompiler.Tests/ClosureDiagnosticEvidenceTests.cs::Extract_MarksNonNamedReceiverCompatibilityIncomplete#1",
            "src/ILInspector.Decompiler.Tests/ClosureDiagnosticEvidenceTests.cs::Extract_MarksReceiverCompatibilityIncompleteWhenBaseTypeIsUnresolved#1",
            "src/ILInspector.Decompiler.Tests/ClosureDiagnosticEvidenceTests.cs::Extract_ReportsReceiverBaseAndGenericInterfaceCompatibility#1",
            "src/ILInspector.Decompiler.Tests/ClosureDiagnosticEvidenceTests.cs::Extract_ReturnsNullForSupportedDiagnosticWithoutSourceLocation#1",
            "src/ILInspector.Decompiler.Tests/ClosureDiagnosticEvidenceTests.cs::Extract_UsesStructuredSyntaxAndSemanticEvidence#1",
            "src/ILInspector.Decompiler.Tests/CoerceChokePointTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/CompileBackTypeIdentityTests.cs::Compile#1",
            "src/ILInspector.Decompiler.Tests/CompilerFeatureOptionsTests.cs::Compile#1",
            "src/ILInspector.Decompiler.Tests/CrossAssemblyMethodFactsTests.cs::Emit#1",
            "src/ILInspector.Decompiler.Tests/DataflowFactsTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/DefaultParameterValidityTests.cs::CompileSignature#1",
            "src/ILInspector.Decompiler.Tests/EnumCastPrinterTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/FidelityCheckGeneratedFilterTests.cs::CompileFixture#1",
            "src/ILInspector.Decompiler.Tests/FidelityCheckGeneratedFilterTests.cs::ExtensionRootSelection_UsesArityAwareRoslynReceiverEvidence#1",
            "src/ILInspector.Decompiler.Tests/FinallyDisposePrinterTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/IrImporterTests.cs::AssertRefLocalBodyCompiles#1",
            "src/ILInspector.Decompiler.Tests/IteratorReconstructionPassTests.cs::CompileComplexIteratorFixture#1",
            "src/ILInspector.Decompiler.Tests/LadderRung6GateTests.cs::RecompileNewRules#1",
            "src/ILInspector.Decompiler.Tests/LadderRung9GateTests.cs::CompileExpressionTreeStatement#1",
            "src/ILInspector.Decompiler.Tests/MemberBodyProducerUnionTests.cs::Compile#1",
            "src/ILInspector.Decompiler.Tests/MemberNameCollisionRenderingTests.cs::AssertBodyCompiles#1",
            "src/ILInspector.Decompiler.Tests/MixedSignComparisonTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/MultiDimensionalArrayPrinterTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/NestedScopeNameCollisionTests.cs::AssertCompiles#1",
            "src/ILInspector.Decompiler.Tests/NonFiniteConstantPrinterTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/PrinterPrecedenceTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/ReturnToSenderFixtureCatalogTests.cs::CompileSourceFixture#1",
            "src/ILInspector.Decompiler.Tests/ReturnToSenderPrototypeTests.cs::CompileFixture#1",
            "src/ILInspector.Decompiler.Tests/TypeSourceCheckTests.cs::AssertCompiles#1",
            "src/ILInspector.Decompiler.Tests/UnsafeEmitterTests.cs::Recompile#1",
            "src/ILInspector.Decompiler.Tests/ValidityShellNoiseTests.cs::CreateCompilation#1",
        };

    public static DecompilerFixtureSourceReport Create()
    {
        var built = FixtureSourceInventory
            .Create(FixtureCatalog.SelectByTag("decompiler"))
            .Fixtures
            .Select(row => new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Built,
                row.FixtureId,
                row.Applicability,
                row.Status,
                row.DiscoveredDocumentCount,
                0,
                row.Reason));

        var generated = GeneratedFixtureCatalog.Catalog.Select(fixture =>
            new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Generated,
                fixture.Id,
                FixtureSourceApplicability.Required,
                string.IsNullOrWhiteSpace(fixture.Source)
                    ? FixtureSourceInventoryStatus.SourceMissing
                    : FixtureSourceInventoryStatus.SourceDiscovered,
                string.IsNullOrWhiteSpace(fixture.Source) ? 0 : 1,
                fixture.Targets.Count,
                string.IsNullOrWhiteSpace(fixture.Source)
                    ? "The generated fixture has no retained source."
                    : null));

        var dynamic = DiscoverDynamicCompilationSites().Select(site =>
            new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Dynamic,
                site,
                FixtureSourceApplicability.Required,
                FixtureSourceInventoryStatus.Unclassified,
                0,
                0,
                "Test-local compilation has not migrated to the source-retaining materializer."));

        return new([.. built, .. generated, .. dynamic]);
    }

    public static string Format(DecompilerFixtureSourceReport report, bool json)
    {
        if (json)
        {
            return JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
        }

        var output = new StringBuilder();
        output.AppendLine("# DECOMPILER FIXTURE SOURCE INVENTORY");
        output.AppendLine();
        output.AppendLine("| Population | Entries | Source discovered | Unresolved |");
        output.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var population in Enum.GetValues<FixtureSourcePopulation>())
        {
            var rows = report.Rows.Where(row => row.Population == population).ToArray();
            output.AppendLine($"| {population} | {rows.Length} | "
                + $"{rows.Count(row => row.Status == FixtureSourceInventoryStatus.SourceDiscovered)} | "
                + $"{rows.Count(row => row.Status is FixtureSourceInventoryStatus.Unclassified or FixtureSourceInventoryStatus.SourceMissing)} |");
        }
        return output.ToString();
    }

    static IReadOnlyList<string> DiscoverDynamicCompilationSites()
    {
        string root = RepositoryRoot();
        string testRoot = Path.Combine(root, "src", "ILInspector.Decompiler.Tests");
        var sites = new List<string>();
        foreach (string path in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            sites.AddRange(DiscoverCSharpCompilationSites(File.ReadAllText(path), relativePath));
        }
        return sites.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyList<string> DiscoverCSharpCompilationSites(
        string source,
        string relativePath)
    {
        var rootNode = CSharpSyntaxTree.ParseText(source).GetRoot();
        var memberOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var sites = new List<string>();
        foreach (var invocation in rootNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access
                || access.Name.Identifier.ValueText != "Create"
                || access.Expression.ToString() != "CSharpCompilation")
                continue;

            string member = invocation.Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.ValueText ?? "<top-level>";
            int ordinal = memberOrdinals.TryGetValue(member, out int count) ? count + 1 : 1;
            memberOrdinals[member] = ordinal;
            sites.Add($"{relativePath}::{member}#{ordinal}");
        }
        return sites;
    }

    static string RepositoryRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                    return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root for dynamic fixture inventory.");
    }
}
