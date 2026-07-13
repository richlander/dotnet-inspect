using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Freezes the signature-provider closure contract for issue #2575 across the
/// SRM-only assemblies. SRM's <c>DecodeSignature</c> recurses on the native
/// stack for every nested element <em>before</em> the first provider callback,
/// so a single over-deep blob overflows the stack in a way no managed
/// <c>try/catch</c> can contain. Every top-level provider decode must therefore
/// be prescanned with <c>SignatureBlobGuard.IsSafeToDecode</c>, and every nested
/// cross-handle TypeSpec re-entry must be bounded by <c>TypeSpecGuard</c>.
///
/// This census is a deny-list: any <c>Decode*Signature</c> invocation that is
/// not one of the two sanctioned guarded forms is a violation. A newly added
/// provider, an un-gated site, or a decode routed through a local alias fails
/// this test rather than shipping an unguarded same-mechanism hole. This is the
/// anti-ratchet closure proof.
///
/// The classification is performed on the Roslyn syntax tree, not by string or
/// regex matching, so it cannot be evaded by comments, line splits, whitespace,
/// aliasing, or spoofed tokens. It enumerates every occurrence of a decode
/// method name and requires each to be the invoked member of a sanctioned call,
/// so null-conditional (`x?.Decode...`), method-group, and delegate forms are
/// flagged rather than skipped. Form 2 additionally binds the prescan to the
/// decoded blob (same receiver's `.Signature`), so an unrelated or nil-handle
/// guard cannot launder an unguarded decode past the census.
/// </summary>
public class ProviderSignatureDecodeBoundaryTests
{
    static readonly string[] AssemblyRoots =
    {
        Path.Combine("src", "ILInspector.Metadata"),
        Path.Combine("src", "ILInspector.MetadataPrimitives"),
        Path.Combine("src", "ILInspector.Instructions"),
    };

    // The complete set of SRM top-level signature-blob decoders that recurse on
    // the native stack (once per nested element, before the first provider
    // callback) and can therefore StackOverflow uncatchably on an over-deep
    // blob. This is a denylist of dangerous entry points, so names the product
    // does not currently call are still listed to freeze the anti-ratchet gate.
    // Lower-level sub-decoders (DecodeType, DecodeTypeSequence) are reached only
    // from within one of these and are covered transitively.
    static readonly string[] DecodeMethodNames =
    {
        "DecodeSignature",
        "DecodeMethodSignature",
        "DecodeFieldSignature",
        "DecodeLocalSignature",
        "DecodeMethodSpecificationSignature",
    };

    [Fact]
    public void EveryProviderDecode_IsGuarded()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            foreach (var name in DecodeNameOccurrences(file))
            {
                // A decode method name may appear ONLY as the invoked method of a
                // sanctioned decode call. Any other occurrence — a method-group /
                // delegate reference, or a call whose name node is not the invoked
                // member — is an unguarded escape and a violation.
                if (TryGetInvokedDecode(name, out var invocation)
                    && (IsNestedTypeSpecReentry(invocation) || IsPrescanGuardedTernary(invocation)))
                {
                    // Sanctioned form 1: a nested cross-handle TypeSpec re-entry,
                    // `reader.GetTypeSpecification(handle).Decode*Signature(...)`,
                    // bounded by TypeSpecGuard in its enclosing provider file
                    // (asserted by NestedTypeSpecReentry_IsBoundedByTypeSpecGuard).
                    // Sanctioned form 2: the decode is the true-branch of a
                    // `SignatureBlobGuard.IsSafeToDecode(reader, <recv>.Signature,
                    // ...) ? decode : fallback` prescan ternary.
                    continue;
                }

                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                violations.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every top-level provider signature decode must be the true-branch of a "
            + "SignatureBlobGuard.IsSafeToDecode prescan ternary or a "
            + "TypeSpecGuard-bounded nested GetTypeSpecification re-entry. "
            + "Unguarded raw decodes:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void NestedTypeSpecReentry_IsBoundedByTypeSpecGuard()
    {
        var root = FindRepoRoot();
        var unguarded = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var invocations = AllInvocations(file);

            bool hasNestedReentry = invocations.Any(i => IsDecodeInvocation(i) && IsNestedTypeSpecReentry(i));
            if (!hasNestedReentry)
                continue;

            bool boundsWithTypeSpecGuard = invocations.Any(i =>
                i.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.Text == "TryEnter"
                && member.Expression is IdentifierNameSyntax id
                && id.Identifier.Text == "TypeSpecGuard");

            if (!boundsWithTypeSpecGuard)
                unguarded.Add(Path.GetRelativePath(root, file));
        }

        Assert.True(
            unguarded.Count == 0,
            "Every file that re-enters a nested TypeSpec decode must bound it with "
            + "TypeSpecGuard.TryEnter:\n  " + string.Join("\n  ", unguarded));
    }

    static IEnumerable<SimpleNameSyntax> DecodeNameOccurrences(string file)
        => ParseRoot(file).DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Where(name => DecodeMethodNames.Contains(name.Identifier.Text));

    static InvocationExpressionSyntax[] AllInvocations(string file)
        => ParseRoot(file).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();

    static SyntaxNode ParseRoot(string file)
    {
        var token = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: token);
        return tree.GetRoot(token);
    }

    // Resolves a decode-method name occurrence to the invocation that invokes it,
    // handling both `x.Decode*Signature(...)` (MemberAccess) and
    // `x?.Decode*Signature(...)` (MemberBinding). Returns false when the name is
    // not the invoked member — e.g. a method-group / delegate reference — which
    // is itself a violation.
    static bool TryGetInvokedDecode(SimpleNameSyntax name, out InvocationExpressionSyntax invocation)
    {
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax member
                when member.Name == name
                && member.Parent is InvocationExpressionSyntax invoked
                && invoked.Expression == member:
                invocation = invoked;
                return true;
            case MemberBindingExpressionSyntax binding
                when binding.Name == name
                && binding.Parent is InvocationExpressionSyntax invoked
                && invoked.Expression == binding:
                invocation = invoked;
                return true;
            default:
                invocation = null!;
                return false;
        }
    }

    static bool IsDecodeInvocation(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax member
            && DecodeMethodNames.Contains(member.Name.Identifier.Text);

    // `<expr>.GetTypeSpecification(...).Decode*Signature(...)`: the decode's
    // immediate receiver is itself a GetTypeSpecification invocation.
    static bool IsNestedTypeSpecReentry(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax member
            && member.Expression is InvocationExpressionSyntax receiver
            && receiver.Expression is MemberAccessExpressionSyntax receiverMember
            && receiverMember.Name.Identifier.Text == "GetTypeSpecification";

    // `SignatureBlobGuard.IsSafeToDecode(reader, <recv>.Signature, ...) ?
    // <recv>.Decode*Signature(...) : <fallback>`: some enclosing conditional
    // guards this decode in its true-branch with a prescan of the SAME
    // receiver's signature blob. Binding the guard to the decoded blob prevents
    // laundering an unguarded decode past the census behind an unrelated or
    // nil-handle IsSafeToDecode call.
    static bool IsPrescanGuardedTernary(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax decodeMember)
            return false;
        var decodeReceiver = decodeMember.Expression.ToString();

        foreach (var ancestor in invocation.Ancestors())
        {
            if (ancestor is ConditionalExpressionSyntax conditional
                && conditional.WhenTrue.Span.Contains(invocation.Span)
                && ConditionGuardsReceiverSignature(conditional.Condition, decodeReceiver))
            {
                return true;
            }
        }
        return false;
    }

    static bool ConditionGuardsReceiverSignature(ExpressionSyntax condition, string decodeReceiver)
        => condition.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(i => i.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.Text == "IsSafeToDecode"
                && member.Expression is IdentifierNameSyntax id
                && id.Identifier.Text == "SignatureBlobGuard"
                && i.ArgumentList.Arguments.Any(a =>
                    a.Expression is MemberAccessExpressionSyntax blob
                    && blob.Name.Identifier.Text == "Signature"
                    && blob.Expression.ToString() == decodeReceiver));

    static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        foreach (var assemblyRoot in AssemblyRoots)
        {
            var dir = Path.Combine(root, assemblyRoot);
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                yield return file;
        }
    }

    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
