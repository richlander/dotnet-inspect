using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Harness-owned span attribution for ReturnToSender recompile failures.
///
/// The substitution experiment in <see cref="ReturnToSender.TryIsolateRecompileFailure"/>
/// can only credit a body defect when the authored body compiles in the failing
/// row's shell. When the shell is also broken the experiment goes blind and the
/// row is filed as <c>ShellOrClosureDefect</c>, masking any decompiler body
/// defect that co-occurs with a broken shell.
///
/// This helper recovers the additional signal without a further compile: it
/// locates the target member's body span in both the decompiled and the
/// authored artifacts (which the isolation pass already compiled) and compares
/// where each compiler error lands. The attribution is intentionally
/// conservative so the resulting count stays a sound lower bound — it never
/// converts a shell fault into a body defect (see
/// <see cref="DecompiledBodyIsolatedUnderBrokenShell"/>).
///
/// Attribution is a harness measurement concern; it reads compiler diagnostics
/// (an independent oracle) and the source the pipeline already produced. It does
/// not push a body-span API into the product printer and it never mutates the
/// artifact that flows to other consumers.
/// </summary>
internal static class SpanAttribution
{
    internal enum TargetMemberKind
    {
        Method,
        Constructor,
        PropertyGet,
        PropertySet,
        EventAdd,
        EventRemove,
    }

    internal readonly record struct TargetIdentity(
        string FullType,
        string MetadataMemberName,
        int ParameterCount,
        TargetMemberKind Kind);

    /// <summary>
    /// Sound refinement of the substitution oracle for the case where the
    /// authored body also failed to compile (shell broken). Returns true only
    /// when the authored body is provably error-free within its own body span
    /// yet the decompiled body carries at least one in-body error: the shell
    /// then demonstrably does not force a body-region error, so the decompiled
    /// body's in-body errors are attributable to the decompiler.
    ///
    /// If either body span cannot be uniquely located the method returns false
    /// (the caller keeps the existing <c>ShellOrClosureDefect</c> classification);
    /// it never fabricates a body defect.
    /// </summary>
    internal static bool DecompiledBodyIsolatedUnderBrokenShell(
        string decompiledSource,
        ImmutableArray<Diagnostic> decompiledDiagnostics,
        string authoredSource,
        ImmutableArray<Diagnostic> authoredDiagnostics,
        TargetIdentity identity)
    {
        if (TryLocateBodySpan(decompiledSource, identity) is not { } decompiledBody)
            return false;
        if (TryLocateBodySpan(authoredSource, identity) is not { } authoredBody)
            return false;

        int authoredInBody = CountErrorsInSpan(authoredDiagnostics, authoredBody);
        if (authoredInBody != 0)
            return false;

        return CountErrorsInSpan(decompiledDiagnostics, decompiledBody) > 0;
    }

    static int CountErrorsInSpan(ImmutableArray<Diagnostic> diagnostics, TextSpan bodySpan)
    {
        int count = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;
            var location = diagnostic.Location;
            if (location.Kind != LocationKind.SourceFile)
                continue;
            if (bodySpan.IntersectsWith(location.SourceSpan))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Locates the target member's body span (block or expression body) within
    /// <paramref name="source"/>, or null when the member cannot be uniquely
    /// identified. Robust to reformatting because it walks the parse tree rather
    /// than matching text.
    /// </summary>
    internal static TextSpan? TryLocateBodySpan(string source, TargetIdentity identity)
    {
        SyntaxNode root;
        try
        {
            root = CSharpSyntaxTree.ParseText(source).GetRoot();
        }
        catch (Exception)
        {
            return null;
        }

        var type = LocateType(root, identity.FullType);
        if (type is null)
            return null;

        var member = LocateMember(type, identity);
        return member is null ? null : BodySpanOf(member);
    }

    static TypeDeclarationSyntax? LocateType(SyntaxNode root, string fullType)
    {
        var simpleName = SimpleTypeName(fullType);
        var candidates = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.ValueText == simpleName)
            .ToList();

        if (candidates.Count == 1)
            return candidates[0];
        if (candidates.Count == 0)
            return null;

        // Disambiguate same-simple-name types by their full nested chain.
        var chain = TypeNameChain(fullType);
        TypeDeclarationSyntax? match = null;
        foreach (var candidate in candidates)
        {
            if (!MatchesChain(candidate, chain))
                continue;
            if (match is not null)
                return null; // still ambiguous
            match = candidate;
        }

        return match;
    }

    static bool MatchesChain(TypeDeclarationSyntax candidate, IReadOnlyList<string> chain)
    {
        int index = chain.Count - 1;
        SyntaxNode? node = candidate;
        while (node is not null && index >= 0)
        {
            if (node is TypeDeclarationSyntax type)
            {
                if (type.Identifier.ValueText != chain[index])
                    return false;
                index--;
            }

            node = node.Parent;
        }

        return index < 0;
    }

    static SyntaxNode? LocateMember(TypeDeclarationSyntax type, TargetIdentity identity)
    {
        switch (identity.Kind)
        {
            case TargetMemberKind.Method:
            {
                var methods = type.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where(method => method.Identifier.ValueText == identity.MetadataMemberName
                        && method.ParameterList.Parameters.Count == identity.ParameterCount)
                    .ToList();
                return methods.Count == 1 ? methods[0] : null;
            }

            case TargetMemberKind.Constructor:
            {
                var constructors = type.Members
                    .OfType<ConstructorDeclarationSyntax>()
                    .Where(constructor => constructor.ParameterList.Parameters.Count == identity.ParameterCount)
                    .ToList();
                return constructors.Count == 1 ? constructors[0] : null;
            }

            case TargetMemberKind.PropertyGet:
            case TargetMemberKind.PropertySet:
                return LocateAccessor(type, identity, isProperty: true);

            case TargetMemberKind.EventAdd:
            case TargetMemberKind.EventRemove:
                return LocateAccessor(type, identity, isProperty: false);

            default:
                return null;
        }
    }

    static AccessorDeclarationSyntax? LocateAccessor(TypeDeclarationSyntax type, TargetIdentity identity, bool isProperty)
    {
        string memberName = StripAccessorPrefix(identity.MetadataMemberName);
        SyntaxKind accessorKind = identity.Kind switch
        {
            TargetMemberKind.PropertyGet => SyntaxKind.GetAccessorDeclaration,
            TargetMemberKind.PropertySet => SyntaxKind.SetAccessorDeclaration,
            TargetMemberKind.EventAdd => SyntaxKind.AddAccessorDeclaration,
            TargetMemberKind.EventRemove => SyntaxKind.RemoveAccessorDeclaration,
            _ => SyntaxKind.None,
        };

        var accessorLists = isProperty
            ? type.Members.OfType<BasePropertyDeclarationSyntax>()
                .Where(member => AccessorMemberName(member) == memberName)
                .Select(member => member.AccessorList)
                .ToList()
            : type.Members.OfType<EventDeclarationSyntax>()
                .Where(member => member.Identifier.ValueText == memberName)
                .Select(member => member.AccessorList)
                .ToList();

        if (accessorLists.Count != 1 || accessorLists[0] is null)
            return null;

        // init accessors surface as set in metadata (set_X); accept either.
        var accessors = accessorLists[0]!.Accessors
            .Where(accessor => accessor.Kind() == accessorKind
                || (accessorKind == SyntaxKind.SetAccessorDeclaration
                    && accessor.Kind() == SyntaxKind.InitAccessorDeclaration))
            .ToList();
        return accessors.Count == 1 ? accessors[0] : null;
    }

    static string? AccessorMemberName(BasePropertyDeclarationSyntax member) => member switch
    {
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        IndexerDeclarationSyntax => "Item",
        _ => null,
    };

    static string StripAccessorPrefix(string metadataName)
    {
        foreach (var prefix in new[] { "get_", "set_", "add_", "remove_" })
        {
            if (metadataName.StartsWith(prefix, StringComparison.Ordinal))
                return metadataName[prefix.Length..];
        }

        return metadataName;
    }

    static TextSpan? BodySpanOf(SyntaxNode member) => member switch
    {
        MethodDeclarationSyntax method => BlockOrArrow(method.Body, method.ExpressionBody),
        ConstructorDeclarationSyntax constructor => BlockOrArrow(constructor.Body, constructor.ExpressionBody),
        AccessorDeclarationSyntax accessor => BlockOrArrow(accessor.Body, accessor.ExpressionBody),
        _ => null,
    };

    static TextSpan? BlockOrArrow(BlockSyntax? block, ArrowExpressionClauseSyntax? arrow)
    {
        if (block is not null)
            return block.Span;
        if (arrow is not null)
            return arrow.Span;
        return null;
    }

    static string SimpleTypeName(string fullType)
    {
        var chain = TypeNameChain(fullType);
        return chain.Count == 0 ? fullType : chain[^1];
    }

    static IReadOnlyList<string> TypeNameChain(string fullType)
    {
        // Split namespace + nested-type separators, drop the namespace prefix,
        // and strip metadata generic-arity backticks from each type segment.
        var segments = fullType.Split('+', '/');
        var chain = new List<string>();
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (i == 0)
            {
                int lastDot = segment.LastIndexOf('.');
                if (lastDot >= 0)
                    segment = segment[(lastDot + 1)..];
            }

            chain.Add(StripArity(segment));
        }

        return chain;
    }

    static string StripArity(string typeName)
    {
        int backtick = typeName.IndexOf('`');
        return backtick >= 0 ? typeName[..backtick] : typeName;
    }
}
