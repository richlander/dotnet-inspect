using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

/// <summary>
/// Runs the closed Research-local producer catalog over one complete target
/// resolution and publishes only a fully accounted inert completion.
/// </summary>
public static class ResearchProducerSession
{
    /// <summary>Runs one producer session sequentially.</summary>
    public static ResearchProducerSessionOutcome Run(
        ResearchProducerSessionRequest request,
        CancellationToken cancellationToken = default)
        => Run(request, NativeProducerInvoker.Instance, cancellationToken);

    internal static ResearchProducerSessionOutcome Run(
        ResearchProducerSessionRequest request,
        IResearchProducerInvoker invoker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(invoker);

        if (ResearchProducerSessionValidator.ValidateRequest(
                request,
                out ImmutableArray<ResearchProducerKind> producers) is
            ResearchProducerRejection rejection)
        {
            return new ResearchProducerSessionOutcome.Rejected(rejection);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ResearchProducerSessionOutcome.Cancelled([]);
        }

        var session = new ResearchProducerSessionId(
            request.Population.Operation,
            request.Identity);
        ImmutableArray<ResearchProducerWorkItem> workItems =
            DeriveWorkItems(session, request.Resolution, producers);
        var results = ImmutableArray.CreateBuilder<ResearchProducerWorkResult>(
            workItems.Length);
        var stages = new Dictionary<ResearchComparisonInputId, StageAccess>(
            ReferenceEqualityComparer.Instance);
        var acquired = new List<InputStage>();
        ResearchProducerDiagnostic? firstFailure = null;
        bool cancelled = false;
        ImmutableArray<ResearchProducerCleanupOutcome> cleanup = [];
        try
        {
            for (int index = 0; index < workItems.Length; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                ResearchProducerWorkItem item = workItems[index];
                ResearchProducerWorkOutcome outcome = Execute(
                    request.Population,
                    request.Resolution,
                    item,
                    stages,
                    acquired,
                    invoker);
                results.Add(new ResearchProducerWorkResult(item, outcome));
                if (outcome is ResearchProducerWorkOutcome.Failed
                    {
                        Diagnostic.Kind:
                            ResearchProducerDiagnosticKind
                                .ProducerContractViolation,
                    } failed)
                {
                    firstFailure ??= failed.Diagnostic;
                }

                if (index + 1 < workItems.Length
                    && cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception exception) when (IsRecoverableExecutionFailure(exception))
        {
            firstFailure ??= new ResearchProducerDiagnostic(
                ResearchProducerDiagnosticKind.ResearchExecutionFailed);
        }
        finally
        {
            cleanup = Cleanup(acquired);
        }

        if (cancelled)
            return new ResearchProducerSessionOutcome.Cancelled(cleanup);

        if (cleanup.Any(
                static outcome =>
                    outcome is ResearchProducerCleanupOutcome.Failed))
        {
            return new ResearchProducerSessionOutcome.Failed(
                new ResearchProducerDiagnostic(
                    ResearchProducerDiagnosticKind.CleanupFailed),
                cleanup);
        }

        if (firstFailure is not null)
        {
            return new ResearchProducerSessionOutcome.Failed(
                firstFailure,
                cleanup);
        }

        if (!ResearchProducerSessionValidator.TryCreateCompletion(
                request,
                session,
                workItems,
                results.ToImmutable(),
                [.. acquired.Select(static stage => stage.Input)],
                cleanup,
                out ResearchProducerCompletion? completion))
        {
            return new ResearchProducerSessionOutcome.Failed(
                new ResearchProducerDiagnostic(
                    ResearchProducerDiagnosticKind.CompletionValidationFailed),
                cleanup);
        }

        return new ResearchProducerSessionOutcome.Completed(completion!);
    }

    static ImmutableArray<ResearchProducerWorkItem> DeriveWorkItems(
        ResearchProducerSessionId session,
        ResearchTargetResolution resolution,
        ImmutableArray<ResearchProducerKind> producers)
    {
        var items = ImmutableArray.CreateBuilder<ResearchProducerWorkItem>(
            resolution.Correspondences.Length * producers.Length);
        foreach (ResearchTargetCorrespondenceOutcome correspondence in
            resolution.Correspondences)
        {
            foreach (ResearchProducerKind producer in producers)
            {
                items.Add(
                    new ResearchProducerWorkItem(
                        new ResearchProducerWorkItemId(session),
                        correspondence,
                        producer));
            }
        }

        return items.ToImmutable();
    }

    static ResearchProducerWorkOutcome Execute(
        ResearchAdmittedPopulation population,
        ResearchTargetResolution resolution,
        ResearchProducerWorkItem item,
        Dictionary<ResearchComparisonInputId, StageAccess> stages,
        List<InputStage> acquired,
        IResearchProducerInvoker invoker)
    {
        if (item.Correspondence
            is ResearchTargetCorrespondenceOutcome.CounterpartUnavailable
                or ResearchTargetCorrespondenceOutcome.DomainUnavailable)
        {
            return new ResearchProducerWorkOutcome.Unavailable(
                Unavailable(
                    ResearchProducerUnavailableKind.CorrespondenceUnavailable));
        }

        EndpointPair endpoints = CreateEndpoints(
            population,
            resolution,
            item.Correspondence,
            stages,
            acquired);
        if (endpoints.Unavailable is { } unavailable)
            return new ResearchProducerWorkOutcome.Unavailable(unavailable);

        if (item.Producer is not ResearchProducerKind.CSharp
            and not ResearchProducerKind.IlBody)
        {
            throw new InvalidOperationException(
                "A validated work item must name a cataloged producer.");
        }

        try
        {
            return item.Producer switch
            {
                ResearchProducerKind.CSharp => ProduceCSharp(
                    endpoints,
                    invoker),
                ResearchProducerKind.IlBody => ProduceIl(endpoints, invoker),
                _ => throw new UnreachableException(),
            };
        }
        catch (Exception exception) when (IsRecoverableProducerFailure(exception))
        {
            return new ResearchProducerWorkOutcome.Failed(
                new ResearchProducerDiagnostic(
                    ResearchProducerDiagnosticKind.ProducerException,
                    item.Producer));
        }
    }

    static ResearchProducerWorkOutcome ProduceCSharp(
        EndpointPair endpoints,
        IResearchProducerInvoker invoker)
    {
        CSharpMemberDiffEndpoint oldEndpoint = CSharpEndpoint(endpoints.Before!);
        CSharpMemberDiffEndpoint newEndpoint = CSharpEndpoint(endpoints.After!);
        CSharpMemberEndpointComparison result = invoker.CompareCSharp(
            oldEndpoint,
            newEndpoint);
        return result is not null
            && NativeResultMatches(endpoints, result)
                ? new ResearchProducerWorkOutcome.ProducedCSharp(result)
                : ContractViolation(ResearchProducerKind.CSharp);
    }

    static ResearchProducerWorkOutcome ProduceIl(
        EndpointPair endpoints,
        IResearchProducerInvoker invoker)
    {
        IlMemberDiffEndpoint oldEndpoint = IlEndpoint(endpoints.Before!);
        IlMemberDiffEndpoint newEndpoint = IlEndpoint(endpoints.After!);
        IlMemberEndpointComparison result = invoker.CompareIl(
            oldEndpoint,
            newEndpoint);
        return result is not null
            && NativeResultMatches(endpoints, result)
                ? new ResearchProducerWorkOutcome.ProducedIlBody(result)
                : ContractViolation(ResearchProducerKind.IlBody);
    }

    static ResearchProducerWorkOutcome ContractViolation(
        ResearchProducerKind producer)
        => new ResearchProducerWorkOutcome.Failed(
            new ResearchProducerDiagnostic(
                ResearchProducerDiagnosticKind.ProducerContractViolation,
                producer));

    static EndpointPair CreateEndpoints(
        ResearchAdmittedPopulation population,
        ResearchTargetResolution resolution,
        ResearchTargetCorrespondenceOutcome correspondence,
        Dictionary<ResearchComparisonInputId, StageAccess> stages,
        List<InputStage> acquired)
    {
        string subject = SubjectIdentity(resolution, correspondence);
        return correspondence switch
        {
            ResearchTargetCorrespondenceOutcome.Paired paired =>
                PairPresent(
                    population,
                    paired.Before,
                    paired.After,
                    subject,
                    stages,
                    acquired),
            ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly =>
                Pair(
                    Present(
                        population,
                        beforeOnly.Before,
                        subject,
                        stages,
                        acquired),
                    EndpointAccess.Absent(
                        subject,
                        "The correspondence key is absent from this side.")),
            ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly =>
                Pair(
                    EndpointAccess.Absent(
                        subject,
                        "The correspondence key is absent from this side."),
                    Present(
                        population,
                        afterOnly.After,
                        subject,
                        stages,
                        acquired)),
            ResearchTargetCorrespondenceOutcome.Absent =>
                Pair(
                    EndpointAccess.Absent(
                        subject,
                        "The target domain is absent from this side."),
                    EndpointAccess.Absent(
                        subject,
                        "The target domain is absent from this side.")),
            _ => throw new InvalidOperationException(
                "Unavailable correspondence must not be adapted."),
        };
    }

    static EndpointPair PairPresent(
        ResearchAdmittedPopulation population,
        ResearchCorrespondingTarget beforeTarget,
        ResearchCorrespondingTarget afterTarget,
        string subject,
        Dictionary<ResearchComparisonInputId, StageAccess> stages,
        List<InputStage> acquired)
    {
        EndpointAccess before = Present(
            population,
            beforeTarget,
            subject,
            stages,
            acquired);
        if (before.Unavailable is { } unavailable)
            return new EndpointPair(null, null, unavailable);

        return Pair(
            before,
            Present(
                population,
                afterTarget,
                subject,
                stages,
                acquired));
    }

    static EndpointPair Pair(EndpointAccess before, EndpointAccess after)
        => before.Unavailable is { } beforeUnavailable
            ? new EndpointPair(null, null, beforeUnavailable)
            : after.Unavailable is { } afterUnavailable
                ? new EndpointPair(null, null, afterUnavailable)
                : new EndpointPair(before, after, null);

    static EndpointAccess Present(
        ResearchAdmittedPopulation population,
        ResearchCorrespondingTarget target,
        string subject,
        Dictionary<ResearchComparisonInputId, StageAccess> stages,
        List<InputStage> acquired)
    {
        ResearchComparisonInputId input = target.Attempt.Request.Input;
        MetadataMethodAddress? address = target.Target.Address;
        if (target.Target.Role == ResearchTargetRelationshipRole.None
            || address is null)
        {
            return EndpointAccess.UnavailableEndpoint(
                Unavailable(
                    ResearchProducerUnavailableKind.EndpointAddressUnavailable,
                    input));
        }

        StageAccess stage = GetStage(population, input, stages, acquired);
        if (stage.Unavailable is { } unavailable)
            return EndpointAccess.UnavailableEndpoint(unavailable);

        InputStage acquiredStage = stage.Stage!;
        MetadataReader reader = acquiredStage.Source.Reader;
        if (target.Target.Module
                != acquiredStage.Occurrence.BodyIndex.ModuleIdentity
            || !address.Value.BelongsTo(reader)
            || address.Value.Handle.IsNil
            || MetadataTokens.GetRowNumber(address.Value.Handle)
                > reader.MethodDefinitions.Count)
        {
            return EndpointAccess.UnavailableEndpoint(
                Unavailable(
                    ResearchProducerUnavailableKind.ModuleIdentityMismatch,
                    input));
        }

        return EndpointAccess.PresentEndpoint(
            subject,
            acquiredStage,
            address.Value.Handle);
    }

    static StageAccess GetStage(
        ResearchAdmittedPopulation population,
        ResearchComparisonInputId input,
        Dictionary<ResearchComparisonInputId, StageAccess> stages,
        List<InputStage> acquired)
    {
        if (stages.TryGetValue(input, out StageAccess? existing))
            return existing;

        ResearchAdmittedInput admitted = population.GetInput(input);
        var occurrence =
            (ImplementationComparisonInputOccurrence)admitted.Occurrence;
        MetadataSource source;
        try
        {
            source = MetadataSource.OpenWithoutSymbols(
                occurrence.Assembly,
                occurrence.Resolver);
        }
        catch (Exception exception) when (IsExpectedInputFailure(exception))
        {
            var failed = new StageAccess(
                null,
                Unavailable(
                    ResearchProducerUnavailableKind.InputUnreadable,
                    input));
            stages.Add(input, failed);
            return failed;
        }

        var stage = new InputStage(input, occurrence, source);
        acquired.Add(stage);
        StageAccess access;
        try
        {
            ResearchTargetInputValidationEvidence evidence =
                ResearchInputImageValidation.Capture(
                    source.Reader,
                    occurrence);
            access = ResearchInputImageValidation.Validate(evidence, occurrence)
                switch
                {
                    ResearchTargetDiagnosticKind.AssemblyIdentityMismatch
                        or ResearchTargetDiagnosticKind.StandaloneModule =>
                        new StageAccess(
                            stage,
                            Unavailable(
                                ResearchProducerUnavailableKind
                                    .AssemblyIdentityMismatch,
                                input)),
                    ResearchTargetDiagnosticKind.ModuleIdentityMismatch =>
                        new StageAccess(
                            stage,
                            Unavailable(
                                ResearchProducerUnavailableKind
                                    .ModuleIdentityMismatch,
                                input)),
                    null => new StageAccess(stage, null),
                    _ => new StageAccess(
                        stage,
                        Unavailable(
                            ResearchProducerUnavailableKind.InputUnreadable,
                            input)),
                };
        }
        catch (Exception exception) when (IsExpectedMetadataFailure(exception))
        {
            access = new StageAccess(
                stage,
                Unavailable(
                    ResearchProducerUnavailableKind.InputUnreadable,
                    input));
        }

        stages.Add(input, access);
        return access;
    }

    static ImmutableArray<ResearchProducerCleanupOutcome> Cleanup(
        List<InputStage> acquired)
    {
        var cleanup =
            ImmutableArray.CreateBuilder<ResearchProducerCleanupOutcome>(
                acquired.Count);
        for (int index = acquired.Count - 1; index >= 0; index--)
        {
            InputStage stage = acquired[index];
            try
            {
                stage.Source.Dispose();
                cleanup.Add(
                    new ResearchProducerCleanupOutcome.Succeeded(stage.Input));
            }
            catch (Exception exception) when (IsRecoverableCleanupFailure(exception))
            {
                cleanup.Add(
                    new ResearchProducerCleanupOutcome.Failed(
                        stage.Input,
                        new ResearchProducerDiagnostic(
                            ResearchProducerDiagnosticKind.CleanupFailed)));
            }
        }

        return cleanup.ToImmutable();
    }

    static CSharpMemberDiffEndpoint CSharpEndpoint(EndpointAccess endpoint)
    {
        var subject = new FindingSubject(endpoint.Subject!, endpoint.Subject!);
        return endpoint.Stage is { } stage
            ? new CSharpMemberDiffEndpoint.Present(
                subject,
                stage.Source,
                endpoint.Handle)
            : new CSharpMemberDiffEndpoint.SubjectAbsent(
                subject,
                endpoint.AbsenceDetail);
    }

    static IlMemberDiffEndpoint IlEndpoint(EndpointAccess endpoint)
    {
        var subject = new IlMemberDiffSubject(
            endpoint.Subject!,
            endpoint.Subject!);
        return endpoint.Stage is { } stage
            ? new IlMemberDiffEndpoint.Present(
                subject,
                stage.Source.Pe,
                stage.Source.Reader,
                endpoint.Handle)
            : new IlMemberDiffEndpoint.SubjectAbsent(
                subject,
                endpoint.AbsenceDetail);
    }

    static string SubjectIdentity(
        ResearchTargetResolution resolution,
        ResearchTargetCorrespondenceOutcome correspondence)
        => correspondence switch
        {
            ResearchTargetCorrespondenceOutcome.Paired paired =>
                paired.Before.CorrespondenceKey.CanonicalIdentity,
            ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly =>
                beforeOnly.Before.CorrespondenceKey.CanonicalIdentity,
            ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly =>
                afterOnly.After.CorrespondenceKey.CanonicalIdentity,
            ResearchTargetCorrespondenceOutcome.Absent =>
                AbsentSubject(resolution, correspondence.Scope),
            _ => throw new InvalidOperationException(
                "Unavailable correspondence has no producer subject."),
        };

    static string AbsentSubject(
        ResearchTargetResolution resolution,
        ResearchTargetScopeId scopeId)
    {
        ResearchTargetScope scope = resolution.Scopes.Single(
            scope => ReferenceEquals(scope.Id, scopeId));
        return $"{scope.DeclaringTypeFullName}::{scope.Selector.NormalizedSelector}";
    }

    static bool NativeResultMatches(
        EndpointPair endpoints,
        CSharpMemberEndpointComparison result)
        => string.Equals(
                result.Old.Key,
                endpoints.Before!.Subject,
                StringComparison.Ordinal)
            && string.Equals(
                result.New.Key,
                endpoints.After!.Subject,
                StringComparison.Ordinal)
            && InspectionMatches(
                endpoints.Before,
                result.Findings.OldInspection)
            && InspectionMatches(
                endpoints.After,
                result.Findings.NewInspection);

    static bool NativeResultMatches(
        EndpointPair endpoints,
        IlMemberEndpointComparison result)
        => string.Equals(
                result.Old.Identity,
                endpoints.Before!.Subject,
                StringComparison.Ordinal)
            && string.Equals(
                result.New.Identity,
                endpoints.After!.Subject,
                StringComparison.Ordinal)
            && InspectionMatches(
                endpoints.Before,
                result.Findings.OldInspection)
            && InspectionMatches(
                endpoints.After,
                result.Findings.NewInspection);

    static bool InspectionMatches<T>(
        EndpointAccess endpoint,
        FindingInspection<T> inspection)
        where T : notnull
        => endpoint.Stage is null
            ? inspection is FindingInspection<T>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.SubjectAbsent,
                }
            : inspection is not FindingInspection<T>.Absent
                {
                    Kind: FindingInspectionAbsenceKind.SubjectAbsent,
                };

    static ResearchProducerUnavailable Unavailable(
        ResearchProducerUnavailableKind kind,
        ResearchComparisonInputId? input = null)
        => new(
            kind,
            input,
            kind switch
            {
                ResearchProducerUnavailableKind.CorrespondenceUnavailable =>
                    "Target correspondence is unavailable.",
                ResearchProducerUnavailableKind.InputUnreadable =>
                    "The admitted input could not be opened or read.",
                ResearchProducerUnavailableKind.AssemblyIdentityMismatch =>
                    "The live input does not match the admitted assembly.",
                ResearchProducerUnavailableKind.ModuleIdentityMismatch =>
                    "The live input does not match the resolved module.",
                ResearchProducerUnavailableKind.EndpointAddressUnavailable =>
                    "The resolved target has no physical method endpoint.",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            });

    static bool IsExpectedInputFailure(Exception exception)
        => IsRecoverableExecutionFailure(exception);

    static bool IsExpectedMetadataFailure(Exception exception)
        => IsRecoverableExecutionFailure(exception);

    static bool IsRecoverableProducerFailure(Exception exception)
        => exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;

    static bool IsRecoverableExecutionFailure(Exception exception)
        => exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;

    static bool IsRecoverableCleanupFailure(Exception exception)
        => exception is not OutOfMemoryException
            and not StackOverflowException
            and not AccessViolationException;

    sealed record InputStage(
        ResearchComparisonInputId Input,
        ImplementationComparisonInputOccurrence Occurrence,
        MetadataSource Source);

    sealed record StageAccess(
        InputStage? Stage,
        ResearchProducerUnavailable? Unavailable);

    sealed record EndpointAccess(
        string? Subject,
        InputStage? Stage,
        MethodDefinitionHandle Handle,
        string? AbsenceDetail,
        ResearchProducerUnavailable? Unavailable)
    {
        internal static EndpointAccess PresentEndpoint(
            string subject,
            InputStage stage,
            MethodDefinitionHandle handle)
            => new(subject, stage, handle, null, null);

        internal static EndpointAccess Absent(
            string subject,
            string detail)
            => new(subject, null, default, detail, null);

        internal static EndpointAccess UnavailableEndpoint(
            ResearchProducerUnavailable unavailable)
            => new(null, null, default, null, unavailable);
    }

    sealed record EndpointPair(
        EndpointAccess? Before,
        EndpointAccess? After,
        ResearchProducerUnavailable? Unavailable);

    sealed class NativeProducerInvoker : IResearchProducerInvoker
    {
        internal static NativeProducerInvoker Instance { get; } = new();

        public CSharpMemberEndpointComparison CompareCSharp(
            CSharpMemberDiffEndpoint oldEndpoint,
            CSharpMemberDiffEndpoint newEndpoint)
            => CSharpBodyDiff.CompareMemberEndpoints(oldEndpoint, newEndpoint);

        public IlMemberEndpointComparison CompareIl(
            IlMemberDiffEndpoint oldEndpoint,
            IlMemberDiffEndpoint newEndpoint)
            => IlAssemblyDiff.CompareMemberEndpoints(oldEndpoint, newEndpoint);
    }
}

internal interface IResearchProducerInvoker
{
    CSharpMemberEndpointComparison CompareCSharp(
        CSharpMemberDiffEndpoint oldEndpoint,
        CSharpMemberDiffEndpoint newEndpoint);

    IlMemberEndpointComparison CompareIl(
        IlMemberDiffEndpoint oldEndpoint,
        IlMemberDiffEndpoint newEndpoint);
}
