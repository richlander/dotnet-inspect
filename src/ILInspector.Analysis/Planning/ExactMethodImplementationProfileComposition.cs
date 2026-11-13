using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Compatibility profile projection over one exact Method Query Source
/// execution.
/// </summary>
public sealed record ExactMethodImplementationProfileComposition(
    ImplementationProfilePopulationCoverageReceipt Coverage,
    ImmutableArray<MethodImplementationProfile> Profiles,
    ImmutableArray<OverloadCallRelationship> OverloadRelationships,
    ImmutableHashSet<TypeRef> GeneratedFrameworkTypes,
    ImmutableArray<AnalysisDiagnostic> Diagnostics);

/// <summary>
/// Composes complete implementation profiles from independently owned exact
/// Method facts without becoming a Method Query Source producer.
/// </summary>
public static class ExactMethodImplementationProfileComposer
{
    public static ExactMethodImplementationProfileComposition Compose(
        ExactMethodAnalysisBundleProducerResult result,
        MethodDefinitionSourceReceipt? sourceReceipt,
        IEnumerable<int> declaredMethodTokens,
        IEnumerable<AnalysisDiagnostic>? additionalDiagnostics = null,
        IEnumerable<MethodDefinitionGeneratedExpansionOrigin>?
            generatedExpansionOrigins = null,
        IEnumerable<int>? unavailableMethodTokens = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(declaredMethodTokens);
        if (sourceReceipt is null
            && generatedExpansionOrigins is null)
        {
            throw new ArgumentException(
                "Implementation profile composition requires source "
                    + "generated-body evidence.",
                nameof(generatedExpansionOrigins));
        }

        ImmutableHashSet<int> declaredTokens =
        [
            .. declaredMethodTokens,
        ];
        if (declaredTokens.IsEmpty)
        {
            throw new ArgumentException(
                "Implementation profile composition requires at least one "
                    + "declared MethodDef.",
                nameof(declaredMethodTokens));
        }

        Dictionary<int, MethodIdentity> identitiesByToken =
            result.Bodies
                .Where(static body => body.Method.Method is not null)
                .ToDictionary(
                    static body => body.Method.MethodToken,
                    static body => body.Method.Method!);
        Dictionary<int, MethodDefinitionGeneratedExpansionOrigin>
            generatedOrigins =
                (generatedExpansionOrigins
                    ?? sourceReceipt!.Coverage.GeneratedExpansion.Origins)
                    .GroupBy(static origin =>
                        MetadataTokens.GetToken(origin.Method))
                    .ToDictionary(
                        static group => group.Key,
                        static group => group.First());

        ImmutableArray<MethodIdentity> declaredMethods =
        [
            .. declaredTokens
                .Select(token =>
                    identitiesByToken.GetValueOrDefault(token))
                .OfType<MethodIdentity>()
                .OrderBy(static method =>
                    method.MetadataToken),
        ];
        var diagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        foreach (ExactMethodAnalysisBundleBody body in result.Bodies)
        {
            foreach (AnalysisDiagnostic diagnostic
                in body.Method.Diagnostics)
            {
                diagnostics.Add(
                    WithGeneratedSource(
                        diagnostic,
                        body,
                        generatedOrigins,
                        identitiesByToken));
            }
            foreach (AnalysisDiagnostic diagnostic
                in body.Optimization.Allocation.Diagnostics)
            {
                diagnostics.Add(
                    WithGeneratedSource(
                        diagnostic,
                        body,
                        generatedOrigins,
                        identitiesByToken));
            }
            if (body.Method.ExceptionRegionError is { } exceptionError)
            {
                diagnostics.Add(
                    WithGeneratedSource(
                        new(
                            body.Method.MethodToken,
                            MethodLabel(body),
                            exceptionError),
                        body,
                        generatedOrigins,
                        identitiesByToken));
            }
        }
        if (additionalDiagnostics is not null)
            diagnostics.AddRange(additionalDiagnostics);
        if (sourceReceipt is not null)
        {
            AddSourceDiagnostics(
                sourceReceipt,
                identitiesByToken,
                declaredTokens,
                diagnostics);
        }

        var metricBodies =
            ImmutableArray.CreateBuilder<
                MethodImplementationMetricEvidence>();
        var managedBodies =
            ImmutableArray.CreateBuilder<MethodIdentity>();
        var normalizedCalls =
            ImmutableArray.CreateBuilder<DirectCall>();
        var physicalCalls =
            ImmutableArray.CreateBuilder<DirectCall>();
        var physicalMethods =
            ImmutableArray.CreateBuilder<MethodIdentity>();

        foreach (ExactMethodAnalysisBundleBody body
            in result.Bodies.OrderBy(
                static candidate =>
                    candidate.Method.MethodToken))
        {
            if (body.Method.Method is not { } evidenceMethod)
                continue;

            physicalMethods.Add(evidenceMethod);
            physicalCalls.AddRange(body.DirectCalls);
            MethodIdentity? method =
                LogicalMethod(
                    evidenceMethod,
                    generatedOrigins,
                    identitiesByToken);
            if (method is null)
            {
                diagnostics.Add(
                    new(
                        evidenceMethod.MetadataToken,
                        ExactMethodAnalysisProducer.MethodLabel(
                            evidenceMethod),
                        "Generated MethodDef owner evidence was unavailable."));
                continue;
            }

            ImmutableArray<DirectCall> calls =
            [
                .. body.DirectCalls.Select(call =>
                    call with
                    {
                        Caller = method,
                        EvidenceMethod = evidenceMethod,
                    }),
            ];
            normalizedCalls.AddRange(calls);

            if (!body.Method.HasManagedBody)
                continue;

            managedBodies.Add(evidenceMethod);
            if (body.Method.Context is not { } context
                || body.Method.EncodedIlBytes is not { } encodedIlBytes
                || body.Method.ExceptionRegionError is not null)
            {
                continue;
            }

            MethodImplementationContextMeasurements measurements =
                MethodImplementationProfileAnalysis.MeasureContext(
                    context,
                    includeInstructionShape: true,
                    includeControlFlow: true,
                    includeDirectCallCount: true,
                    includeCallSiteCount: false);
            ImplementationMetricDirectCalls? directCalls =
                MeasureDirectCalls(
                    body,
                    measurements.DirectCallCount?.Count ?? 0);
            metricBodies.Add(
                new(
                    method,
                    evidenceMethod,
                    encodedIlBytes,
                    ExceptionRegions(body.Method.ExceptionRegions),
                    new(
                        context.LocalCount,
                        context.LocalTypesIncompleteReason),
                    measurements.InstructionShape,
                    measurements.ControlFlow,
                    measurements.DirectCallCount,
                    measurements.CallSiteCount,
                    directCalls)
                {
                    IsAsync =
                        generatedOrigins.TryGetValue(
                            evidenceMethod.MetadataToken,
                            out MethodDefinitionGeneratedExpansionOrigin?
                                origin)
                        && origin.IsAsyncStateMachine,
                    AllocationCount = body.Signals.Allocations,
                    ThrowCount = body.Signals.Throws,
                    Unsafe = body.Signals.Unsafe,
                    ReflectionCallCount = body.Signals.Reflection,
                    IncompleteReasons =
                        MethodImplementationProfileAnalysis
                            .IncompleteReasons(context),
                    DirectCallCollectionAttempted =
                        body.Method.CallSites is not null,
                    DirectCallCollectionComplete =
                        body.Method.CallSites?.Failure is null,
                });
        }

        ImmutableArray<OverloadCallRelationship> relationships =
            MethodImplementationProfileAnalysis
                .CollectOverloadRelationships(
                    declaredMethods,
                    normalizedCalls.ToImmutable());
        Dictionary<(int Method, int Evidence), int>
            outgoingOverloadTargetCounts =
                CollectOutgoingOverloadTargetCounts(
                    result.Bodies,
                    generatedOrigins,
                    identitiesByToken);
        ImmutableArray<AnalysisDiagnostic> distinctDiagnostics =
        [
            .. diagnostics.Distinct(),
        ];
        ImmutableArray<MethodImplementationProfile> profiles =
            MethodImplementationProfileAnalysis
                .CollectFocusedCompleteProfiles(
                    metricBodies.ToImmutable(),
                    relationships,
                    distinctDiagnostics,
                    outgoingOverloadTargetCounts);
        ImmutableArray<MethodIdentity> profiledEvidenceBodies =
        [
            .. profiles
                .Select(static profile => profile.EvidenceMethod)
                .DistinctBy(static method =>
                    method.MetadataToken)
                .OrderBy(static method =>
                    method.MetadataToken),
        ];
        HashSet<int> profiledTokens =
        [
            .. profiledEvidenceBodies.Select(static method =>
                method.MetadataToken),
        ];
        Dictionary<int, AnalysisDiagnostic> diagnosticsByToken =
            distinctDiagnostics
                .GroupBy(static diagnostic =>
                    diagnostic.MethodToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.First());
        ImmutableArray<ImplementationProfileUnavailableBody>
            unavailableBodies =
        [
            .. result.Bodies
                .Where(static body =>
                    body.Method.Method is null
                    && body.Method.HasManagedBody)
                .Select(body =>
                {
                    AnalysisDiagnostic? diagnostic =
                        diagnosticsByToken.GetValueOrDefault(
                            body.Method.MethodToken);
                    return new ImplementationProfileUnavailableBody(
                        EvidenceMethod: null,
                        body.Method.MethodToken,
                        diagnostic is null
                            ? ImplementationProfileUnavailableReason
                                .ProfileUnavailable
                            : ImplementationProfileUnavailableReason
                                .AnalysisFailed,
                        diagnostic);
                }),
            .. (unavailableMethodTokens ?? [])
                .Where(token =>
                    result.Bodies.All(body =>
                        body.Method.MethodToken != token))
                .Distinct()
                .Order()
                .Select(token =>
                {
                    AnalysisDiagnostic? diagnostic =
                        diagnosticsByToken.GetValueOrDefault(token);
                    return new ImplementationProfileUnavailableBody(
                        EvidenceMethod: null,
                        token,
                        diagnostic is null
                            ? ImplementationProfileUnavailableReason
                                .ProfileUnavailable
                            : ImplementationProfileUnavailableReason
                                .AnalysisFailed,
                        diagnostic);
                }),
            .. managedBodies
                .Where(method =>
                    !profiledTokens.Contains(method.MetadataToken))
                .DistinctBy(static method =>
                    method.MetadataToken)
                .OrderBy(static method =>
                    method.MetadataToken)
                .Select(method =>
                {
                    AnalysisDiagnostic? diagnostic =
                        diagnosticsByToken.GetValueOrDefault(
                            method.MetadataToken);
                    return new ImplementationProfileUnavailableBody(
                        method,
                        diagnostic is null
                            ? ImplementationProfileUnavailableReason
                                .ProfileUnavailable
                            : ImplementationProfileUnavailableReason
                                .AnalysisFailed,
                        diagnostic);
                }),
        ];
        var coverage =
            new ImplementationProfilePopulationCoverageReceipt(
                WasRequested: true,
                HasFullMethodEvidenceScope: false,
                declaredMethods,
                [
                    .. managedBodies
                        .DistinctBy(static method =>
                            method.MetadataToken)
                        .OrderBy(static method =>
                            method.MetadataToken),
                ],
                profiledEvidenceBodies,
                unavailableBodies,
                distinctDiagnostics);
        return new(
            coverage,
            profiles,
            relationships,
            GeneratedFrameworkTypeAnalysis.Collect(
                physicalCalls.ToImmutable(),
                physicalMethods.ToImmutable()).ToImmutableHashSet(),
            distinctDiagnostics);
    }

    static MethodIdentity? LogicalMethod(
        MethodIdentity evidenceMethod,
        IReadOnlyDictionary<
            int,
            MethodDefinitionGeneratedExpansionOrigin> generatedOrigins,
        IReadOnlyDictionary<int, MethodIdentity> identitiesByToken)
    {
        if (!generatedOrigins.TryGetValue(
                evidenceMethod.MetadataToken,
                out MethodDefinitionGeneratedExpansionOrigin? origin))
        {
            return evidenceMethod;
        }

        return identitiesByToken.GetValueOrDefault(
            MetadataTokens.GetToken(origin.DeclaredOwner));
    }

    static AnalysisDiagnostic WithGeneratedSource(
        AnalysisDiagnostic diagnostic,
        ExactMethodAnalysisBundleBody body,
        IReadOnlyDictionary<
            int,
            MethodDefinitionGeneratedExpansionOrigin> generatedOrigins,
        IReadOnlyDictionary<int, MethodIdentity> identitiesByToken)
    {
        if (diagnostic.SourceMethodToken is not null
            || !generatedOrigins.TryGetValue(
                body.Method.MethodToken,
                out MethodDefinitionGeneratedExpansionOrigin? origin))
        {
            return diagnostic;
        }

        int sourceToken =
            MetadataTokens.GetToken(origin.DeclaredOwner);
        MethodIdentity? source =
            identitiesByToken.GetValueOrDefault(sourceToken);
        return diagnostic with
        {
            SourceMethodToken = sourceToken,
            DeclaringType =
                diagnostic.DeclaringType
                ?? body.Method.Method?.DeclaringType,
            SourceDeclaringType =
                diagnostic.SourceDeclaringType
                ?? source?.DeclaringType,
        };
    }

    static ImplementationMetricExceptionRegionCounts ExceptionRegions(
        ImmutableArray<MethodExceptionRegionInfo> regions) =>
        new(
            regions.Count(static region =>
                region.Clause == "catch"),
            regions.Count(static region =>
                region.Clause == "filter"),
            regions.Count(static region =>
                region.Clause == "finally"),
            regions.Count(static region =>
                region.Clause == "fault"));

    static ImplementationMetricDirectCalls? MeasureDirectCalls(
        ExactMethodAnalysisBundleBody body,
        int invocationCount)
    {
        if (body.Method.CallSites is not { } callSites)
            return null;

        var currentModuleTargets = new HashSet<int>();
        var otherTargets = new HashSet<MemberRef>();
        foreach (MethodCallSite site in callSites.Sites)
        {
            if (!MethodImplementationProfileAnalysis
                .IsInvocation(site.Kind))
            {
                continue;
            }

            if (site.Target
                is MethodCallTarget.CurrentModule currentModule)
            {
                currentModuleTargets.Add(
                    MetadataTokens.GetToken(
                        currentModule.Method));
            }
            else
            {
                otherTargets.Add(site.Callee);
            }
        }

        string? incompleteReason =
            callSites.Failure is { } failure
                ? $"{nameof(BadImageFormatException)}: "
                    + failure.Message
                : null;
        return new(
            invocationCount,
            currentModuleTargets.Count + otherTargets.Count,
            incompleteReason);
    }

    static Dictionary<(int Method, int Evidence), int>
        CollectOutgoingOverloadTargetCounts(
            ImmutableArray<ExactMethodAnalysisBundleBody> bodies,
            IReadOnlyDictionary<
                int,
                MethodDefinitionGeneratedExpansionOrigin> generatedOrigins,
            IReadOnlyDictionary<int, MethodIdentity> identitiesByToken)
    {
        var targetTokens = new Dictionary<
            (int Method, int Evidence),
            HashSet<int>>();
        foreach (ExactMethodAnalysisBundleBody body in bodies)
        {
            if (body.Method.Method is not { } evidenceMethod
                || body.Method.CallSites is not { } callSites
                || LogicalMethod(
                    evidenceMethod,
                    generatedOrigins,
                    identitiesByToken) is not { } caller)
            {
                continue;
            }

            Dictionary<int, MethodCallSite> sitesByOffset =
                callSites.Sites.ToDictionary(
                    static site => site.ILOffset);
            foreach (DirectCall call in body.DirectCalls)
            {
                if (!MethodImplementationProfileAnalysis
                        .IsInvocation(call.Kind)
                    || !sitesByOffset.TryGetValue(
                        call.ILOffset,
                        out MethodCallSite? site)
                    || !TryCurrentModuleTargetToken(
                        call,
                        site,
                        out int targetToken)
                    || targetToken == caller.MetadataToken
                    || call.Callee.Name != caller.Name
                    || !MethodImplementationProfileAnalysis
                        .SameDeclaringType(
                            caller.DeclaringType,
                            call.Callee.DeclaringType))
                {
                    continue;
                }

                var key = (
                    caller.MetadataToken,
                    evidenceMethod.MetadataToken);
                if (!targetTokens.TryGetValue(
                        key,
                        out HashSet<int>? targets))
                {
                    targets = [];
                    targetTokens[key] = targets;
                }
                targets.Add(targetToken);
            }
        }

        return targetTokens.ToDictionary(
            static entry => entry.Key,
            static entry => entry.Value.Count);
    }

    static bool TryCurrentModuleTargetToken(
        DirectCall call,
        MethodCallSite site,
        out int targetToken)
    {
        if (site.Target
            is MethodCallTarget.CurrentModule currentModule)
        {
            targetToken =
                MetadataTokens.GetToken(currentModule.Method);
            return true;
        }

        EntityHandle operand =
            MetadataTokens.EntityHandle(call.OperandToken);
        EntityHandle definition =
            MetadataTokens.EntityHandle(
                call.CalleeDefinitionToken);
        if (operand.Kind == HandleKind.MethodSpecification
            && definition.Kind == HandleKind.MethodDefinition)
        {
            targetToken = call.CalleeDefinitionToken;
            return true;
        }

        targetToken = 0;
        return false;
    }

    static void AddSourceDiagnostics(
        MethodDefinitionSourceReceipt sourceReceipt,
        IReadOnlyDictionary<int, MethodIdentity> identitiesByToken,
        IReadOnlySet<int> declaredTokens,
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics)
    {
        int? sourceMethodToken =
            declaredTokens.Count == 0
                ? null
                : declaredTokens.Min();
        if (sourceReceipt.SourceFailure is { } failure)
        {
            diagnostics.Add(
                new(
                    failure.UnitToken,
                    failure.Unit,
                    failure.Message,
                    sourceMethodToken));
        }

        MethodDefinitionGeneratedExpansionCoverage expansion =
            sourceReceipt.Coverage.GeneratedExpansion;
        if (expansion.ReachedLimit is { } expansionLimit)
        {
            int token = expansion.ReachedAtToken ?? 0;
            diagnostics.Add(
                new(
                    token,
                    MethodLabel(token, identitiesByToken),
                    "Generated-body expansion reached its "
                        + $"{expansionLimit} limit.",
                    sourceMethodToken));
        }

        MethodDefinitionTerminalWorkCoverage terminal =
            sourceReceipt.Coverage.TerminalWork;
        if (terminal.ReachedLimit is { } terminalLimit)
        {
            int token = terminal.ReachedAtMethodToken ?? 0;
            diagnostics.Add(
                new(
                    token,
                    MethodLabel(token, identitiesByToken),
                    "Exact Method profile work reached its "
                        + $"{terminalLimit} limit.",
                    sourceMethodToken));
        }
    }

    static string MethodLabel(
        ExactMethodAnalysisBundleBody body) =>
        body.Method.Method is { } method
            ? ExactMethodAnalysisProducer.MethodLabel(method)
            : $"0x{body.Method.MethodToken:X8}";

    static string MethodLabel(
        int token,
        IReadOnlyDictionary<int, MethodIdentity> identitiesByToken) =>
        identitiesByToken.TryGetValue(
            token,
            out MethodIdentity? method)
            ? ExactMethodAnalysisProducer.MethodLabel(method)
            : $"0x{token:X8}";
}
