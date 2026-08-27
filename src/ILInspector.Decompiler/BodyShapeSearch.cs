using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler;

/// <summary>
/// One exact rendered-syntax occurrence in a decompiled method body.
/// </summary>
/// <param name="AssemblyName">Simple name of the inspected assembly.</param>
/// <param name="Member">Round-tripping qualified selector for the source-facing member body.</param>
/// <param name="TypeName">Full metadata name of the declaring type.</param>
/// <param name="MethodName">Metadata name of the containing method.</param>
/// <param name="MethodToken">MethodDef token of the containing method.</param>
/// <param name="Kind">Exact stable rendered-syntax kind that matched.</param>
/// <param name="Extent">Exact range within the method's rendered body.</param>
/// <param name="Text">Exact rendered text selected by <paramref name="Extent"/>.</param>
public sealed record BodyShapeMatch(
    string AssemblyName,
    string Member,
    string TypeName,
    string MethodName,
    int MethodToken,
    string Kind,
    PrintedExtent Extent,
    string Text);

/// <summary>
/// A method or metadata row that could not participate in a body-shape search.
/// </summary>
/// <param name="Subject">Source-facing member and MethodDef token, or metadata operation, that failed.</param>
/// <param name="Reason">Human-readable failure detail.</param>
public sealed record BodyShapeSearchFailure(string Subject, string Reason);

/// <summary>
/// Results and explicit skips from one assembly-scoped body-shape search.
/// </summary>
/// <param name="Matches">Exact rendered-syntax matches in metadata and source order.</param>
/// <param name="Failures">Rows or bodies that could not be inspected.</param>
/// <param name="MethodsInspected">Number of method bodies sent through the decompiler.</param>
public sealed record BodyShapeSearchResult(
    IReadOnlyList<BodyShapeMatch> Matches,
    IReadOnlyList<BodyShapeSearchFailure> Failures,
    int MethodsInspected);

/// <summary>
/// Searches one live assembly for an exact stable rendered-syntax kind.
/// </summary>
public static class BodyShapeSearch
{
    private static readonly IReadOnlySet<string> UnsearchableKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        AnnotatedSourceNodeKinds.Instruction,
        AnnotatedSourceNodeKinds.Unknown,
        "MemberBody",
        "Block",
        "CatchClause",
        "SwitchSection",
        "DeconstructionTarget",
        "UnsupportedExpression"
    };

    /// <summary>
    /// Stable C# rendered-syntax kinds that can appear as exact
    /// <see cref="PrintedNodeSpan"/> values in a full-fidelity body and that
    /// <see cref="Search"/> accepts.
    /// </summary>
    public static IReadOnlyList<string> SupportedKinds { get; } =
    [
        .. AnnotatedSourceNodeKinds.All
            .Where(kind => !UnsearchableKinds.Contains(kind))
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>
    /// Decompiles the assembly's API-surface method bodies and returns one result per
    /// exact occurrence of <paramref name="kind"/>.
    /// </summary>
    /// <param name="source">Live metadata and PE source for one assembly.</param>
    /// <param name="kind">Exact value from <see cref="SupportedKinds"/>.</param>
    /// <param name="includeAll">
    /// Include non-public, hidden, and obsolete members. Compiler-generated implementation
    /// methods participate only when the decompiler reconstructs them into a full-fidelity
    /// source-facing body; incomplete bodies are returned through
    /// <see cref="BodyShapeSearchResult.Failures"/>.
    /// </param>
    /// <param name="limit">Optional maximum number of matches. Search stops when reached.</param>
    /// <param name="cancellationToken">Cancellation token checked between method bodies.</param>
    /// <param name="printerOptions">
    /// Optional explicit rendering options. The library default preserves stable slot names.
    /// </param>
    public static BodyShapeSearchResult Search(
        MetadataSource source,
        string kind,
        bool includeAll = false,
        int? limit = null,
        CancellationToken cancellationToken = default,
        PrinterOptions? printerOptions = null)
        => SearchCore(
            source,
            kind,
            includeAll,
            limit,
            cancellationToken,
            printerOptions,
            methodTokens: null);

    /// <summary>
    /// Searches the selected MethodDef bodies for exact rendered-syntax node kinds.
    /// </summary>
    /// <param name="source">The metadata and PE source to inspect.</param>
    /// <param name="kind">An exact stable kind from <see cref="SupportedKinds"/>.</param>
    /// <param name="methodTokens">
    /// MethodDef tokens that define the search scope. An empty set searches no bodies.
    /// </param>
    /// <param name="includeAll">
    /// Whether non-public API-surface members may participate in the token scope. An explicitly
    /// token-selected accessor of a public property or event remains eligible.
    /// </param>
    /// <param name="limit">Optional maximum number of matches.</param>
    /// <param name="cancellationToken">Cancellation token checked between bodies.</param>
    /// <param name="printerOptions">
    /// Optional explicit rendering options. The library default preserves stable slot names.
    /// </param>
    public static BodyShapeSearchResult Search(
        MetadataSource source,
        string kind,
        IReadOnlySet<int> methodTokens,
        bool includeAll = false,
        int? limit = null,
        CancellationToken cancellationToken = default,
        PrinterOptions? printerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(methodTokens);
        return SearchCore(
            source,
            kind,
            includeAll,
            limit,
            cancellationToken,
            printerOptions,
            methodTokens);
    }

    static BodyShapeSearchResult SearchCore(
        MetadataSource source,
        string kind,
        bool includeAll,
        int? limit,
        CancellationToken cancellationToken,
        PrinterOptions? printerOptions,
        IReadOnlySet<int>? methodTokens)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!SupportedKinds.Contains(kind, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown rendered-syntax kind '{kind}'.", nameof(kind));
        if (limit is <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "The match limit must be positive.");

        var surface = source.ExtractApiSurface(includeAll);
        var failures = surface.InspectionFailures
            .Select(failure => new BodyShapeSearchFailure(
                $"{failure.Operation} at 0x{failure.SubjectToken:X8}",
                failure.Detail))
            .ToList();
        var methods = SurfaceMethods(
            source.Reader,
            surface,
            includeAll,
            methodTokens,
            failures);
        var matches = new List<BodyShapeMatch>();
        int methodsInspected = 0;

        foreach (var candidate in methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int methodToken = candidate.MethodToken;
            var anchor = ApiMemberIdentity.GetMemberAnchor(candidate.Type, candidate.Member);
            string member = candidate.AccessorOrdinal is { } accessorOrdinal
                ? $"{anchor.TypeFullName}.{anchor.StableSelector}:{accessorOrdinal}"
                : anchor.Format(MemberAnchorFormat.Qualified);
            string subject = $"{member} (0x{methodToken:X8})";
            var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken);
            var method = source.Reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var function = IrImporter.Import(source, handle);
            if (function is null)
            {
                failures.Add(new BodyShapeSearchFailure(subject, "Decompiler could not import the method body."));
                continue;
            }
            if (InternalError(function.Diagnostics) is { } importError)
            {
                failures.Add(new BodyShapeSearchFailure(subject, importError));
                continue;
            }

            methodsInspected++;
            var rendered = CSharpPrinter.PrintRaised(
                function,
                out var ranges,
                importMethodBody: methodRef => IrImporter.Import(source, methodRef),
                typesProvablyDisjoint: source.AreProvablyDisjoint,
                options: printerOptions);
            if (InternalError(rendered.Diagnostics) is { } renderError)
            {
                failures.Add(new BodyShapeSearchFailure(subject, renderError));
                continue;
            }
            if (!rendered.Succeeded)
            {
                failures.Add(new BodyShapeSearchFailure(
                    subject,
                    rendered.Diagnostics.Count == 0
                        ? "Decompiler produced no output."
                        : string.Join("; ", rendered.Diagnostics.Select(diagnostic => diagnostic.ToString()))));
                continue;
            }
            if (IncompleteBodyReason(source.Reader, method, function, rendered) is { } incompleteReason)
            {
                failures.Add(new BodyShapeSearchFailure(subject, incompleteReason));
                continue;
            }

            var map = PrintedBodyMap.Create(ranges);
            foreach (var node in map.Nodes)
            {
                if (!string.Equals(node.Kind, kind, StringComparison.Ordinal))
                    continue;

                matches.Add(new BodyShapeMatch(
                    source.AssemblyName,
                    member,
                    anchor.TypeFullName,
                    source.Reader.GetString(method.Name),
                    methodToken,
                    node.Kind,
                    node.Extent,
                    SelectText(map.Lines, node.Extent)));
                if (matches.Count == limit)
                    return new BodyShapeSearchResult(matches.AsReadOnly(), failures.AsReadOnly(), methodsInspected);
            }
        }

        return new BodyShapeSearchResult(matches.AsReadOnly(), failures.AsReadOnly(), methodsInspected);
    }

    static string? IncompleteBodyReason(
        MetadataReader reader,
        MethodDefinition method,
        IrFunction function,
        DecompilerResult rendered)
    {
        if (rendered.Fidelity != DecompilationFidelity.Full)
        {
            string diagnostics = rendered.Diagnostics.Count == 0
                ? ""
                : $" {string.Join("; ", rendered.Diagnostics.Select(diagnostic => diagnostic.ToString()))}";
            return $"Decompiler fidelity is {rendered.Fidelity}; exact body-shape search requires Full fidelity.{diagnostics}";
        }

        var attributes = method.GetCustomAttributes();
        bool isClassicAsync = AttributeReader.HasAttribute(
                reader,
                attributes,
                KnownAttributeNames.AsyncStateMachineAttribute)
            || AttributeReader.HasAttribute(
                reader,
                attributes,
                KnownAttributeNames.AsyncIteratorStateMachineAttribute);
        if (isClassicAsync && !rendered.RequiresAsyncBodyModifier)
            return "Compiler-generated async state-machine body was not reconstructed.";

        bool isIterator = AttributeReader.HasAttribute(
                reader,
                attributes,
                KnownAttributeNames.IteratorStateMachineAttribute)
            || AttributeReader.HasAttribute(
                reader,
                attributes,
                KnownAttributeNames.AsyncIteratorStateMachineAttribute);
        if (isIterator && !function.Descendants.Any(node => node is YieldReturn or YieldBreak))
            return "Compiler-generated iterator state-machine body was not reconstructed.";

        return null;
    }

    static string? InternalError(IEnumerable<DecompilerDiagnostic> diagnostics)
    {
        var failures = diagnostics
            .Where(diagnostic => diagnostic.Id == DiagnosticIds.InternalError)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        return failures.Length == 0 ? null : string.Join("; ", failures);
    }

    static IReadOnlyList<SurfaceMethod> SurfaceMethods(
        MetadataReader reader,
        ApiSurface surface,
        bool includeAll,
        IReadOnlySet<int>? methodTokens,
        List<BodyShapeSearchFailure> failures)
    {
        var methods = new SortedDictionary<int, SurfaceMethod>();
        var explicitVisibility = includeAll
            ? null
            : CSharpBodyDiff.GetVisibleExplicitImplementationBodies(reader);
        if (explicitVisibility is not null)
        {
            failures.AddRange(explicitVisibility.Failures.Select(failure =>
                new BodyShapeSearchFailure(
                    $"explicit-interface visibility at 0x{failure.SubjectToken:X8}",
                    failure.Reason)));
        }
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                Add(type, member, member.MetadataToken, accessorOrdinal: null);
                int accessorOrdinal = 0;
                if (member.GetterToken.HasValue)
                    Add(type, member, member.GetterToken, ++accessorOrdinal);
                if (member.SetterToken.HasValue)
                    Add(type, member, member.SetterToken, ++accessorOrdinal);
                accessorOrdinal = 0;
                if (member.AdderToken.HasValue)
                    Add(type, member, member.AdderToken, ++accessorOrdinal);
                if (member.RemoverToken.HasValue)
                    Add(type, member, member.RemoverToken, ++accessorOrdinal);
            }
        }
        return [.. methods.Values];

        void Add(
            ApiType type,
            ApiMember member,
            int? token,
            int? accessorOrdinal)
        {
            if (token is not { } value)
                return;
            if (methodTokens is not null && !methodTokens.Contains(value))
                return;
            if (member.ExplicitInterfaceProvenance
                ?.HasUnavailableDeclaration == true)
            {
                return;
            }
            var entity = MetadataTokens.EntityHandle(value);
            if (entity.Kind != HandleKind.MethodDefinition)
                return;
            var methodHandle = (MethodDefinitionHandle)entity;
            if (!includeAll
                && member.Kind == "explicit-interface-implementation"
                && !explicitVisibility!.Handles.Contains(methodHandle))
            {
                return;
            }
            if (accessorOrdinal.HasValue && !includeAll && methodTokens is null)
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    return;
            }
            var candidate = new SurfaceMethod(
                value,
                type,
                member,
                accessorOrdinal);
            if (!methods.TryGetValue(value, out var existing)
                || Prefer(candidate, existing))
            {
                methods[value] = candidate;
            }
        }

        static bool Prefer(SurfaceMethod candidate, SurfaceMethod existing)
        {
            bool candidateIsDeclaration = IsDeclaration(candidate);
            bool existingIsDeclaration = IsDeclaration(existing);
            if (candidateIsDeclaration != existingIsDeclaration)
                return candidateIsDeclaration;

            bool candidateHasKindMarker = HasKindMarker(candidate);
            bool existingHasKindMarker = HasKindMarker(existing);
            if (candidateHasKindMarker != existingHasKindMarker)
                return candidateHasKindMarker;

            var candidateAnchor = ApiMemberIdentity.GetMemberAnchor(candidate.Type, candidate.Member);
            var existingAnchor = ApiMemberIdentity.GetMemberAnchor(existing.Type, existing.Member);
            return string.CompareOrdinal(
                candidateAnchor.Format(MemberAnchorFormat.Qualified),
                existingAnchor.Format(MemberAnchorFormat.Qualified)) < 0;
        }

        static bool IsDeclaration(SurfaceMethod method)
            => string.IsNullOrWhiteSpace(method.Member.DeclaringType)
                || string.Equals(
                    MetadataTypeNameFormatter.FormatFullName(method.Type),
                    method.Member.DeclaringType,
                    StringComparison.Ordinal);

        static bool HasKindMarker(SurfaceMethod method)
            => method.Member.Kind is
                "explicit-interface-implementation"
                or "operator"
                or "extension-method";
    }

    private sealed record SurfaceMethod(
        int MethodToken,
        ApiType Type,
        ApiMember Member,
        int? AccessorOrdinal);

    static string SelectText(IReadOnlyList<string> lines, PrintedExtent extent)
    {
        if (extent.StartLine == extent.EndLine)
            return lines[extent.StartLine][extent.StartColumn..extent.EndColumn];

        var selected = new List<string>(extent.EndLine - extent.StartLine + 1)
        {
            lines[extent.StartLine][extent.StartColumn..]
        };
        for (int line = extent.StartLine + 1; line < extent.EndLine; line++)
            selected.Add(lines[line]);
        selected.Add(lines[extent.EndLine][..extent.EndColumn]);
        return string.Join('\n', selected);
    }
}
