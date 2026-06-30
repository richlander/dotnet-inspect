using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using ILInspector.Decompiler.Pipeline;

using Decompiler = ILInspector.Decompiler;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Acquires per-member code sections — decompiled source, IL, annotated source,
/// custom attributes — from an assembly on disk. Owns all PE and metadata
/// access for member code so the output formatter only renders views
/// (docs/decompiler.md, seams). Failures surface as diagnostic
/// comment text, never as missing entries.
/// </summary>
internal static class MemberCodeProvider
{
    internal sealed record Request(bool DecompiledSource, bool AnnotatedSource, bool CostOverlay, bool IL, bool Attributes, bool Calls, bool Callers, bool CallGraph, bool UnsafeOperations, bool Facts = false);

    /// <summary>
    /// Code content for one member. Body and diagnostic are mutually
    /// exclusive per section: a body renders (with declaration formatting
    /// applied by the caller), a diagnostic renders verbatim as comments.
    /// </summary>
    internal sealed record Item(
        string? LoweredBody,
        string? LoweredDiagnostic,
        IReadOnlyList<string>? MethodGenericParameters,
        string? AnnotatedBody,
        string? AnnotatedDiagnostic,
        string? CostOverlayBody,
        IReadOnlyList<string>? CostOverlayHeaderComments,
        string? CostOverlayDiagnostic,
        string? ILText,
        string? ILDiagnostic,
        IReadOnlyList<(string Name, string? Value)>? Attributes,
        IReadOnlyList<ILInspector.Research.ResearchViews.FactRow>? Facts = null,
        Decompiler.DecompilerTrace? DecompileTrace = null,
        bool RequiresAsyncDeclaration = false);

    internal static List<(ApiMember Member, Item Code)> Collect(
        ApiType type, List<ApiMember> methods, string dllPath, int? overloadIndex,
        Request request, string? pdbPath = null, bool includeAll = false)
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
            // Overload selection counts same-named methods at the visibility basis the
            // member index numbered them on: only public members are numbered by default,
            // but `--all` numbers every overload regardless of visibility. Counting public-
            // only here while the index numbered all overloads (the `--all` case) skips the
            // selected non-public method, so the body falsely reports "no IL body" even
            // though sections like Calls — which index every method — read it fine.
            // Explicit interface implementations are metadata-private but always selectable,
            // so they too must count across all visibilities.
            var publicOnly = !includeAll && method.Kind != "explicit-interface-implementation";

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

            // Decompiled source: raised C# only, without annotations or interleaved IL.
            string? loweredBody = null, loweredDiagnostic = null;
            Decompiler.DecompilerTrace? decompileTrace = null;
            if (request.DecompiledSource && pipelineSource is not null)
            {
                var result = RenderPlainSource(pipelineSource, lookupType, method.Name, Decompiler.Annotations.AnnotationStage.Raised, lookupOverloadIndex, publicOnly);
                decompileTrace = new Decompiler.DecompilerTrace(result.Fidelity, pipelineSource.Symbols, result.Diagnostics);
                if (result.Output is { } plain)
                    loweredBody = plain.TrimEnd();
                else
                    loweredDiagnostic = DiagnosticComment(result);
            }

            // Annotated source: raised C# with hidden-fact comments and the
            // raw IL interleaved beneath each statement.
            string? annotatedBody = null, annotatedDiagnostic = null;
            if (request.AnnotatedSource && pipelineSource is not null)
            {
                var result = ILInspector.Research.ResearchViews.RenderMixed(
                    pipelineSource, lookupType, method.Name, overloadIndex: lookupOverloadIndex, publicOnly: publicOnly);
                decompileTrace = result.Trace;
                if (result.Output is { } annotated)
                    annotatedBody = annotated.TrimEnd();
                else
                    annotatedDiagnostic = DiagnosticComment(result);
            }

            string? costOverlayBody = null, costOverlayDiagnostic = null;
            IReadOnlyList<string>? costOverlayHeaderComments = null;
            if (request.CostOverlay && pipelineSource is not null)
            {
                var overlay = ILInspector.Research.ResearchViews.RenderCostOverlayWithHeaderFacts(
                    pipelineSource, lookupType, method.Name, overloadIndex: lookupOverloadIndex, publicOnly: publicOnly);
                var result = overlay.Body;
                decompileTrace = result.Trace;
                if (result.Output is { } costOverlay)
                {
                    costOverlayBody = costOverlay.TrimEnd();
                    if (overlay.HeaderFacts.Count > 0)
                        costOverlayHeaderComments = overlay.HeaderFacts
                            .Select(fact => $"// {fact.Format()}")
                            .ToList();
                }
                else
                    costOverlayDiagnostic = DiagnosticComment(result);
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

            // Structured Research overlay rows for one method: the table-shaped
            // projection of the same facts the annotated source/IL views render.
            IReadOnlyList<ILInspector.Research.ResearchViews.FactRow>? facts = null;
            if (request.Facts && pipelineSource is not null)
            {
                facts = ILInspector.Research.ResearchViews.CollectFactRows(
                    pipelineSource, lookupType, method.Name, lookupOverloadIndex, publicOnly);
            }

            results.Add((method, new Item(
                loweredBody,
                loweredDiagnostic,
                methodGenericParameters,
                annotatedBody,
                annotatedDiagnostic,
                costOverlayBody,
                costOverlayHeaderComments,
                costOverlayDiagnostic,
                ilText,
                ilDiagnostic,
                attributes,
                facts,
                decompileTrace,
                RequiresAsyncDeclaration: ContainsAwaitKeyword(loweredBody))));
        }

        return results;
    }

    /// <summary>Renders a failed result as comment lines so sections degrade honestly instead of disappearing.</summary>
    static string DiagnosticComment(Decompiler.DecompilerResult result)
        => string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"// {d}"));

    static bool ContainsAwaitKeyword(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        const string keyword = "await";
        var span = text.AsSpan();
        var index = text.IndexOf(keyword, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 ? '\0' : span[index - 1];
            var afterIndex = index + keyword.Length;
            var after = afterIndex >= span.Length ? '\0' : span[afterIndex];
            if (!IsIdentifierPart(before) && !IsIdentifierPart(after))
                return true;
            index = text.IndexOf(keyword, index + keyword.Length, StringComparison.Ordinal);
        }
        return false;
    }

    static bool IsIdentifierPart(char c)
        => c == '_' || char.IsLetterOrDigit(c);

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
        if (!request.DecompiledSource && !request.AnnotatedSource && !request.CostOverlay && !request.Facts)
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

    static Decompiler.DecompilerResult RenderPlainSource(Decompiler.Pipeline.MetadataSource source, string type, string method, Decompiler.Annotations.AnnotationStage stage, int overloadIndex, bool publicOnly)
    {
        try
        {
            var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
                ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
            var result = stage == Decompiler.Annotations.AnnotationStage.Lowered
                ? Decompiler.Pipeline.CSharpPrinter.PrintLowered(imported, target => IrImporter.Import(source, target))
                : Decompiler.Pipeline.CSharpPrinter.PrintRaised(imported, target => IrImporter.Import(source, target));
            if (result.Output is not { } output)
                return result;
            var body = result.ConstructorChain is { } chain
                ? output.Length == 0 ? $": {chain}" : $": {chain}{Environment.NewLine}{output}"
                : output;
            return string.IsNullOrWhiteSpace(body)
                ? Decompiler.DecompilerResult.Failure(Decompiler.DiagnosticIds.EmptyOutput, "projection produced no output for a method with a body")
                : result with { Output = body };
        }
        catch (Exception ex)
        {
            return Decompiler.DecompilerResult.Failure(Decompiler.DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
