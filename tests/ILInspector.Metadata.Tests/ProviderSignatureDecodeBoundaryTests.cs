using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Freezes the signature-provider closure contract for issue #2575 across both
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
/// aliasing, or spoofed tokens: an invocation is classified purely by its actual
/// receiver and enclosing guard expression.
/// </summary>
public class ProviderSignatureDecodeBoundaryTests
{
    static readonly string[] AssemblyRoots =
    {
        Path.Combine("src", "ILInspector.Metadata"),
        Path.Combine("src", "ILInspector.MetadataPrimitives"),
    };

    static readonly string[] DecodeMethodNames =
    {
        "DecodeSignature",
        "DecodeMethodSignature",
        "DecodeFieldSignature",
    };

    [Fact]
    public void EveryProviderDecode_IsGuarded()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            foreach (var invocation in DecodeInvocations(file))
            {
                // Sanctioned form 1: a nested cross-handle TypeSpec re-entry,
                // `reader.GetTypeSpecification(handle).Decode*Signature(...)`,
                // bounded by TypeSpecGuard in its enclosing provider file
                // (asserted by NestedTypeSpecReentry_IsBoundedByTypeSpecGuard).
                if (IsNestedTypeSpecReentry(invocation))
                    continue;

                // Sanctioned form 2: this decode is the true-branch of a
                // `SignatureBlobGuard.IsSafeToDecode(...) ? decode : fallback`
                // prescan ternary.
                if (IsPrescanGuardedTernary(invocation))
                    continue;

                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
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

    static IEnumerable<InvocationExpressionSyntax> DecodeInvocations(string file)
        => AllInvocations(file).Where(IsDecodeInvocation);

    static InvocationExpressionSyntax[] AllInvocations(string file)
    {
        var token = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: token);
        return tree.GetRoot(token).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
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
