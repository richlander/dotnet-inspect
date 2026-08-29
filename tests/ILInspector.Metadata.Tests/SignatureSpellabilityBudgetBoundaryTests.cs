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

    // MetadataTypeDefinitionNameReader.Read walks a chain before it
    // materializes any segment, and that walk is charged work. There is exactly
    // one way to pay for it: hand the reader the chargeChain callback. Every
    // call site in the provider does so, including GetTypeFromReference, whose
    // own explicit ChargeMetadataWork pays for its *second* walk over the same
    // resolution scope rather than for the read's walk.
    //
    // The single rule matters as much as the charge. An earlier version of this
    // gate accepted either a callback or a bare ChargeMetadataWork call
    // somewhere in the method, and had to guess which route a site had taken.
    // Both guesses were bypassable: Read(reader, handle, null) satisfied an
    // argument-count test while charging nothing, and ChargeMetadataWork(0)
    // satisfied a method-wide existence test while removing the accounting.
    // This gate therefore checks the argument itself.
    [Fact]
    public void NameReadsChargeTheChainTheyWalk()
    {
        ClassDeclarationSyntax provider = Provider();
        var reads = new List<(string Method, bool ChargesChain)>();
        foreach (MethodDeclarationSyntax method in
            provider.Members.OfType<MethodDeclarationSyntax>())
        {
            foreach (InvocationExpressionSyntax invocation in
                method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax access
                    || access.Name.Identifier.ValueText != "Read"
                    || access.Expression is not IdentifierNameSyntax reader
                    || reader.Identifier.ValueText
                        != "MetadataTypeDefinitionNameReader")
                {
                    continue;
                }

                // Only the field itself counts. A null literal, a default
                // expression, or any other constant is not a charge, however
                // it is spelled or positioned.
                bool chargesChain = invocation.ArgumentList.Arguments.Any(
                    argument =>
                        argument.Expression is IdentifierNameSyntax charge
                        && charge.Identifier.ValueText == "_chargeChainWalk");

                reads.Add((method.Identifier.ValueText, chargesChain));
            }
        }

        // A gate that matches nothing passes for the wrong reason. Both read
        // sites must actually be found before the charge assertion means
        // anything.
        Assert.Equal(2, reads.Count);
        Assert.Contains(reads, read => read.Method == "GetTypeFromDefinition");
        Assert.Contains(reads, read => read.Method == "GetTypeFromReference");

        string[] unpriced = reads
            .Where(read => !read.ChargesChain)
            .Select(read => read.Method)
            .ToArray();
        Assert.Empty(unpriced);

        // GetTypeFromReference walks the resolution scope a second time to
        // recover the terminal, and charges that walk itself. Asserting only
        // that some ChargeMetadataWork call exists lets the accounting be
        // replaced by ChargeMetadataWork(0), so bind the charge to the length
        // the traversal produced.
        MethodDeclarationSyntax fromReference = Assert.Single(
            provider.Members.OfType<MethodDeclarationSyntax>(),
            candidate =>
                candidate.Identifier.ValueText == "GetTypeFromReference");
        Assert.Contains(
            fromReference.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation =>
                invocation.Expression is MemberAccessExpressionSyntax charge
                && charge.Name.Identifier.ValueText == "ChargeMetadataWork"
                && invocation.ArgumentList.Arguments.Count == 1
                && invocation.ArgumentList.Arguments[0].Expression
                    is IdentifierNameSyntax length
                && length.Identifier.ValueText == "chainLength");
    }

    // Every charge above is routed through one delegate, so the delegate's own
    // body is the single point where all of them can be silently zeroed.
    // Gating only the call sites leaves that point unguarded: rewriting the
    // lambda to ChargeMetadataWork(0) keeps every call site spelled correctly
    // while charging nothing anywhere. The lambda's parameter must reach the
    // charge.
    [Fact]
    public void ChainChargeDelegateChargesItsOwnArgument()
    {
        ClassDeclarationSyntax provider = Provider();
        SimpleLambdaExpressionSyntax lambda = Assert.Single(
            provider
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment =>
                    assignment.Left is IdentifierNameSyntax target
                    && target.Identifier.ValueText == "_chargeChainWalk")
                .Select(assignment => assignment.Right)
                .OfType<SimpleLambdaExpressionSyntax>());

        string parameter = lambda.Parameter.Identifier.ValueText;
        Assert.Contains(
            lambda.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            invocation =>
                invocation.Expression is MemberAccessExpressionSyntax charge
                && charge.Name.Identifier.ValueText == "ChargeMetadataWork"
                && invocation.ArgumentList.Arguments.Count == 1
                && invocation.ArgumentList.Arguments[0].Expression
                    is IdentifierNameSyntax argument
                && argument.Identifier.ValueText == parameter);
    }

    // The chargeChain callback must stay distinct from beforeMaterialize.
    // MetadataTypeNameBudget.TryRead invokes beforeMaterialize once per name
    // component with that component's UTF-8 length, so a provider that passed
    // its chain charge as beforeMaterialize would also pay for every name --
    // and pay again through ChargeName, which charges the decoded characters.
    [Fact]
    public void ChainChargeIsNotPassedAsTheMaterializationHook()
    {
        ClassDeclarationSyntax provider = Provider();
        foreach (InvocationExpressionSyntax invocation in provider
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access
                || access.Name.Identifier.ValueText != "Read"
                || access.Expression is not IdentifierNameSyntax reader
                || reader.Identifier.ValueText
                    != "MetadataTypeDefinitionNameReader")
            {
                continue;
            }

            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is not IdentifierNameSyntax charge
                    || charge.Identifier.ValueText != "_chargeChainWalk")
                {
                    continue;
                }

                Assert.True(
                    argument.NameColon?.Name.Identifier.ValueText
                        == "chargeChain",
                    "The chain charge must be passed as the chargeChain "
                        + "argument. Passing it positionally or as "
                        + "beforeMaterialize also charges every name "
                        + "component's UTF-8 length, which ChargeName then "
                        + "charges a second time.");
            }
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
