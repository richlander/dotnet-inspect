using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Freezes the accounting boundary for the signature-spellability decode.
///
/// The per-decode node and occurrence-copy budgets count callbacks and copies.
/// They cannot observe how much metadata an individual callback materializes,
/// so the decode also charges the storage it reads against a separate work
/// ledger. That protection is only as good as its weakest call site: a single
/// uncharged <c>GetString</c> or blob copy reintroduces unbounded, quadratic
/// amplification from a small file, which is how the shared-resolution-scope
/// hole was originally introduced.
///
/// Enforcing this by review does not work -- the scope projection was named as
/// a gap, believed fixed, and shipped uncharged anyway. This census makes the
/// rule structural instead: inside <c>SignatureOccurrenceProvider</c>, every
/// metadata materialization must occur inside a method that charges first. A
/// newly added uncharged read fails here rather than shipping.
///
/// Classification is performed on the Roslyn syntax tree rather than by string
/// or regex matching, so comments, line splits, and aliasing cannot evade it.
/// </summary>
public class SignatureSpellabilityBudgetBoundaryTests
{
    // Members that copy metadata storage into managed objects, or that
    // transitively do so. Reaching one of these without charging first is the
    // defect this gate exists to prevent.
    static readonly string[] MaterializingMembers =
    {
        "GetString",
        "GetBlobBytes",
        "GetBlobContent",
        "From",
    };

    // The only methods permitted to materialize. Each charges the storage it is
    // about to read against the ledger before reading it.
    static readonly string[] ChargedMethods =
    {
        "ProjectScope",
        "ModuleScope",
        "ChargeStorage",
    };

    [Fact]
    public void ProviderMaterializesMetadataOnlyInsideChargedMethods()
    {
        ClassDeclarationSyntax provider = Provider();
        var violations = new List<string>();
        foreach (MethodDeclarationSyntax method in
            provider.Members.OfType<MethodDeclarationSyntax>())
        {
            if (ChargedMethods.Contains(method.Identifier.ValueText))
            {
                continue;
            }

            foreach (InvocationExpressionSyntax invocation in
                method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax access
                    && MaterializingMembers.Contains(
                        access.Name.Identifier.ValueText))
                {
                    violations.Add(
                        $"{method.Identifier.ValueText} -> "
                        + access.Name.Identifier.ValueText);
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void ChargingMethodsChargeBeforeMaterializing()
    {
        ClassDeclarationSyntax provider = Provider();
        foreach (string name in new[] { "ProjectScope", "ModuleScope" })
        {
            MethodDeclarationSyntax method = Assert.Single(
                provider.Members.OfType<MethodDeclarationSyntax>(),
                candidate => candidate.Identifier.ValueText == name);
            Assert.Contains(
                method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation =>
                    invocation.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.ValueText == "ChargeStorage");
        }
    }

    static ClassDeclarationSyntax Provider()
    {
        string file = Path.Combine(
            FindRepoRoot(),
            "src",
            "ILInspector.Metadata",
            "SignatureSpellabilityAggregate.cs");
        Assert.True(File.Exists(file), file);
        CancellationToken token = TestContext.Current.CancellationToken;
        SyntaxNode root = CSharpSyntaxTree
            .ParseText(File.ReadAllText(file), cancellationToken: token)
            .GetRoot(token);
        return Assert.Single(
            root.DescendantNodes().OfType<ClassDeclarationSyntax>(),
            declaration =>
                declaration.Identifier.ValueText
                    == "SignatureOccurrenceProvider");
    }

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
