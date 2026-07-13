using System.Reflection;

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
/// be prescanned with <c>SignatureBlobGuard.IsSafeToDecode</c> or enter through
/// <c>GuardedSignatureDecoder</c>, and every nested cross-handle TypeSpec
/// re-entry must be bounded by the disposable scope returned for that invocation
/// by <c>TypeSpecGuard</c>.
///
/// This census is a deny-list: any <c>Decode*Signature</c> invocation that is
/// not one of the three sanctioned guarded forms is a violation. A newly added
/// provider, an un-gated site, or a decode routed through a local alias fails
/// this test rather than shipping an unguarded same-mechanism hole. This is the
/// anti-ratchet closure proof.
///
/// The classification is performed on the Roslyn syntax tree, not by string or
/// regex matching, so it cannot be evaded by comments, line splits, whitespace,
/// aliasing, or spoofed tokens. Negative self-tests freeze those three reviewer
/// evasions. The census enumerates every occurrence of a decode
/// method name and requires each to be the invoked member of a sanctioned call,
/// so null-conditional (`x?.Decode...`), method-group, and delegate forms are
/// flagged rather than skipped. Form 2 additionally binds the prescan to the
/// decoded blob (same receiver's `.Signature`), so an unrelated or nil-handle
/// guard cannot launder an unguarded decode past the census. Nested TypeSpec
/// re-entry is likewise bound to a matching TryEnter handle and disposable
/// scope per invocation; a guard elsewhere in the file proves nothing.
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

    public static TheoryData<string, string> CensusEvasions => new()
    {
        {
            "provider local alias",
            """
            object Decode()
            {
                var provider = TypeNodeProvider.Instance;
                return method.DecodeSignature(provider, context);
            }
            """
        },
        {
            "newline-split invocation",
            """
            object Decode()
            {
                return method.
                    DecodeSignature(TypeNodeProvider.Instance, context);
            }
            """
        },
        {
            "guard token in comment",
            """
            object Decode()
            {
                // SignatureBlobGuard.IsSafeToDecode(reader, method.Signature)
                return method.DecodeSignature(TypeNodeProvider.Instance, context);
            }
            """
        },
        {
            "verbatim decode identifier",
            """
            object Decode()
            {
                return method.@DecodeSignature(TypeNodeProvider.Instance, context);
            }
            """
        },
        {
            "unicode-escaped decode identifier",
            """
            object Decode()
            {
                return method.\u0044ecodeSignature(TypeNodeProvider.Instance, context);
            }
            """
        },
        {
            "short-circuited prescan",
            """
            object Decode()
            {
                return SignatureBlobGuard.IsSafeToDecode(
                        reader,
                        method.Signature,
                        SignatureBlobGuard.Kind.Method) || true
                    ? method.DecodeSignature(TypeNodeProvider.Instance, context)
                    : fallback;
            }
            """
        },
    };

    [Fact]
    public void EveryProviderDecode_IsGuarded()
    {
        var root = FindRepoRoot();
        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            foreach (var name in FindDecodeViolations(ParseFileRoot(file)))
            {
                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                violations.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every top-level provider signature decode must be the true-branch of a "
            + "SignatureBlobGuard.IsSafeToDecode prescan ternary, the value factory "
            + "of a matching GuardedSignatureDecoder.Decode call, or a "
            + "TypeSpecGuard-bounded nested GetTypeSpecification re-entry. "
            + "Unguarded raw decodes:\n  " + string.Join("\n  ", violations));
    }

    [Theory]
    [MemberData(nameof(CensusEvasions))]
    public void Census_RejectsReviewerEvasion(string evasion, string source)
    {
        var violations = FindDecodeViolations(ParseSourceRoot(source));

        Assert.True(violations.Count == 1, $"{evasion}: expected one violation, found {violations.Count}");
    }

    [Fact]
    public void NestedTypeSpecReentry_IsBoundedPerInvocation()
    {
        var root = FindRepoRoot();
        var unguarded = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            foreach (var invocation in AllInvocations(ParseFileRoot(file))
                         .Where(i => IsDecodeInvocation(i)
                             && IsNestedTypeSpecReentry(i)
                             && !IsBoundedByTypeSpecGuardScope(i)))
            {
                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                unguarded.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(
            unguarded.Count == 0,
            "Every nested TypeSpec decode must be inside the disposable scope from "
            + "a matching TypeSpecGuard.TryEnter invocation:\n  "
            + string.Join("\n  ", unguarded));
    }

    [Fact]
    public void NestedTypeSpecFact_RejectsGuardElsewhereInFile()
    {
        var root = ParseSourceRoot("""
            object Guarded()
            {
                if (!TypeSpecGuard.TryEnter(reader, first, out var scope))
                    return null;
                using (scope)
                {
                    return reader.GetTypeSpecification(first).DecodeSignature(provider, context);
                }
            }

            object Unguarded()
                => reader.GetTypeSpecification(second).DecodeSignature(provider, context);
            """);

        var violation = Assert.Single(FindDecodeViolations(root));
        Assert.Equal("DecodeSignature", violation.Identifier.Text);
        Assert.Contains("second", violation.Parent!.Parent!.ToString());
    }

    [Fact]
    public void TypeSpecGuard_ExposesDisposableScopeInsteadOfUnpairedExit()
    {
        Assert.Null(typeof(TypeSpecGuard).GetMethod(
            "Exit",
            BindingFlags.Public | BindingFlags.Static));

        var tryEnter = Assert.Single(
            typeof(TypeSpecGuard).GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == nameof(TypeSpecGuard.TryEnter));
        Assert.Equal(
            typeof(TypeSpecGuard.Scope).MakeByRefType(),
            tryEnter.GetParameters()[2].ParameterType);
        Assert.NotNull(typeof(TypeSpecGuard.Scope).GetMethod(
            nameof(TypeSpecGuard.Scope.Dispose),
            BindingFlags.Public | BindingFlags.Instance));
    }

    static IReadOnlyList<SimpleNameSyntax> FindDecodeViolations(SyntaxNode root)
        => DecodeNameOccurrences(root)
            .Where(name => !TryGetInvokedDecode(name, out var invocation)
                || !(IsPrescanGuardedTernary(invocation)
                    || IsGuardedSignatureDecoderGateway(invocation)
                    || (IsNestedTypeSpecReentry(invocation)
                        && IsBoundedByTypeSpecGuardScope(invocation))))
            .ToArray();

    static IEnumerable<SimpleNameSyntax> DecodeNameOccurrences(SyntaxNode root)
        => root.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Where(name => DecodeMethodNames.Contains(name.Identifier.ValueText));

    static InvocationExpressionSyntax[] AllInvocations(SyntaxNode root)
        => root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();

    static SyntaxNode ParseFileRoot(string file)
    {
        var token = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: token);
        return tree.GetRoot(token);
    }

    static SyntaxNode ParseSourceRoot(string source)
    {
        var token = TestContext.Current.CancellationToken;
        return CSharpSyntaxTree.ParseText(source, cancellationToken: token).GetRoot(token);
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
            && DecodeMethodNames.Contains(member.Name.Identifier.ValueText);

    // `<expr>.GetTypeSpecification(...).Decode*Signature(...)`: the decode's
    // immediate receiver is itself a GetTypeSpecification invocation.
    static bool IsNestedTypeSpecReentry(InvocationExpressionSyntax invocation)
        => TryGetNestedTypeSpecReceiver(invocation, out _);

    static bool TryGetNestedTypeSpecReceiver(
        InvocationExpressionSyntax invocation,
        out InvocationExpressionSyntax receiver)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax member
            && member.Expression is InvocationExpressionSyntax typeSpecReceiver
            && typeSpecReceiver.Expression is MemberAccessExpressionSyntax receiverMember
            && receiverMember.Name.Identifier.ValueText == "GetTypeSpecification")
        {
            receiver = typeSpecReceiver;
            return true;
        }

        receiver = null!;
        return false;
    }

    static bool IsBoundedByTypeSpecGuardScope(InvocationExpressionSyntax invocation)
    {
        if (!TryGetNestedTypeSpecReceiver(invocation, out var receiver)
            || receiver.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var handle = receiver.ArgumentList.Arguments[0].Expression;
        foreach (var usingStatement in invocation.Ancestors().OfType<UsingStatementSyntax>())
        {
            if (usingStatement.Expression is not IdentifierNameSyntax scope
                || usingStatement.Parent is not BlockSyntax block)
            {
                continue;
            }

            int usingIndex = block.Statements.IndexOf(usingStatement);
            if (usingIndex <= 0
                || block.Statements[usingIndex - 1] is not IfStatementSyntax guard
                || !GuardReturnsWhenEntryFails(guard, scope.Identifier.ValueText, handle))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    static bool GuardReturnsWhenEntryFails(
        IfStatementSyntax guard,
        string scopeName,
        ExpressionSyntax handle)
    {
        var condition = UnwrapParentheses(guard.Condition);
        if (condition is not PrefixUnaryExpressionSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.ExclamationToken
            } negation
            || UnwrapParentheses(negation.Operand) is not InvocationExpressionSyntax tryEnter
            || tryEnter.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "TypeSpecGuard" },
                Name.Identifier.ValueText: "TryEnter",
            }
            || tryEnter.ArgumentList.Arguments.Count < 3
            || !SyntaxFactory.AreEquivalent(
                tryEnter.ArgumentList.Arguments[1].Expression,
                handle)
            || tryEnter.ArgumentList.Arguments[2].Expression is not DeclarationExpressionSyntax
            {
                Designation: SingleVariableDesignationSyntax designation,
            }
            || designation.Identifier.ValueText != scopeName)
        {
            return false;
        }

        return guard.Statement switch
        {
            ReturnStatementSyntax or ThrowStatementSyntax => true,
            BlockSyntax block when block.Statements.LastOrDefault()
                is ReturnStatementSyntax or ThrowStatementSyntax => true,
            _ => false,
        };
    }

    static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
            expression = parentheses.Expression;
        return expression;
    }

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
        var decodeReceiver = decodeMember.Expression;

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

    static bool ConditionGuardsReceiverSignature(
        ExpressionSyntax condition,
        ExpressionSyntax decodeReceiver)
    {
        condition = UnwrapParentheses(condition);
        if (condition is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.LogicalAndExpression))
        {
            return ConditionGuardsReceiverSignature(binary.Left, decodeReceiver)
                || ConditionGuardsReceiverSignature(binary.Right, decodeReceiver);
        }

        return condition is InvocationExpressionSyntax i
            && i.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText == "IsSafeToDecode"
                && member.Expression is IdentifierNameSyntax id
                && id.Identifier.ValueText == "SignatureBlobGuard"
                && i.ArgumentList.Arguments.Any(a =>
                    a.Expression is MemberAccessExpressionSyntax blob
                    && blob.Name.Identifier.ValueText == "Signature"
                    && SyntaxFactory.AreEquivalent(blob.Expression, decodeReceiver));
    }

    // `GuardedSignatureDecoder.Decode(reader, <recv>.Signature, ..., () =>
    // <recv>.Decode*Signature(...))`: the decode must be inside the supplied
    // value factory and the gateway must prescan the same receiver's blob.
    static bool IsGuardedSignatureDecoderGateway(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax decodeMember)
            return false;
        var decodeReceiver = decodeMember.Expression;

        var valueFactory = invocation.Ancestors()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault();
        if (valueFactory?.Parent is not ArgumentSyntax valueArgument
            || valueArgument.Parent?.Parent is not InvocationExpressionSyntax gateway
            || gateway.Expression is not MemberAccessExpressionSyntax gatewayMember
            || gatewayMember.Name.Identifier.ValueText != "Decode"
            || gatewayMember.Expression is not IdentifierNameSyntax gatewayType
            || gatewayType.Identifier.ValueText != "GuardedSignatureDecoder")
        {
            return false;
        }

        return gateway.ArgumentList.Arguments.Any(a =>
            a.Expression is MemberAccessExpressionSyntax blob
            && blob.Name.Identifier.ValueText == "Signature"
            && SyntaxFactory.AreEquivalent(blob.Expression, decodeReceiver));
    }

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
