using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

using Decompiler = ILInspector.Decompiler;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Acquires per-member code sections — decompiled source, IL, annotated source,
/// custom attributes — from an assembly on disk. Owns all PE and metadata
/// access for member code so the output formatter only renders views
/// (docs/decompiler-pipeline.md, seams). Failures surface as diagnostic
/// comment text, never as missing entries.
/// </summary>
internal static class MemberCodeProvider
{
    internal sealed record Request(bool DecompiledSource, bool IL, bool AnnotatedSource, bool Attributes, bool Calls, bool Callers, bool CallGraph, bool UnsafeOperations, bool Stages = false, bool Facts = false);

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
        string? AnnotatedSourceText,
        string? AnnotatedSourceDiagnostic,
        IReadOnlyList<(string Name, string? Value)>? Attributes,
        string? StagesText = null,
        string? StagesDiagnostic = null,
        IReadOnlyList<Decompiler.Analysis.Annotation>? Facts = null);

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

        // All decompiler-backed sections (decompiled source, annotated source, IR
        // stages) read through one MetadataSource that owns its own readers.
        // A malformed-metadata failure opening it degrades those sections to
        // empty — the IL/attribute sections still render — instead of throwing.
        using var pipelineSource = OpenPipelineSource(request, dllPath, pdbPath);

        // Resolve each method's declaring type once via an index, instead of having every helper
        // (attributes, IL, decompiled source, annotated source) re-scan all TypeDefinitions per method.
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

            // Method generic parameter names, read straight from metadata, feed
            // the decompiled-source declaration formatter. Sourced here (not from
            // a decompiler pass) so it is available whenever a method body is
            // shown, independent of which sections were requested.
            var methodGenericParameters = MethodGenericParameterNames(
                reader, typeHandle, method.Name, lookupOverloadIndex, publicOnly);

            // Decompiled source: import the method to typed IR, run the raising
            // passes, and print. A null function means the member has no IL body
            // (abstract/extern) — nothing to show, not an error. PrintRaised
            // never throws; an import or pass failure surfaces as a diagnostic.
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
            if (request.AnnotatedSource)
            {
                var result = pipelineSource is null
                    ? Decompiler.DecompilerResult.Failure(
                        Decompiler.DiagnosticIds.ContextUnavailable,
                        "method body source unavailable")
                    : Decompiler.Analysis.MixedSourceRenderer.Render(
                        pipelineSource, lookupType, method.Name,
                        lookupOverloadIndex, publicOnly);
                if (result.Output is { } annotated)
                    annotatedText = annotated.TrimEnd();
                else
                    annotatedDiagnostic = DiagnosticComment(result);
            }

            // Per-pass IR pipeline dump (JitDump-style). Shares the decompiler's
            // MetadataSource with decompiled source, so it is opened when either
            // section is requested.
            string? stagesText = null, stagesDiagnostic = null;
            if (request.Stages && pipelineSource is not null)
            {
                var result = Decompiler.Pipeline.StageDump.DumpMethod(
                    pipelineSource, lookupType, method.Name,
                    Decompiler.Pipeline.StageDumpView.IrTree, lookupOverloadIndex, publicOnly);
                if (result.Output is { } stages)
                    stagesText = stages.TrimEnd();
                else
                    stagesDiagnostic = DiagnosticComment(result);
            }

            // Structured hidden-fact rows for one method: classify the imported
            // body (the same engine the Annotated Source view uses), in IL order.
            IReadOnlyList<Decompiler.Analysis.Annotation>? facts = null;
            if (request.Facts && pipelineSource is not null)
            {
                var function = Decompiler.Pipeline.IrImporter.Import(
                    pipelineSource, lookupType, method.Name, lookupOverloadIndex, publicOnly);
                if (function is not null)
                    facts = Decompiler.Analysis.AnnotationStructuredView.Collect(function);
            }

            results.Add((method, new Item(
                loweredBody,
                loweredDiagnostic,
                methodGenericParameters,
                ilText,
                ilDiagnostic,
                annotatedText,
                annotatedDiagnostic,
                attributes,
                stagesText,
                stagesDiagnostic,
                facts)));
        }

        return results;
    }

    /// <summary>Renders a failed result as comment lines so sections degrade honestly instead of disappearing.</summary>
    static string DiagnosticComment(Decompiler.DecompilerResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"// {d}"));

    /// <summary>
    /// Resolves the selected method overload's generic parameter names directly
    /// from metadata (e.g. <c>["T", "TResult"]</c>), or null when the method is
    /// non-generic or not found. Mirrors the overload-selection walk the IL and
    /// annotated-source sections use, so the same method is named.
    /// </summary>
    static IReadOnlyList<string>? MethodGenericParameterNames(
        MetadataReader reader, TypeDefinitionHandle typeHandle, string methodName, int overloadIndex, bool publicOnly)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        int matchCount = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != methodName)
                continue;
            if (publicOnly && (method.Attributes & System.Reflection.MethodAttributes.MemberAccessMask) != System.Reflection.MethodAttributes.Public)
                continue;
            if (matchCount != overloadIndex)
            {
                matchCount++;
                continue;
            }
            var handles = method.GetGenericParameters();
            if (handles.Count == 0)
                return null;
            return handles.Select(reader.GetGenericParameterName).ToList();
        }
        return null;
    }

    /// <summary>
    /// Opens the decompiler's reader for the code sections that need it
    /// (decompiled source, annotated source, IR stages), or null when none are
    /// requested or when the assembly cannot be opened (malformed metadata
    /// that nonetheless passed the first PE read). Failing to a null source
    /// keeps the no-crash invariant: those sections degrade to empty while the
    /// IL and attribute sections, which use the already-open reader, still
    /// render.
    /// </summary>
    static Decompiler.Pipeline.MetadataSource? OpenPipelineSource(Request request, string dllPath, string? pdbPath)
    {
        if (!request.DecompiledSource && !request.Stages && !request.AnnotatedSource && !request.Facts)
            return null;
        try
        {
            return Decompiler.Pipeline.MetadataSource.Open(dllPath, pdbPath);
        }
        catch
        {
            return null;
        }
    }
}
