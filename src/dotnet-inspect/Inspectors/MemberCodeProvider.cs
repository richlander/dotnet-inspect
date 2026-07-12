using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Services;
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
    internal sealed record Request(bool DecompiledSource, bool AnnotatedSource, bool CostOverlay, bool SemanticsOverlay, bool IL, bool Attributes, bool Calls, bool Callers, bool CallGraph, bool UnsafeOperations, bool Facts = false, string? ProjectAssetsPath = null, string? TargetFramework = null);

    /// <summary>
    /// Code content for one member. C# sections retain the complete decompiler
    /// result so declaration formatting consumes typed constructor and async
    /// evidence instead of recovering it from rendered text.
    /// </summary>
    internal sealed record Item(
        Decompiler.DecompilerResult? DecompiledResult,
        IReadOnlyList<string>? MethodGenericParameters,
        Decompiler.DecompilerResult? AnnotatedResult,
        Decompiler.DecompilerResult? CostOverlayResult,
        IReadOnlyList<string>? CostOverlayHeaderComments,
        Decompiler.DecompilerResult? SemanticsOverlayResult,
        string? ILText,
        string? ILDiagnostic,
        IReadOnlyList<(string Name, string? Value)>? Attributes,
        IReadOnlyList<ILInspector.Research.ResearchViews.FactRow>? Facts = null);

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
            
        // Read metadata/IL through the assembly seam (which owns the single PE open) rather than
        // opening a raw PEReader here — the decompiler-backed sections still open their own
        // MetadataSource below (with symbols) via OpenPipelineSource.
        using var image = ILInspector.Metadata.AssemblyInspectionSession.Open(dllPath);
        if (!image.HasMetadata)
            return results;

        var peReader = image.PeReader;
        var reader = image.MetadataReader;

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
            Decompiler.DecompilerResult? decompiledResult = null;
            if (request.DecompiledSource && pipelineSource is not null)
            {
                decompiledResult = TrimOutput(RenderDecompiledSource(
                    pipelineSource,
                    lookupType,
                    method.Name,
                    lookupOverloadIndex,
                    publicOnly));
                decompiledResult = decompiledResult with
                {
                    Trace = new Decompiler.DecompilerTrace(
                        decompiledResult.Fidelity,
                        pipelineSource.Symbols,
                        decompiledResult.Diagnostics)
                };
            }

            ILInspector.Research.ResearchViews.MemberProjectionResult? researchProjection = null;
            if ((request.AnnotatedSource || request.CostOverlay || request.SemanticsOverlay || request.Facts) && pipelineSource is not null)
            {
                researchProjection = ILInspector.Research.ResearchViews.ProjectMember(
                    new ILInspector.Research.ResearchViews.MemberProjectionRequest(
                        pipelineSource,
                        lookupType,
                        method.Name,
                        lookupOverloadIndex,
                        publicOnly,
                        AnnotatedSource: request.AnnotatedSource,
                        CostOverlay: request.CostOverlay,
                        SemanticsOverlay: request.SemanticsOverlay,
                        FactRows: request.Facts));
            }

            // Annotated source: raised C# with hidden-fact comments and the
            // raw IL interleaved beneath each statement.
            var annotatedResult = request.AnnotatedSource
                && researchProjection?.AnnotatedSource is { } annotated
                    ? TrimOutput(annotated)
                    : null;

            Decompiler.DecompilerResult? costOverlayResult = null;
            IReadOnlyList<string>? costOverlayHeaderComments = null;
            if (request.CostOverlay && researchProjection?.CostOverlay is { } overlay)
            {
                costOverlayResult = TrimOutput(overlay.Body);
                if (costOverlayResult.Output is not null)
                {
                    if (overlay.HeaderFacts.Count > 0)
                        costOverlayHeaderComments = overlay.HeaderFacts
                            .Select(fact => $"// {fact.Format()}")
                            .ToList();
                }
            }

            var semanticsOverlayResult = request.SemanticsOverlay
                && researchProjection?.SemanticsOverlay is { } semantics
                    ? TrimOutput(semantics)
                    : null;

            string? ilText = null, ilDiagnostic = null;
            if (request.IL)
            {
                try
                {
                    var instructions = ILInstructionPrinter.DisassembleMethod(
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
            if (request.Facts && researchProjection is not null)
                facts = researchProjection.Facts;

            results.Add((method, new Item(
                decompiledResult,
                methodGenericParameters,
                annotatedResult,
                costOverlayResult,
                costOverlayHeaderComments,
                semanticsOverlayResult,
                ilText,
                ilDiagnostic,
                attributes,
                facts)));
        }

        return results;
    }

    static Decompiler.DecompilerResult TrimOutput(Decompiler.DecompilerResult result)
        => result.Output is { } output
            ? result with { Output = output.TrimEnd() }
            : result;

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
        if (!request.DecompiledSource && !request.AnnotatedSource && !request.CostOverlay && !request.SemanticsOverlay && !request.Facts)
            return null;
        try
        {
            var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(dllPath)
            {
                ProjectAssetsPath = request.ProjectAssetsPath,
                TargetFramework = request.TargetFramework,
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
            return Decompiler.Pipeline.MetadataSource.Open(dllPath, pdbPath, resolver);
        }
        catch
        {
            return null;
        }
    }

    static Decompiler.DecompilerResult RenderDecompiledSource(
        Decompiler.Pipeline.MetadataSource source,
        string type,
        string method,
        int overloadIndex,
        bool publicOnly)
    {
        try
        {
            var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
                ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
            var result = Decompiler.Pipeline.CSharpPrinter.PrintRaised(
                imported,
                target => IrImporter.Import(source, target));
            if (result.Output is not { } output)
                return result;
            return string.IsNullOrWhiteSpace(output) && result.ConstructorChain is null
                ? Decompiler.DecompilerResult.Failure(Decompiler.DiagnosticIds.EmptyOutput, "projection produced no output for a method with a body")
                : result;
        }
        catch (Exception ex)
        {
            return Decompiler.DecompilerResult.Failure(Decompiler.DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
