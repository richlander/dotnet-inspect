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

    // Methods that resolve a scope by charging and then materializing. Both
    // requirements of the ordering gate below apply to each of them.
    static readonly string[] ChargingScopeMethods =
    {
        "ProjectScope",
        "ModuleScope",
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
        // Enumerating methods and inspecting their reads is the wrong
        // direction: it can only reject a read it happens to visit, so a read
        // that moves into a property, accessor, constructor, field
        // initializer, or local function is not judged at all -- it is simply
        // never seen, and absence of a violation reads as compliance.
        //
        // Enumerate the reads instead and demand that each one justify its
        // location. A read is permitted only when the member enclosing it is
        // one of the named charging methods; every other host, whatever its
        // kind, is a violation. Relocating a read out of a charging method now
        // fails the gate rather than escaping it.
        TypeDeclarationSyntax provider = Provider();
        var violations = new List<string>();
        foreach (InvocationExpressionSyntax invocation in
            provider.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access
                || !MaterializingMembers.Contains(
                    access.Name.Identifier.ValueText))
            {
                continue;
            }

            MemberDeclarationSyntax? host = invocation
                .Ancestors()
                .OfType<MemberDeclarationSyntax>()
                .FirstOrDefault();
            bool permitted = host is MethodDeclarationSyntax method
                && ChargedMethods.Contains(method.Identifier.ValueText);
            if (!permitted)
            {
                violations.Add(
                    $"{access.Name.Identifier.ValueText} in "
                    + $"{host?.Kind().ToString() ?? "<no member>"} "
                    + $"'{HostName(host)}'");
            }
        }

        Assert.Empty(violations);
    }

    static string HostName(MemberDeclarationSyntax? host) => host switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        ConstructorDeclarationSyntax constructor =>
            constructor.Identifier.ValueText,
        BaseFieldDeclarationSyntax field =>
            field.Declaration.Variables.First().Identifier.ValueText,
        _ => host?.ToString() ?? "<none>",
    };

    // Members of an AssemblyReference whose value is stored inline in the
    // table row rather than in a heap. Reading one copies no author-sized
    // storage, so it needs no charge. Every other member the identity
    // materializes must be charged; a newly added heap member fails the gate
    // below until it is either charged or declared here deliberately.
    static readonly string[] FixedWidthReferenceMembers =
    {
        "Version",
        "Flags",
    };

    [Fact]
    public void ScopeProjectionChargesEveryHeapMemberTheIdentityReads()
    {
        // ProjectScope charges, then hands the handle to
        // AssemblyReferenceIdentity, which performs the actual materialization
        // in another type. The deny-by-default census above cannot see across
        // that call, which is why ProjectScope is exempt from it -- and an
        // exemption checked only by "some ChargeStorage call exists somewhere
        // in the method" let a missing charge for one of three members ship
        // green.
        //
        // Derive the requirement from the consumer instead: whatever the
        // identity reads out of a heap, the projection must have charged. A
        // removed charge and a newly materialized member both fail here.
        var expected = new SortedSet<string>(
            MaterializedReferenceMembers()
                .Except(FixedWidthReferenceMembers));
        var charged = new SortedSet<string>(ChargedReferenceMembers());

        Assert.NotEmpty(expected);
        Assert.Equal(expected, charged);
    }

    [Fact]
    public void ChargesPrecedeMaterializationInEveryChargingMethod()
    {
        // Set equality alone does not price anything: charging after the work
        // is done still reads attacker-sized storage before the ledger can
        // refuse it. Ordering is the property, and it has to hold for the
        // delegating method too, not just the one that materializes inline.
        //
        // Two requirements, because materialization takes two shapes here.
        // ModuleScope names the handle it reads, so the charge naming that
        // same handle must come first. ProjectScope hands the handle to
        // AssemblyReferenceIdentity, which materializes out of sight, so the
        // handle is not a syntactic argument -- there the requirement is that
        // no materialization begins until every charge in the method is paid.
        var violations = new List<string>();
        foreach (string methodName in ChargingScopeMethods)
        {
            MethodDeclarationSyntax method = ProviderMethod(methodName);
            InvocationExpressionSyntax[] charges = method
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation =>
                    invocation.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.ValueText == "ChargeStorage"
                    && invocation.ArgumentList.Arguments.Count == 2)
                .ToArray();
            InvocationExpressionSyntax[] materializations = method
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation =>
                    invocation.Expression is MemberAccessExpressionSyntax access
                    && MaterializingMembers.Contains(
                        access.Name.Identifier.ValueText))
                .ToArray();

            // A method that charges but never materializes, or materializes but
            // never charges, means this gate is aimed at the wrong method.
            Assert.NotEmpty(charges);
            Assert.NotEmpty(materializations);

            foreach (InvocationExpressionSyntax materialization in
                materializations)
            {
                string description = materialization.Expression.ToString();
                foreach (InvocationExpressionSyntax charge in charges)
                {
                    if (charge.SpanStart > materialization.SpanStart)
                    {
                        violations.Add(
                            $"{methodName}: {description} materializes before "
                            + $"{charge.ArgumentList.Arguments[1].Expression} "
                            + "is charged");
                    }
                }

                if (materialization.ArgumentList.Arguments.Count != 1)
                {
                    continue;
                }

                string handle = materialization
                    .ArgumentList.Arguments[0].Expression.ToString();
                bool chargedFirst = charges.Any(charge =>
                    charge.ArgumentList.Arguments[1].Expression.ToString()
                        == handle
                    && charge.SpanStart < materialization.SpanStart);
                if (!chargedFirst)
                {
                    violations.Add(
                        $"{methodName}: {description}({handle}) has no earlier "
                        + "charge naming that handle");
                }
            }
        }

        Assert.Empty(violations);
    }

    // Every `reference.X` the identity touches while building itself from a
    // reader, taken from its source rather than restated here.
    static IEnumerable<string> MaterializedReferenceMembers()
    {
        TypeDeclarationSyntax identity = Declaration(
            "AssemblyReferenceIdentity",
            "AssemblyReferenceIdentity.cs");
        return identity
            .Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method =>
                method.Identifier.ValueText is "From" or "Create")
            .SelectMany(method =>
                method.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>())
            .Where(access =>
                access.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == "reference")
            .Select(access => access.Name.Identifier.ValueText)
            .Distinct();
    }

    static IEnumerable<string> ChargedReferenceMembers() =>
        ProviderMethod("ProjectScope")
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation =>
                invocation.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == "ChargeStorage"
                && invocation.ArgumentList.Arguments.Count == 2)
            .Select(invocation =>
                invocation.ArgumentList.Arguments[1].Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Where(access =>
                access.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == "reference")
            .Select(access => access.Name.Identifier.ValueText)
            .Distinct();

    static MethodDeclarationSyntax ProviderMethod(string name) =>
        Assert.Single(
            Provider().Members.OfType<MethodDeclarationSyntax>(),
            candidate => candidate.Identifier.ValueText == name);

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
        TypeDeclarationSyntax provider = Provider();
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
        TypeDeclarationSyntax provider = Provider();
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
        TypeDeclarationSyntax provider = Provider();
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

    static TypeDeclarationSyntax Provider() =>
        Declaration(
            "SignatureOccurrenceProvider",
            "SignatureSpellabilityAggregate.cs");

    static TypeDeclarationSyntax Declaration(string name, string fileName)
    {
        string file = Path.Combine(
            FindRepoRoot(),
            "src",
            "ILInspector.Metadata",
            fileName);
        Assert.True(File.Exists(file), file);
        CancellationToken token = TestContext.Current.CancellationToken;
        SyntaxNode root = CSharpSyntaxTree
            .ParseText(File.ReadAllText(file), cancellationToken: token)
            .GetRoot(token);
        return Assert.Single(
            root.DescendantNodes().OfType<TypeDeclarationSyntax>(),
            declaration => declaration.Identifier.ValueText == name);
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
