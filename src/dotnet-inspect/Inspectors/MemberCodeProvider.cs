using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

using Decompiler = ILInspector.Decompiler;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Acquires per-member code sections — decompiled source, IL, annotated IL,
/// custom attributes — from an assembly on disk. Owns all PE and metadata
/// access for member code so the output formatter only renders views
/// (docs/decompiler-pipeline.md, seams). Failures surface as diagnostic
/// comment text, never as missing entries.
/// </summary>
internal static class MemberCodeProvider
{
    internal sealed record Request(bool DecompiledSource, bool IL, bool AnnotatedIL, bool Attributes);

    /// <summary>
    /// Code content for one member. Body and diagnostic are mutually
    /// exclusive per section: a body renders (with declaration formatting
    /// applied by the caller), a diagnostic renders verbatim as comments.
    /// </summary>
    internal sealed record Item(
        string? LoweredBody,
        string? LoweredDiagnostic,
        IReadOnlyList<string>? MethodGenericParameters,
        string? ILText,
        string? ILDiagnostic,
        string? AnnotatedILText,
        string? AnnotatedILDiagnostic,
        IReadOnlyList<(string Name, string? Value)>? Attributes);

    internal static List<(ApiMember Member, Item Code)> Collect(
        ApiType type, List<ApiMember> methods, string dllPath, int overloadIndex,
        Request request, string? pdbPath = null)
    {
        var results = new List<(ApiMember, Item)>();
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            return results;

        var reader = peReader.GetMetadataReader();

        // Resolve each method's declaring type once via an index, instead of having every helper
        // (attributes, IL, decompiled source, annotated IL) re-scan all TypeDefinitions per method.
        var typeIndex = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (var typeDefHandle in reader.TypeDefinitions)
            typeIndex.TryAdd(reader.GetFullTypeName(reader.GetTypeDefinition(typeDefHandle)), typeDefHandle);

        foreach (var method in methods)
        {
            var lookupType = method.DeclaringType ?? type.FullName;
            var lookupOverloadIndex = method.DeclaringOverloadIndex is { } declaringIndex
                ? declaringIndex - 1
                : overloadIndex;
            var publicOnly = method.Kind != "explicit-interface-implementation";

            if (!typeIndex.TryGetValue(lookupType, out var typeHandle))
                continue;

            IReadOnlyList<(string Name, string? Value)>? attributes = null;
            if (request.Attributes)
            {
                var found = AttributeReader.GetMethodAttributes(
                    reader, typeHandle, method.Name, lookupOverloadIndex, publicOnly);
                if (found.Count > 0)
                    attributes = found.Select(a => (a.Name, a.Value)).ToList();
            }

            // Decompiled source and annotated IL share one method-body context (it is immutable),
            // so the PDB is opened and the method body decoded once rather than per section.
            Decompiler.MethodBodyContext? context = null;
            Decompiler.DecompilerResult? contextFailure = null;
            if (request.DecompiledSource || request.AnnotatedIL)
            {
                try
                {
                    context = Decompiler.MethodBodyContext.Create(
                        peReader, reader, typeHandle, method.Name, lookupOverloadIndex, publicOnly, externalPdbPath: pdbPath);
                }
                catch (Exception ex)
                {
                    contextFailure = Decompiler.DecompilerResult.Failure(
                        Decompiler.DiagnosticIds.ContextUnavailable,
                        $"method body context unavailable: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // A null context with no failure means the member has no IL body
            // (abstract/extern) — nothing to show, not an error. One analysis
            // feeds both code sections (CFG and stack simulation are computed
            // once, not per emitter).
            var analysis = context != null ? Decompiler.MethodAnalysis.Create(context) : null;

            string? loweredBody = null, loweredDiagnostic = null;
            if (request.DecompiledSource && (analysis != null || contextFailure != null))
            {
                var result = contextFailure ?? Decompiler.CSharpEmitter.Decompile(analysis!);
                if (result.Output is { } lowered)
                    loweredBody = lowered;
                else
                    loweredDiagnostic = DiagnosticComment(result);
            }

            string? ilText = null, ilDiagnostic = null;
            if (request.IL)
            {
                try
                {
                    var instructions = ILDisassembler.DisassembleMethod(
                        peReader, reader, typeHandle, method.Name, lookupOverloadIndex, publicOnly);
                    if (instructions is { Count: > 0 })
                        ilText = string.Join(Environment.NewLine, instructions.Select(i => i.ToString()));
                }
                catch (Exception ex)
                {
                    ilDiagnostic = $"// {Decompiler.DiagnosticIds.InternalError}: IL disassembly failed: {ex.GetType().Name}: {ex.Message}";
                }
            }

            string? annotatedText = null, annotatedDiagnostic = null;
            if (request.AnnotatedIL && (analysis != null || contextFailure != null))
            {
                var result = contextFailure ?? Decompiler.AnnotatedILEmitter.Decompile(
                    analysis!, Decompiler.ILAnnotationDepth.Structured);
                if (result.Output is { } annotated)
                    annotatedText = annotated.TrimEnd();
                else
                    annotatedDiagnostic = DiagnosticComment(result);
            }

            results.Add((method, new Item(
                loweredBody,
                loweredDiagnostic,
                context?.GenericContext?.MethodParameters,
                ilText,
                ilDiagnostic,
                annotatedText,
                annotatedDiagnostic,
                attributes)));
        }

        return results;
    }

    /// <summary>Renders a failed result as comment lines so sections degrade honestly instead of disappearing.</summary>
    static string DiagnosticComment(Decompiler.DecompilerResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"// {d}"));
}
