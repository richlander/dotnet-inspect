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
/// not one of the sanctioned guarded forms is a violation. A newly added
/// provider, an un-gated site, or a decode routed through a local alias fails
/// this test rather than shipping an unguarded same-mechanism hole. This is the
/// anti-ratchet closure proof.
///
/// The classification is performed on the Roslyn syntax tree, not by string or
/// regex matching, so it cannot be evaded by comments, line splits, whitespace,
/// aliasing, or spoofed tokens. It enumerates every occurrence of a decode
/// method name and requires each to be the invoked member of a sanctioned call,
/// so null-conditional (`x?.Decode...`), method-group, and delegate forms are
/// flagged rather than skipped. Prescan forms bind the guard to the decoded
/// blob (same receiver's `.Signature`), TypeSpec forms bind the disposable
/// scope to the decoded handle, and the two TypeRefDecoder implementations
/// must retain their depth/cumulative counter pattern.
/// </summary>
public class ProviderSignatureDecodeBoundaryTests
{
    static readonly string[] AssemblyRoots =
    {
        Path.Combine("src", "ILInspector.Metadata"),
        Path.Combine("src", "ILInspector.MetadataPrimitives"),
        Path.Combine("src", "ILInspector.Instructions"),
        Path.Combine("src", "ILInspector.Analysis"),
        Path.Combine("src", "ILInspector.Decompiler"),
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
            foreach (var name in UnsafeDecodeOccurrences(ParseRoot(file)))
            {
                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                violations.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every provider signature decode must be bound to its receiver's "
            + "SignatureBlobGuard prescan, a disposable TypeSpecGuard scope, or "
            + "the bounded TypeRefDecoder counter pattern. "
            + "Unguarded raw decodes:\n  " + string.Join("\n  ", violations));
    }

    [Fact]
    public void NestedTypeSpecReentry_IsBoundedByTypeSpecGuard()
    {
        var root = FindRepoRoot();
        var unguarded = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            foreach (var invocation in ParseRoot(file).DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(i => IsDecodeInvocation(i) && IsNestedTypeSpecReentry(i)))
            {
                if (IsBoundedByTypeSpecGuard(invocation)
                    || IsDepthAndCumulativeBoundedTypeSpecDecode(invocation))
                    continue;

                var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                unguarded.Add($"{Path.GetRelativePath(root, file)}:{line}");
            }
        }

        Assert.True(
            unguarded.Count == 0,
            "Every nested TypeSpec decode invocation must be enclosed by the disposable "
            + "scope returned from its own TypeSpecGuard.TryEnter call:\n  "
            + string.Join("\n  ", unguarded));
    }

    [Theory]
    [MemberData(nameof(ReviewerEvasions))]
    public void ReviewerEvasion_IsRejected(string _, string source)
    {
        var violations = UnsafeDecodeOccurrences(ParseSource(source)).ToArray();

        Assert.NotEmpty(violations);
    }

    public static TheoryData<string, string> ReviewerEvasions => new()
    {
        {
            "gateway file",
            """
            class C
            {
                object M() => gateway.DecodeSignature(provider, context);
            }
            """
        },
        {
            "local alias",
            """
            class C
            {
                object M()
                {
                    var alias = value;
                    return SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind)
                        ? alias.DecodeSignature(provider, context)
                        : fallback;
                }
            }
            """
        },
        {
            "newline and comment spoof",
            """
            class C
            {
                object M() => value
                    /* SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind) */
                    .DecodeSignature(provider, context);
            }
            """
        },
        {
            "nil handle guard",
            """
            class C
            {
                object M() => SignatureBlobGuard.IsSafeToDecode(reader, default, kind)
                    ? value.DecodeSignature(provider, context)
                    : fallback;
            }
            """
        },
        {
            "unrelated blob guard",
            """
            class C
            {
                object M() => SignatureBlobGuard.IsSafeToDecode(reader, other.Signature, kind)
                    ? value.DecodeSignature(provider, context)
                    : fallback;
            }
            """
        },
        {
            "disjunctive blob guard",
            """
            class C
            {
                object M() =>
                    (SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind) || alwaysTrue)
                        ? value.DecodeSignature(provider, context)
                        : fallback;
            }
            """
        },
        {
            "null conditional",
            """
            class C
            {
                object M() => SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind)
                    ? value?.DecodeSignature(provider, context)
                    : fallback;
            }
            """
        },
        {
            "method group",
            """
            class C
            {
                object M()
                {
                    var decode = value.DecodeSignature;
                    return decode(provider, context);
                }
            }
            """
        },
        {
            "lambda deferred decode",
            """
            class C
            {
                object M()
                {
                    var decode = () => value.DecodeSignature(provider, context);
                    return decode();
                }
            }
            """
        },
        {
            "prescan deferred lambda",
            """
            class C
            {
                object M() => SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind)
                    ? (() => value.DecodeSignature(provider, context))
                    : fallback;
            }
            """
        },
        {
            "new decoder entry point",
            """
            class C
            {
                object M() => value.DecodeLocalSignature(provider, context);
            }
            """
        },
        {
            "file-granular TypeSpec guard",
            """
            class C
            {
                object GetTypeFromSpecification()
                {
                    TypeSpecGuard.TryEnter(reader, otherHandle, out var unrelated);
                    return reader.GetTypeSpecification(handle).DecodeSignature(provider, context);
                }
            }
            """
        },
        {
            "TypeSpec local alias with prescan only",
            """
            class C
            {
                object GetTypeFromSpecification()
                {
                    var spec = reader.GetTypeSpecification(handle);
                    return SignatureBlobGuard.IsSafeToDecode(reader, spec.Signature, kind)
                        ? spec.DecodeSignature(provider, context)
                        : fallback;
                }
            }
            """
        },
        {
            "TypeSpec double local alias with prescan only",
            """
            class C
            {
                object GetTypeFromSpecification()
                {
                    var spec = reader.GetTypeSpecification(handle);
                    var alias = spec;
                    return SignatureBlobGuard.IsSafeToDecode(reader, alias.Signature, kind)
                        ? alias.DecodeSignature(provider, context)
                        : fallback;
                }
            }
            """
        },
        {
            "TypeSpec deferred local function",
            """
            class C
            {
                object GetTypeFromSpecification()
                {
                    if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                        return fallback;
                    using (scope)
                    {
                        object Decode() =>
                            reader.GetTypeSpecification(handle).DecodeSignature(provider, context);
                        return Decode;
                    }
                }
            }
            """
        },
        {
            "mismatched TypeSpec handle",
            """
            class C
            {
                object GetTypeFromSpecification()
                {
                    if (!TypeSpecGuard.TryEnter(reader, otherHandle, out var scope))
                        return fallback;
                    using (scope)
                    {
                        return reader.GetTypeSpecification(handle).DecodeSignature(provider, context);
                    }
                }
            }
            """
        },
        {
            "mismatched TypeSpec reader",
            """
            class C
            {
                object GetTypeFromSpecification()
                {
                    if (!TypeSpecGuard.TryEnter(otherReader, handle, out var scope))
                        return fallback;
                    using (scope)
                    {
                        return reader.GetTypeSpecification(handle).DecodeSignature(provider, context);
                    }
                }
            }
            """
        },
        {
            "TypeSpec scope entered before an unscoped return",
            """
            class C
            {
                object GetTypeFromSpecification()
                {
                    if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                        return fallback;
                    if (skip)
                        return fallback;
                    using (scope)
                    {
                        return reader.GetTypeSpecification(handle).DecodeSignature(provider, context);
                    }
                }
            }
            """
        },
        {
            "TypeRefDecoder with constant blob length",
            """
            class C
            {
                int s_recursionDepth;
                int s_cumulativeSignatureBytes;
                object GetTypeFromSpecification()
                {
                    if (s_recursionDepth >= MaxRecursionDepth)
                        return fallback;
                    var spec = reader.GetTypeSpecification(handle);
                    int blobLength = 0;
                    if (blobLength > MaxSignatureBlobLength)
                        return fallback;
                    if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
                        return fallback;
                    s_cumulativeSignatureBytes += blobLength;
                    s_recursionDepth++;
                    try
                    {
                        return spec.DecodeSignature(provider, context);
                    }
                    finally
                    {
                        s_recursionDepth--;
                        s_cumulativeSignatureBytes -= blobLength;
                    }
                }
            }
            """
        },
        {
            "TypeRefDecoder with inverted recursion guard",
            """
            class C
            {
                int s_recursionDepth;
                int s_cumulativeSignatureBytes;
                object GetTypeFromSpecification()
                {
                    if (s_recursionDepth < MaxRecursionDepth)
                        return fallback;
                    var spec = reader.GetTypeSpecification(handle);
                    int blobLength = reader.GetBlobReader(spec.Signature).Length;
                    if (blobLength > MaxSignatureBlobLength)
                        return fallback;
                    if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
                        return fallback;
                    s_cumulativeSignatureBytes += blobLength;
                    s_recursionDepth++;
                    try
                    {
                        return spec.DecodeSignature(provider, context);
                    }
                    finally
                    {
                        s_recursionDepth--;
                        s_cumulativeSignatureBytes -= blobLength;
                    }
                }
            }
            """
        },
        {
            "TypeRefDecoder with blob length spoofed in lambda",
            """
            class C
            {
                int s_recursionDepth;
                int s_cumulativeSignatureBytes;
                object GetTypeFromSpecification()
                {
                    if (s_recursionDepth >= MaxRecursionDepth)
                        return fallback;
                    var spec = reader.GetTypeSpecification(handle);
                    int blobLength = 0;
                    if (blobLength > MaxSignatureBlobLength)
                        return fallback;
                    if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
                        return fallback;
                    Action unused = () =>
                    {
                        int blobLength =
                            reader.GetBlobReader(spec.Signature).Length;
                    };
                    s_cumulativeSignatureBytes += blobLength;
                    s_recursionDepth++;
                    try
                    {
                        return spec.DecodeSignature(provider, context);
                    }
                    finally
                    {
                        s_recursionDepth--;
                        s_cumulativeSignatureBytes -= blobLength;
                    }
                }
            }
            """
        },
        {
            "TypeRefDecoder without cumulative guard",
            """
            class C
            {
                int s_recursionDepth;
                int s_cumulativeSignatureBytes;
                object GetTypeFromSpecification()
                {
                    if (s_recursionDepth >= MaxRecursionDepth)
                        return fallback;
                    var spec = reader.GetTypeSpecification(handle);
                    int blobLength = reader.GetBlobReader(spec.Signature).Length;
                    if (blobLength > MaxSignatureBlobLength)
                        return fallback;
                    s_cumulativeSignatureBytes += blobLength;
                    s_recursionDepth++;
                    try
                    {
                        return spec.DecodeSignature(provider, context);
                    }
                    finally
                    {
                        s_recursionDepth--;
                        s_cumulativeSignatureBytes -= blobLength;
                    }
                }
            }
            """
        },
        {
            "TypeRefDecoder without finally cleanup",
            """
            class C
            {
                int s_recursionDepth;
                int s_cumulativeSignatureBytes;
                object GetTypeFromSpecification()
                {
                    if (s_recursionDepth >= MaxRecursionDepth)
                        return fallback;
                    var spec = reader.GetTypeSpecification(handle);
                    int blobLength = reader.GetBlobReader(spec.Signature).Length;
                    if (blobLength > MaxSignatureBlobLength)
                        return fallback;
                    if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
                        return fallback;
                    s_cumulativeSignatureBytes += blobLength;
                    s_recursionDepth++;
                    return spec.DecodeSignature(provider, context);
                }
            }
            """
        },
        {
            "TypeRefDecoder counters incremented conditionally",
            """
            class C
            {
                int s_recursionDepth;
                int s_cumulativeSignatureBytes;
                object GetTypeFromSpecification()
                {
                    if (s_recursionDepth >= MaxRecursionDepth)
                        return fallback;
                    var spec = reader.GetTypeSpecification(handle);
                    int blobLength = reader.GetBlobReader(spec.Signature).Length;
                    if (blobLength > MaxSignatureBlobLength)
                        return fallback;
                    if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
                        return fallback;
                    if (shouldCount)
                    {
                        s_cumulativeSignatureBytes += blobLength;
                        s_recursionDepth++;
                    }
                    try
                    {
                        return spec.DecodeSignature(provider, context);
                    }
                    finally
                    {
                        s_recursionDepth--;
                        s_cumulativeSignatureBytes -= blobLength;
                    }
                }
            }
            """
        },
    };

    [Theory]
    [MemberData(nameof(SanctionedShapes))]
    public void SanctionedShape_IsAccepted(string _, string source)
    {
        var violations = UnsafeDecodeOccurrences(ParseSource(source)).ToArray();

        Assert.Empty(violations);
    }

    public static TheoryData<string, string> SanctionedShapes => new()
    {
        {
            "prescan ternary",
            """
            class C
            {
                object M() => SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind)
                    ? value.DecodeSignature(provider, context)
                    : fallback;
            }
            """
        },
        {
            "prescan positive if",
            """
            class C
            {
                object M()
                {
                    if (SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind))
                        return value.DecodeSignature(provider, context);
                    return fallback;
                }
            }
            """
        },
        {
            "prescan rejecting if",
            """
            class C
            {
                object M()
                {
                    if (!SignatureBlobGuard.IsSafeToDecode(reader, value.Signature, kind))
                        return fallback;
                    return value.DecodeSignature(provider, context);
                }
            }
            """
        },
        {
            "disposable TypeSpec scope",
            """
            class C
            {
                object M()
                {
                    if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                        return fallback;
                    using (scope)
                    {
                        return reader.GetTypeSpecification(handle).DecodeSignature(provider, context);
                    }
                }
            }
            """
        },
        {
            "bounded TypeRefDecoder",
            """
            class C
            {
                int s_recursionDepth;
                int s_cumulativeSignatureBytes;
                object GetTypeFromSpecification()
                {
                    if (s_recursionDepth >= MaxRecursionDepth)
                        return fallback;
                    var spec = reader.GetTypeSpecification(handle);
                    int blobLength = reader.GetBlobReader(spec.Signature).Length;
                    if (blobLength > MaxSignatureBlobLength)
                        return fallback;
                    if (s_cumulativeSignatureBytes + blobLength > MaxCumulativeSignatureBytes)
                        return fallback;
                    s_cumulativeSignatureBytes += blobLength;
                    s_recursionDepth++;
                    try
                    {
                        return spec.DecodeSignature(provider, context);
                    }
                    finally
                    {
                        s_recursionDepth--;
                        s_cumulativeSignatureBytes -= blobLength;
                    }
                }
            }
            """
        },
    };

    static IEnumerable<SimpleNameSyntax> UnsafeDecodeOccurrences(SyntaxNode root)
        => root.DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Where(name => DecodeMethodNames.Contains(name.Identifier.Text))
            .Where(name => !TryGetInvokedDecode(name, out var invocation)
                || !IsSanctionedDecode(invocation));

    static SyntaxNode ParseRoot(string file)
    {
        var token = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: token);
        return tree.GetRoot(token);
    }

    static SyntaxNode ParseSource(string source)
    {
        var token = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: token);
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

    static bool IsSanctionedDecode(InvocationExpressionSyntax invocation)
        => IsDepthAndCumulativeBoundedTypeSpecDecode(invocation)
            || IsBoundedByTypeSpecGuard(invocation)
            || (!IsNestedTypeSpecReentry(invocation)
                && (IsPrescanGuardedTernary(invocation)
                    || IsPrescanGuardedIf(invocation)));

    // `<expr>.GetTypeSpecification(...).Decode*Signature(...)`: the decode's
    // receiver is a GetTypeSpecification invocation, directly or through a
    // local initialized from one, inside the provider callback where it is a
    // cross-handle re-entry rather than a top-level TypeSpec gateway.
    static bool IsNestedTypeSpecReentry(InvocationExpressionSyntax invocation)
        => TryGetTypeSpecificationOrigin(invocation, out _, out _)
            && invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is
            {
                Identifier.Text: "GetTypeFromSpecification"
            };

    static bool IsBoundedByTypeSpecGuard(InvocationExpressionSyntax invocation)
    {
        if (!TryGetTypeSpecificationOrigin(invocation, out var reader, out var handle))
            return false;

        foreach (var usingStatement in invocation.Ancestors().OfType<UsingStatementSyntax>())
        {
            if (!usingStatement.Statement.Span.Contains(invocation.Span)
                || CrossesDeferredExecutionBoundary(invocation, usingStatement)
                || usingStatement.Expression is not IdentifierNameSyntax scope
                || usingStatement.Parent is not BlockSyntax block)
            {
                continue;
            }

            int usingIndex = block.Statements.IndexOf(usingStatement);
            if (usingIndex <= 0
                || block.Statements[usingIndex - 1] is not IfStatementSyntax guard)
                continue;

            if (ConditionRejectsFailedTypeSpecEntry(
                    guard.Condition,
                    scope.Identifier.Text,
                    reader,
                    handle)
                && StatementAlwaysExits(guard.Statement))
            {
                return true;
            }
        }

        return false;
    }

    static bool TryGetTypeSpecificationOrigin(
        InvocationExpressionSyntax decode,
        out ExpressionSyntax reader,
        out ExpressionSyntax handle)
    {
        reader = null!;
        handle = null!;
        if (decode.Expression is not MemberAccessExpressionSyntax decodeMember)
            return false;

        ExpressionSyntax? receiver = decodeMember.Expression;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (receiver is IdentifierNameSyntax identifier)
        {
            if (!visited.Add(identifier.Identifier.Text))
                return false;
            receiver = FindLocalInitializer(decode, identifier.Identifier.Text);
        }

        if (receiver is not InvocationExpressionSyntax getTypeSpec)
            return false;

        if (getTypeSpec?.Expression is not MemberAccessExpressionSyntax access
            || access.Name.Identifier.Text != "GetTypeSpecification"
            || getTypeSpec.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        reader = access.Expression;
        handle = getTypeSpec.ArgumentList.Arguments[0].Expression;
        return true;
    }

    static ExpressionSyntax? FindLocalInitializer(
        InvocationExpressionSyntax invocation,
        string identifier)
        => invocation.SyntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable =>
                variable.Identifier.Text == identifier
                && variable.SpanStart < invocation.SpanStart
                && variable.Ancestors().OfType<BlockSyntax>().FirstOrDefault() is { } block
                && block.Span.Contains(invocation.Span))
            .OrderByDescending(variable => variable.SpanStart)
            .Select(variable => variable.Initializer?.Value)
            .FirstOrDefault(value => value is not null);

    static bool ConditionRejectsFailedTypeSpecEntry(
        ExpressionSyntax condition,
        string scopeName,
        ExpressionSyntax reader,
        ExpressionSyntax handle)
    {
        condition = StripParentheses(condition);
        if (condition is not PrefixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LogicalNotExpression,
                Operand: { } operand
            })
        {
            return false;
        }

        operand = StripParentheses(operand);
        if (operand is not InvocationExpressionSyntax tryEnter
            || tryEnter.Expression is not MemberAccessExpressionSyntax member
            || member.Name.Identifier.Text != "TryEnter"
            || !IsNamedExpression(member.Expression, "TypeSpecGuard"))
        {
            return false;
        }

        var arguments = tryEnter.ArgumentList.Arguments;
        return arguments.Count == 3
            && Equivalent(arguments[0].Expression, reader)
            && Equivalent(arguments[1].Expression, handle)
            && arguments[2].RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
            && arguments[2].Expression is DeclarationExpressionSyntax
            {
                Designation: SingleVariableDesignationSyntax designation
            }
            && designation.Identifier.Text == scopeName;
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
        var decodeReceiver = decodeMember.Expression.ToString();

        foreach (var ancestor in invocation.Ancestors())
        {
            if (ancestor is ConditionalExpressionSyntax conditional
                && conditional.WhenTrue.Span.Contains(invocation.Span)
                && !CrossesDeferredExecutionBoundary(invocation, conditional)
                && ConditionGuardsReceiverSignature(conditional.Condition, decodeReceiver))
            {
                return true;
            }
        }
        return false;
    }

    static bool IsPrescanGuardedIf(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax decodeMember)
            return false;
        string decodeReceiver = decodeMember.Expression.ToString();

        foreach (var ifStatement in invocation.Ancestors().OfType<IfStatementSyntax>())
        {
            if (ifStatement.Statement.Span.Contains(invocation.Span)
                && !CrossesDeferredExecutionBoundary(invocation, ifStatement)
                && ConditionGuardsReceiverSignature(ifStatement.Condition, decodeReceiver))
            {
                return true;
            }
        }

        foreach (var block in invocation.Ancestors().OfType<BlockSyntax>())
        {
            if (CrossesDeferredExecutionBoundary(invocation, block))
                continue;

            var guardedStatement = block.Statements.FirstOrDefault(
                statement => statement.Span.Contains(invocation.Span));
            if (guardedStatement is null)
                continue;

            int guardedIndex = block.Statements.IndexOf(guardedStatement);
            foreach (var ifStatement in block.Statements.Take(guardedIndex).OfType<IfStatementSyntax>())
            {
                if (ConditionRejectsUnsafeReceiverSignature(ifStatement.Condition, decodeReceiver)
                    && StatementAlwaysExits(ifStatement.Statement))
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool ConditionGuardsReceiverSignature(ExpressionSyntax condition, string decodeReceiver)
    {
        condition = StripParentheses(condition);
        if (condition is InvocationExpressionSyntax invocation)
            return IsReceiverSignatureGuard(invocation, decodeReceiver);

        return condition is BinaryExpressionSyntax binary
            && binary.IsKind(SyntaxKind.LogicalAndExpression)
            && (ConditionGuardsReceiverSignature(binary.Left, decodeReceiver)
                || ConditionGuardsReceiverSignature(binary.Right, decodeReceiver));
    }

    static bool ConditionRejectsUnsafeReceiverSignature(
        ExpressionSyntax condition,
        string decodeReceiver)
    {
        condition = StripParentheses(condition);
        if (condition is not PrefixUnaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LogicalNotExpression,
                Operand: { } operand
            })
        {
            return false;
        }

        operand = StripParentheses(operand);
        return operand is InvocationExpressionSyntax invocation
            && IsReceiverSignatureGuard(invocation, decodeReceiver);
    }

    static bool IsReceiverSignatureGuard(
        InvocationExpressionSyntax invocation,
        string decodeReceiver)
        => invocation.Expression is MemberAccessExpressionSyntax member
            && member.Name.Identifier.Text == "IsSafeToDecode"
            && IsNamedExpression(member.Expression, "SignatureBlobGuard")
            && invocation.ArgumentList.Arguments.Any(argument =>
                argument.Expression is MemberAccessExpressionSyntax blob
                && blob.Name.Identifier.Text == "Signature"
                && blob.Expression.ToString() == decodeReceiver);

    static bool IsDepthAndCumulativeBoundedTypeSpecDecode(
        InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver
            }
            || invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not
            {
                Identifier.Text: "GetTypeFromSpecification",
                Body: { } body
            } method)
        {
            return false;
        }

        if (!TryGetTypeSpecificationOrigin(invocation, out var reader, out _))
            return false;

        bool blobLengthIsBoundToReceiver =
            FindLocalInitializer(invocation, "blobLength") is MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Length",
                Expression: InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax blobReader
                } getBlobReader
            }
            && blobReader.Name.Identifier.Text == "GetBlobReader"
            && Equivalent(blobReader.Expression, reader)
            && getBlobReader.ArgumentList.Arguments.Count == 1
            && getBlobReader.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax spec,
                Name.Identifier.Text: "Signature"
            }
            && spec.Identifier.Text == receiver.Identifier.Text;
        if (!blobLengthIsBoundToReceiver
            || !HasRejectingComparisonBefore(
                body,
                invocation,
                SyntaxKind.GreaterThanOrEqualExpression,
                "s_recursionDepth",
                "MaxRecursionDepth")
            || !HasRejectingComparisonBefore(
                body,
                invocation,
                SyntaxKind.GreaterThanExpression,
                "blobLength",
                "MaxSignatureBlobLength")
            || !HasRejectingComparisonBefore(
                body,
                invocation,
                SyntaxKind.GreaterThanExpression,
                "s_cumulativeSignatureBytes + blobLength",
                "MaxCumulativeSignatureBytes"))
        {
            return false;
        }

        var guardedTry = invocation.Ancestors().OfType<TryStatementSyntax>()
            .FirstOrDefault(statement => statement.Block.Span.Contains(invocation.Span));
        if (guardedTry?.Finally?.Block is not { } finallyBlock
            || !HasImmediatelyPrecedingCounterIncrements(guardedTry)
            || !HasExactCounterCleanup(finallyBlock))
        {
            return false;
        }

        return true;
    }

    static bool HasRejectingComparisonBefore(
        BlockSyntax body,
        InvocationExpressionSyntax invocation,
        SyntaxKind kind,
        string left,
        string right)
        => body.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Any(statement =>
                statement.SpanStart < invocation.SpanStart
                && StripParentheses(statement.Condition) is BinaryExpressionSyntax comparison
                && comparison.IsKind(kind)
                && Equivalent(
                    StripParentheses(comparison.Left),
                    SyntaxFactory.ParseExpression(left))
                && Equivalent(
                    StripParentheses(comparison.Right),
                    SyntaxFactory.ParseExpression(right))
                && StatementAlwaysExits(statement.Statement));

    static bool HasImmediatelyPrecedingCounterIncrements(TryStatementSyntax guardedTry)
    {
        if (guardedTry.Parent is not BlockSyntax block)
            return false;

        int tryIndex = block.Statements.IndexOf(guardedTry);
        return tryIndex >= 2
            && IsAssignmentStatement(
                block.Statements[tryIndex - 2],
                "s_cumulativeSignatureBytes",
                "blobLength",
                SyntaxKind.AddAssignmentExpression)
            && IsPostfixMutationStatement(
                block.Statements[tryIndex - 1],
                "s_recursionDepth",
                SyntaxKind.PostIncrementExpression);
    }

    static bool HasExactCounterCleanup(BlockSyntax finallyBlock)
        => finallyBlock.Statements.Count == 2
            && IsPostfixMutationStatement(
                finallyBlock.Statements[0],
                "s_recursionDepth",
                SyntaxKind.PostDecrementExpression)
            && IsAssignmentStatement(
                finallyBlock.Statements[1],
                "s_cumulativeSignatureBytes",
                "blobLength",
                SyntaxKind.SubtractAssignmentExpression);

    static bool IsPostfixMutationStatement(
        StatementSyntax statement,
        string identifier,
        SyntaxKind kind)
        => statement is ExpressionStatementSyntax
        {
            Expression: PostfixUnaryExpressionSyntax expression
        }
            && expression.IsKind(kind)
            && expression.Operand is IdentifierNameSyntax id
            && id.Identifier.Text == identifier;

    static bool IsAssignmentStatement(
        StatementSyntax statement,
        string left,
        string right,
        SyntaxKind kind)
        => statement is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax expression
        }
            && IsAssignment(expression, left, right, kind);

    static bool IsAssignment(
        AssignmentExpressionSyntax expression,
        string left,
        string right,
        SyntaxKind kind)
        => expression.IsKind(kind)
            && expression.Left is IdentifierNameSyntax leftIdentifier
            && leftIdentifier.Identifier.Text == left
            && expression.Right is IdentifierNameSyntax rightIdentifier
            && rightIdentifier.Identifier.Text == right;

    static bool StatementAlwaysExits(StatementSyntax statement)
        => statement switch
        {
            ReturnStatementSyntax or ThrowStatementSyntax or ContinueStatementSyntax => true,
            BlockSyntax { Statements.Count: > 0 } block
                => StatementAlwaysExits(block.Statements[^1]),
            _ => false,
        };

    static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression;
    }

    static bool CrossesDeferredExecutionBoundary(SyntaxNode descendant, SyntaxNode ancestor)
    {
        for (var node = descendant.Parent; node is not null && node != ancestor; node = node.Parent)
        {
            if (node is LambdaExpressionSyntax
                or AnonymousMethodExpressionSyntax
                or LocalFunctionStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    static bool Equivalent(ExpressionSyntax left, ExpressionSyntax right)
        => left.WithoutTrivia().ToFullString() == right.WithoutTrivia().ToFullString();

    static bool IsNamedExpression(ExpressionSyntax expression, string name)
        => expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text == name,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text == name,
            _ => false,
        };

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
