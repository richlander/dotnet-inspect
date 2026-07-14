using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Services;
using ILInspector.CSharp;
using ILInspector.Metadata;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

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
    internal sealed record Request(bool DecompiledSource, bool AnnotatedSource, bool CostOverlay, bool SemanticsOverlay, bool IL, bool Attributes, bool Calls, bool Callers, bool CallGraph, bool UnsafeOperations, bool Facts = false, bool FidelityCauses = false, string? ProjectAssetsPath = null, string? TargetFramework = null);

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
        bool RequiresAsyncBodyModifier = false);

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

            // Resolve the member's own metadata handle once (validated against
            // this reader) and address every section by it, so none drifts onto
            // a different overload. A non-validating token — e.g. carried over
            // from a type-forwarded surface — falls back to the name+ordinal
            // path. This is the same drift class the whole-type composition path
            // fixes (see docs/design/member-body-substrate.md).
            var memberHandle = ResolveMethodHandle(reader, typeHandle, method.MetadataToken, method.Name);

            IReadOnlyList<(string Name, string? Value)>? attributes = null;
            if (request.Attributes)
            {
                var found = memberHandle is { } attrHandle
                    ? AttributeReader.GetMethodAttributes(reader, attrHandle)
                    : AttributeReader.GetMethodAttributes(
                        reader, typeHandle, method.Name, lookupOverloadIndex, publicOnly);
                if (found.Count > 0)
                    attributes = found.Select(a => (a.Name, a.Value)).ToList();
            }

            // Method generic parameter names, read straight from metadata, feed
            // the decompiled-source declaration formatter. Sourced here (not from
            // a decompiler pass) so it is available whenever a method body is
            // shown, independent of which sections were requested.
            var methodGenericParameters = memberHandle is { } genHandle
                ? MethodGenericParameterNames(reader, genHandle)
                : MethodGenericParameterNames(
                    reader, typeHandle, method.Name, lookupOverloadIndex, publicOnly);
            var methodHasBody = memberHandle is { } bodyHandle
                ? SelectedMethodHasBody(reader, bodyHandle)
                : SelectedMethodHasBody(
                    reader, typeHandle, method.Name, lookupOverloadIndex, publicOnly);
            bool requiresAsyncBodyModifier = memberHandle is { } asyncHandle
                && CSharpTypeProducer.RequiresAsyncBodyModifier(reader, asyncHandle);

            // Decompiled source: raised C# only, without annotations or interleaved IL.
            Decompiler.DecompilerResult? decompiledResult = null;
            Decompiler.DecompilerResult? projectionResult = null;
            IrFunction? raisedFunction = null;
            if ((request.DecompiledSource || request.FidelityCauses) && pipelineSource is not null)
            {
                projectionResult = TrimOutput(RenderDecompiledSource(
                    pipelineSource,
                    lookupType,
                    method.Name,
                    lookupOverloadIndex,
                    publicOnly,
                    memberHandle ?? default,
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
                        MethodHandle: memberHandle ?? default));
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
                    var instructions = memberHandle is { } ilHandle
                        ? ILInstructionPrinter.DisassembleMethod(peReader, reader, ilHandle)
                        : ILInstructionPrinter.DisassembleMethod(
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
                facts,
                fidelityCauses,
                requiresAsyncBodyModifier)));
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
    /// Handle-addressed generic parameter names: reads directly from the
    /// method's own definition, free of the name+overload-ordinal drift.
    /// </summary>
    static IReadOnlyList<string>? MethodGenericParameterNames(
        MetadataReader reader, MethodDefinitionHandle methodHandle)
    {
        var handles = reader.GetMethodDefinition(methodHandle).GetGenericParameters();
        return handles.Count == 0 ? null : handles.Select(reader.GetGenericParameterName).ToList();
    }

    static bool SelectedMethodHasBody(
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
            if (matchCount++ != overloadIndex)
                continue;
            return method.RelativeVirtualAddress != 0;
        }
        return false;
    }

    static bool SelectedMethodHasBody(MetadataReader reader, MethodDefinitionHandle methodHandle)
        => reader.GetMethodDefinition(methodHandle).RelativeVirtualAddress != 0;

    /// <summary>
    /// Validates a surface member's metadata token to a
    /// <see cref="MethodDefinitionHandle"/> that belongs to
    /// <paramref name="typeHandle"/> in <paramref name="reader"/> and carries
    /// the member's name, or null when the token is absent, is not a method
    /// definition, or does not validate (e.g. a type-forwarded surface whose
    /// token points into another assembly). A null result asks the caller to
    /// fall back to name+ordinal addressing.
    /// </summary>
    static MethodDefinitionHandle? ResolveMethodHandle(
        MetadataReader reader, TypeDefinitionHandle typeHandle, int? token, string memberName)
    {
        if (token is not { } value)
            return null;
        var entity = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(value);
        if (entity.Kind != HandleKind.MethodDefinition)
            return null;
        var methodHandle = (MethodDefinitionHandle)entity;
        MethodDefinition method;
        try
        {
            method = reader.GetMethodDefinition(methodHandle);
        }
        catch
        {
            return null;
        }
        if (method.GetDeclaringType() != typeHandle)
            return null;
        return reader.GetString(method.Name) == memberName ? methodHandle : null;
    }

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
        if (!request.DecompiledSource && !request.AnnotatedSource && !request.CostOverlay && !request.SemanticsOverlay && !request.Facts && !request.FidelityCauses)
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
        bool publicOnly,
        MethodDefinitionHandle methodHandle,
        out IrFunction? imported)
    {
        imported = null;
        try
        {
            imported = (methodHandle.IsNil
                ? IrImporter.Import(source, type, method, overloadIndex, publicOnly)
                : IrImporter.Import(source, methodHandle))
                ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
            var result = Decompiler.Pipeline.CSharpPrinter.PrintRaised(
                imported,
                target => IrImporter.Import(source, target));
            return result;
        }
        catch (Exception ex)
        {
            return Decompiler.DecompilerResult.Failure(Decompiler.DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
