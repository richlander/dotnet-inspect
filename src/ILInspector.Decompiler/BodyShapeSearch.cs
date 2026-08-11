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
/// <param name="Member">Stable qualified selector for the containing method.</param>
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
/// <param name="Subject">Method token or metadata operation that failed.</param>
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
    /// <summary>
    /// Stable C# rendered-syntax kinds accepted by <see cref="Search"/>.
    /// The IL-only <see cref="AnnotatedSourceNodeKinds.Instruction"/> kind is excluded.
    /// </summary>
    public static IReadOnlyList<string> SupportedKinds { get; } =
    [
        .. AnnotatedSourceNodeKinds.All
            .Where(kind => kind != AnnotatedSourceNodeKinds.Instruction)
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>
    /// Decompiles the assembly's API-surface method bodies and returns one result per
    /// exact occurrence of <paramref name="kind"/>.
    /// </summary>
    /// <param name="source">Live metadata and PE source for one assembly.</param>
    /// <param name="kind">Exact value from <see cref="AnnotatedSourceNodeKinds.All"/>.</param>
    /// <param name="includeAll">
    /// Include non-public, hidden, and obsolete members. Compiler-generated implementation
    /// methods remain folded into their source-facing member bodies.
    /// </param>
    /// <param name="limit">Optional maximum number of matches. Search stops when reached.</param>
    /// <param name="cancellationToken">Cancellation token checked between method bodies.</param>
    public static BodyShapeSearchResult Search(
        MetadataSource source,
        string kind,
        bool includeAll = false,
        int? limit = null,
        CancellationToken cancellationToken = default)
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
        var methodTokens = SurfaceMethodTokens(source.Reader, surface, includeAll);
        var matches = new List<BodyShapeMatch>();
        int methodsInspected = 0;

        foreach (int methodToken in methodTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken);
            var method = source.Reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var function = IrImporter.Import(source, handle);
            if (function is null)
                continue;
            if (InternalError(function.Diagnostics) is { } importError)
            {
                failures.Add(new BodyShapeSearchFailure($"0x{methodToken:X8}", importError));
                continue;
            }

            methodsInspected++;
            var rendered = CSharpPrinter.PrintRaised(
                function,
                out var ranges,
                importMethodBody: methodRef => IrImporter.Import(source, methodRef),
                typesProvablyDisjoint: source.AreProvablyDisjoint);
            if (InternalError(rendered.Diagnostics) is { } renderError)
            {
                failures.Add(new BodyShapeSearchFailure($"0x{methodToken:X8}", renderError));
                continue;
            }
            if (!rendered.Succeeded)
            {
                failures.Add(new BodyShapeSearchFailure(
                    $"0x{methodToken:X8}",
                    rendered.Diagnostics.Count == 0
                        ? "Decompiler produced no output."
                        : string.Join("; ", rendered.Diagnostics.Select(diagnostic => diagnostic.ToString()))));
                continue;
            }

            var map = PrintedBodyMap.Create(ranges);
            MemberAnchor anchor;
            try
            {
                anchor = ApiMemberIdentity.CreateMethodAnchor(
                    source.Reader,
                    method.GetDeclaringType(),
                    method);
            }
            catch (BadImageFormatException ex)
            {
                failures.Add(new BodyShapeSearchFailure($"0x{methodToken:X8}", ex.Message));
                continue;
            }
            string member = anchor.Format(MemberAnchorFormat.Qualified);

            foreach (var node in map.Nodes)
            {
                if (!string.Equals(node.Kind, kind, StringComparison.Ordinal))
                    continue;

                matches.Add(new BodyShapeMatch(
                    source.AssemblyName,
                    member,
                    anchor.TypeFullName,
                    anchor.MemberName,
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

    static string? InternalError(IEnumerable<DecompilerDiagnostic> diagnostics)
    {
        var failures = diagnostics
            .Where(diagnostic => diagnostic.Id == DiagnosticIds.InternalError)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        return failures.Length == 0 ? null : string.Join("; ", failures);
    }

    static SortedSet<int> SurfaceMethodTokens(
        MetadataReader reader,
        ApiSurface surface,
        bool includeAll)
    {
        var tokens = new SortedSet<int>();
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                Add(member.MetadataToken, accessor: false);
                Add(member.GetterToken, accessor: true);
                Add(member.SetterToken, accessor: true);
                Add(member.AdderToken, accessor: true);
                Add(member.RemoverToken, accessor: true);
            }
        }
        return tokens;

        void Add(int? token, bool accessor)
        {
            if (token is not { } value)
                return;
            var entity = MetadataTokens.EntityHandle(value);
            if (entity.Kind != HandleKind.MethodDefinition)
                return;
            if (accessor && !includeAll)
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)entity);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    return;
            }
            tokens.Add(value);
        }
    }

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
