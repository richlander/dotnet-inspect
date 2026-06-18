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
    internal sealed record Request(bool DecompiledSource, bool IL, bool AnnotatedIL, bool Attributes, bool Calls, bool Callers, bool CallGraph, bool UnsafeOperations);

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
        ApiType type, List<ApiMember> methods, string dllPath, int? overloadIndex,
        Request request, string? pdbPath = null)
    {
        var results = new List<(ApiMember, Item)>();
        
        // Sections that require a single selected method (IL, decompiled source, etc.)
        // are skipped when no overload index is provided. Callers works across all overloads
        // and is handled separately in PopulateIndexSections.
        if (!overloadIndex.HasValue)
            return results;
            
        using var stream = File.OpenRead(dllPath);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            return results;

        var reader = peReader.GetMetadataReader();

        // Decompiled source is produced by the replacement pipeline; the old
        // emitter is retired from the product path (demoted to the harness's
        // differential oracle). The source owns its own readers for the call.
        // A malformed-metadata failure opening it degrades decompiled source to
        // empty — the IL/attribute sections still render — instead of throwing.
        using var pipelineSource = OpenPipelineSource(request, dllPath);

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
                : overloadIndex!.Value;
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

            // Annotated IL still uses the method-body context (it is immutable),
            // so the PDB is opened and the method body decoded once for it.
            Decompiler.MethodBodyContext? context = null;
            Decompiler.DecompilerResult? contextFailure = null;
            if (request.AnnotatedIL)
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

            // Annotated IL keeps the old pipeline's analysis for now (a lower
            // -level diagnostic view, not the decompiled source users read).
            var analysis = context != null ? Decompiler.MethodAnalysis.Create(context) : null;

            // Decompiled source through the replacement pipeline: import the
            // method to typed IR, run the raising passes, and print. A null
            // function means the member has no IL body (abstract/extern) —
            // nothing to show, not an error. PrintRaised never throws; an
            // import or pass failure surfaces as an honest diagnostic.
            string? loweredBody = null, loweredDiagnostic = null;
            if (request.DecompiledSource && pipelineSource is not null)
            {
                var function = Decompiler.Pipeline.IrImporter.Import(
                    pipelineSource, lookupType, method.Name, lookupOverloadIndex, publicOnly);
                if (function is not null)
                {
                    var result = Decompiler.Pipeline.CSharpPrinter.PrintRaised(function);
                    if (result.Output is { } lowered)
                        // A constructor's base/this chain is lifted out of the
                        // body (it is invalid as a statement); show it as the
                        // signature initializer so the call is not lost.
                        loweredBody = result.ConstructorChain is { } chain
                            ? $": {chain}{(lowered.Length == 0 ? "" : Environment.NewLine + lowered)}"
                            : lowered;
                    else
                        loweredDiagnostic = DiagnosticComment(result);
                }
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

    /// <summary>
    /// Opens the replacement pipeline's reader for decompiled source, or null
    /// when not requested or when the assembly cannot be opened (malformed
    /// metadata that nonetheless passed the first PE read). Failing to a null
    /// source keeps the no-crash invariant: decompiled source degrades to empty
    /// while the IL and attribute sections, which use the already-open reader,
    /// still render.
    /// </summary>
    static Decompiler.Pipeline.MetadataSource? OpenPipelineSource(Request request, string dllPath)
    {
        if (!request.DecompiledSource)
            return null;
        try
        {
            return Decompiler.Pipeline.MetadataSource.Open(dllPath);
        }
        catch
        {
            return null;
        }
    }
}
