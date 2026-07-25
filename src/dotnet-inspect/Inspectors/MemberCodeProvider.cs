using DotnetInspector.Services;
using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

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
    internal sealed record Request(bool DecompiledSource, bool AnnotatedSource, bool CostOverlay, bool SemanticsOverlay, bool IL, bool Attributes, bool Calls, bool Callers, bool CallGraph, bool UnsafeOperations, bool Facts = false, bool FidelityCauses = false, bool AppliedTaste = false, string? ProjectAssetsPath = null, string? TargetFramework = null);

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
        IReadOnlyList<ILInspector.Research.ResearchViews.FactRow>? Facts = null,
        FindingInspection<Decompiler.DecompilerFidelityCause>? FidelityCauses = null,
        IReadOnlyList<Decompiler.DecompilerDecision>? AppliedTaste = null,
        bool RequiresAsyncBodyModifier = false);

    internal static List<(ApiMember Member, Item Code)> Collect(
        ApiType type, List<ApiMember> methods, string dllPath, int? overloadIndex,
        Request request, string? pdbPath = null, bool includeAll = false,
        PrinterOptions? renderOptions = null)
    {
        var results = new List<(ApiMember, Item)>();
        
        // Sections that require a single selected method (IL, decompiled source, etc.)
        // are skipped when no overload index is provided. Callers works across all overloads
        // and is handled separately in PopulateIndexSections.
        if (!overloadIndex.HasValue)
            return results;
            
        // Read metadata/IL through the assembly seam; decompiler-backed sections
        // still own their symbol-aware MetadataSource below.
        using var image = ILInspector.Metadata.AssemblyInspectionSession.Open(dllPath);
        if (!image.HasMetadata)
            return results;

        var bodySource = image.MethodBodies;

        // All decompiler-backed sections (decompiled source, annotated source, IR
        // stages) read through one MetadataSource that owns its own readers.
        // A malformed-metadata failure opening it degrades those sections to
        // empty — the IL/attribute sections still render — instead of throwing.
        using var pipelineSource = OpenPipelineSource(request, dllPath, pdbPath);

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

            if (!bodySource.ContainsType(lookupType))
                continue;

            // Resolve the member's own metadata token once (validated against
            // this source) and address every section by it, so none drifts onto
            // a different overload. A non-validating token — e.g. carried over
            // from a type-forwarded surface — falls back to the name+ordinal
            // path, resolved to its concrete handle before any projection. This
            // is the same drift class the whole-type composition path fixes
            // (see docs/design/member-body-substrate.md).
            var selection = bodySource.ResolveMethod(
                lookupType,
                method.Name,
                lookupOverloadIndex,
                publicOnly,
                method.MetadataToken);
            int? methodToken = selection?.MetadataToken;

            IReadOnlyList<(string Name, string? Value)>? attributes = null;
            if (request.Attributes && selection?.Attributes.Count > 0)
            {
                attributes = selection.Attributes;
            }

            // Method generic parameter names, read straight from metadata, feed
            // the decompiled-source declaration formatter. Sourced here (not from
            // a decompiler pass) so it is available whenever a method body is
            // shown, independent of which sections were requested.
            var methodGenericParameters = selection?.GenericParameterNames;
            bool methodHasBody = selection?.HasBody == true;
            bool requiresAsyncBodyModifier = selection is not null
                && TypeShellProducer.RequiresAsyncBodyModifier(selection);

            // Decompiled source: raised C# only, without annotations or interleaved IL.
            Decompiler.DecompilerResult? decompiledResult = null;
            Decompiler.DecompilerResult? projectionResult = null;
            IrFunction? raisedFunction = null;
            if ((request.DecompiledSource || request.FidelityCauses || request.AppliedTaste) && pipelineSource is not null)
            {
                // The style options (renderOptions) affect the printed C# string
                // (Decompiled Source) and the set of configurable render choices
                // that fire (Applied Taste) -- notably the opt-in byte-divergent
                // lenses, whose applied-lens decisions only exist when the render
                // runs with those options. Both consume the config. A fidelity-only
                // projection reads the raised IR and recompile diagnostics (both
                // style-invariant) and discards the printed string, so it must not
                // consume the config -- pass the shipped defaults there. The
                // config-warning latch still keys off a surfaced Decompiled Source
                // Output (below), so an Applied-Taste-only run that renders no
                // styled source stays silent.
                var projectionRenderOptions = request.DecompiledSource || request.AppliedTaste ? renderOptions : null;
                projectionResult = TrimOutput(RenderDecompiledSource(
                    pipelineSource,
                    lookupType,
                    method.Name,
                    lookupOverloadIndex,
                    publicOnly,
                    methodToken,
                    projectionRenderOptions,
                    out raisedFunction));
                projectionResult = projectionResult with
                {
                    Trace = new Decompiler.DecompilerTrace(
                        projectionResult.Fidelity,
                        pipelineSource.Symbols,
                        projectionResult.Diagnostics)
                };
            }

            if (request.DecompiledSource)
                decompiledResult = projectionResult;

            IReadOnlyList<Decompiler.DecompilerDecision>? appliedTaste = null;
            if (request.AppliedTaste)
                appliedTaste = projectionResult?.Decisions ?? [];

            FindingInspection<Decompiler.DecompilerFidelityCause>? fidelityCauses = null;
            if (request.FidelityCauses)
            {
                var subject = new FindingSubject(
                    method.MetadataToken is { } token
                        ? $"{Path.GetFullPath(dllPath)}#0x{token:X8}"
                        : $"{Path.GetFullPath(dllPath)}::{lookupType}.{method.Name}#{lookupOverloadIndex}",
                    $"{lookupType}.{method.Name}");
                if (pipelineSource is null)
                {
                    fidelityCauses = new FindingInspection<Decompiler.DecompilerFidelityCause>.Failed(
                        new InspectionError(
                            subject,
                            Decompiler.DecompilerFindings.FidelityInspectionDescriptor,
                            "Decompiler metadata source could not be opened."));
                }
                else
                {
                    fidelityCauses = BuildFidelityCauseInspection(
                        methodHasBody,
                        raisedFunction,
                        projectionResult,
                        subject);
                }
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
                        FactRows: request.Facts,
                        MethodToken: methodToken));
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
                    List<ILInstructionText>? instructions = null;
                    if (methodToken is { } token
                        && bodySource.TryRead(token, out var body, out _))
                    {
                        var decoded = MethodInstructions.Decode(body!);
                        instructions = InstructionProducer.Render(decoded, bodySource);
                    }
                    if (instructions is { Count: > 0 })
                    {
                        // Adopt the offset-anchored SourceLine currency: raw disassembly is
                        // already display-ready text plus an IL offset, so wrap each instruction
                        // as a SourceLine (Text=raw instr, Offset=IL offset) and join over the
                        // text. Byte-identical to the prior instr.ToString() join; the anchor
                        // keeps the IL section addressable for later correlation.
                        IReadOnlyList<Decompiler.SourceLine> ilLines = instructions
                            .Select(i => new Decompiler.SourceLine(i.ToString(), i.Offset))
                            .ToList();
                        ilText = string.Join(Environment.NewLine, ilLines.Select(line => line.Text));
                    }
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
                facts,
                fidelityCauses,
                appliedTaste,
                requiresAsyncBodyModifier)));
        }

        return results;
    }

    static Decompiler.DecompilerResult TrimOutput(Decompiler.DecompilerResult result)
        => result.Output is { } output
            ? result with { Output = output.TrimEnd() }
            : result;

    internal static FindingInspection<Decompiler.DecompilerFidelityCause> BuildFidelityCauseInspection(
        bool methodHasBody,
        IrFunction? raisedFunction,
        Decompiler.DecompilerResult? projection,
        FindingSubject subject)
    {
        if (!methodHasBody)
            return Decompiler.DecompilerFindings.InspectFidelityCauses(null, subject);

        if (raisedFunction is not null && projection?.Succeeded == true)
            return Decompiler.DecompilerFindings.InspectFidelityCauses(raisedFunction, subject);

        return new FindingInspection<Decompiler.DecompilerFidelityCause>.Failed(
            new InspectionError(
                subject,
                Decompiler.DecompilerFindings.FidelityInspectionDescriptor,
                string.Join(
                    "; ",
                    projection?.Diagnostics.Select(static diagnostic => diagnostic.ToString())
                        ?? ["Decompiler import or projection failed."])));
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
        if (!request.DecompiledSource && !request.AnnotatedSource && !request.CostOverlay && !request.SemanticsOverlay && !request.Facts && !request.FidelityCauses && !request.AppliedTaste)
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
                AllowPlatformAssemblyVersionRollForward = true,
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
        bool publicOnly,
        int? methodToken,
        PrinterOptions? renderOptions,
        out IrFunction? imported)
    {
        imported = null;
        try
        {
            imported = (methodToken is null
                ? IrImporter.Import(source, type, method, overloadIndex, publicOnly)
                : IrImporter.Import(source, methodToken.Value))
                ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
            var result = Decompiler.Pipeline.CSharpPrinter.PrintRaised(
                imported,
                target => IrImporter.Import(source, target),
                options: renderOptions,
                typesProvablyDisjoint: source.AreProvablyDisjoint);
            return result;
        }
        catch (Exception ex)
        {
            return Decompiler.DecompilerResult.Failure(Decompiler.DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
