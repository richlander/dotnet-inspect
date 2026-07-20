using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

using DotnetInspector.Fixtures;
using Microsoft.CodeAnalysis;
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
    internal const int ClassifiedDynamicCompilationSiteCount = 35;
    internal const string ClassifiedDynamicCompilationSiteSetFingerprint =
        "DF78DDF0E75F3F3B059A9578351A89024C1F052C4004B71AC3A67C18073FE97E";

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
                || !access.Expression.ToString().EndsWith("CSharpCompilation", StringComparison.Ordinal))
                continue;

            var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            string member = method is null ? "<top-level>" : MethodIdentity(method);
            string fingerprint = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(invocation.WithoutTrivia().ToFullString())))[..12];
            string identity = $"{member}@{invocation.SpanStart:X8}-{fingerprint}";
            int ordinal = memberOrdinals.TryGetValue(identity, out int count) ? count + 1 : 1;
            memberOrdinals[identity] = ordinal;
            sites.Add($"{relativePath}::{identity}#{ordinal}");
        }
        return sites;
    }

    internal static string ComputeSiteSetFingerprint(IEnumerable<string> sites)
    {
        string canonical = string.Join('\n', sites.Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    static string MethodIdentity(MethodDeclarationSyntax method)
    {
        string containingTypes = string.Join("+", method.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .Select(type => $"{type.Identifier.ValueText}`{type.TypeParameterList?.Parameters.Count ?? 0}"));
        string parameters = string.Join(",", method.ParameterList.Parameters.Select(parameter =>
            parameter.Type?.WithoutTrivia().ToString() ?? "?"));
        return $"{containingTypes}.{method.Identifier.ValueText}({parameters})";
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
