using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Analysis = ILInspector.Analysis;
using Planning = ILInspector.Analysis.Planning;
using DotnetInspector.Services;
using ILInspector.Metadata;

using DeclaringTypeOutcome =
    ILInspector.Analysis.CatalogMethodDefinitionCorrespondencePlan
        .CatalogMemberDeclaringTypeOutcome;
using DeclaringTypePlan =
    ILInspector.Analysis.CatalogMethodDefinitionCorrespondencePlan
        .CatalogMemberDeclaringTypePlan;
using SelectionOutcome =
    ILInspector.Analysis.CatalogMethodDefinitionCorrespondencePlan
        .CatalogMemberDefinitionSelectionOutcome;
using SelectionPlan =
    ILInspector.Analysis.CatalogMethodDefinitionCorrespondencePlan
        .CatalogMemberDefinitionSelectionPlan;

namespace DotnetInspector.Queries;

public enum TypeCallCompatibilityTargetDisposition
{
    Available,
    Unavailable,
    Limited,
}

public enum TypeCallCompatibilityTopologyDisposition
{
    Complete,
    Partial,
    Unavailable,
}

/// <summary>
/// Compatibility attributes read directly from one admitted target method.
/// </summary>
public sealed record TypeCallCompatibilityTarget(
    AssemblyContextSubject Subject,
    Analysis.MethodIdentity Method,
    TypeCallCompatibilityTargetDisposition Disposition,
    ImmutableArray<CompatibilityAttributeEvidence> Attributes,
    CandidateOpenFailure? OpenFailure = null,
    string? Limitation = null);

/// <summary>One resolved physical call from the exact focused Type.</summary>
public sealed record TypeCallCompatibilityOccurrence(
    AssemblyContextSubject Source,
    Analysis.MethodIdentity SourceMethod,
    AssemblyContextSubject Target,
    Analysis.MethodIdentity TargetMethod,
    Planning.FocusedMethodCallSite CallSite)
{
    public Analysis.DirectCall Call => CallSite.Call;
}

/// <summary>One focused physical call whose exact target was unavailable.</summary>
public sealed record TypeCallCompatibilityUnresolvedOccurrence(
    AssemblyContextSubject Source,
    Analysis.MethodIdentity SourceMethod,
    Planning.FocusedMethodCallSite CallSite,
    string Limitation)
{
    public Analysis.DirectCall Call => CallSite.Call;
}

/// <summary>Method-source evidence for one metadata-only target visit.</summary>
public sealed record TypeCallCompatibilityTargetSourceReceipt(
    AssemblyContextSubject Subject,
    Planning.MethodDefinitionSourceReceipt Receipt);

/// <summary>Exact focused work performed by call acquisition.</summary>
public sealed record TypeCallCompatibilityAcquisitionReceipt(
    Planning.MethodDefinitionSourceReceipt FocusSource,
    ImmutableArray<TypeCallCompatibilityTargetSourceReceipt> TargetSources,
    int DistinctCallees,
    int TargetMethodsInspected);

/// <summary>
/// Exact outgoing call and compatibility evidence for one focused Type.
/// </summary>
public sealed record AssemblyContextTypeCallCompatibilityResult(
    AssemblyAcquisitionRegistration FocusAssembly,
    MetadataTypeDefinitionName FocusType,
    ImmutableArray<TypeCallCompatibilityOccurrence> Occurrences,
    ImmutableArray<TypeCallCompatibilityUnresolvedOccurrence>
        UnresolvedOccurrences,
    ImmutableArray<TypeCallCompatibilityTarget> Targets,
    TypeCallCompatibilityTopologyDisposition TopologyDisposition,
    TypeCallCompatibilityAcquisitionReceipt? Receipt = null,
    int UnavailableTargetMetadata = 0,
    int LimitedTargetMetadata = 0)
{
    public bool TopologyComplete =>
        TopologyDisposition
            == TypeCallCompatibilityTopologyDisposition.Complete;
}

/// <summary>
/// Decodes one exact Type's bodies and resolves only its observed callees.
/// </summary>
public static class AssemblyContextTypeCallCompatibilityQuery
{
    static readonly Planning.FocusedMethodCallProducer FocusedProducer =
        Planning.FocusedMethodCallProducer.Instance;
    static readonly Planning.MethodIdentityProducer IdentityProducer =
        Planning.MethodIdentityProducer.Instance;
    static readonly Planning.WorkDescription FocusedWork =
        CreateFocusedWork(FocusedProducer);
    static readonly Planning.WorkDescription IdentityWork =
        CreateFocusedWork(IdentityProducer);

    public static InspectionQuery<AssemblyContextTypeCallCompatibilityResult>
        Definition
    { get; } =
        new(
            "Assembly context Type call compatibility",
            InspectionCost.Unbounded);

    public static AssemblyContextTypeCallCompatibilityResult Execute(
        AssemblyContextGroup group,
        AssemblyAcquisitionRegistration focusAssembly,
        MetadataTypeDefinitionName focusType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(focusAssembly);
        ArgumentNullException.ThrowIfNull(focusType);
        cancellationToken.ThrowIfCancellationRequested();

        AssemblyContextParticipant focusParticipant =
            RequireParticipant(group, focusAssembly);
        AssemblyContextSubject focusSubject =
            new(focusParticipant.Assembly);
        SourceAcquisition source;
        AssemblyImageAccessResult<SourceAcquisition> sourceAccess =
            group.UseSnapshot(
                focusParticipant,
                cancellationToken,
                snapshot => AcquireSource(
                    snapshot,
                    focusSubject,
                    focusType));
        switch (sourceAccess)
        {
            case AssemblyImageAccessResult<SourceAcquisition>.Available
                available:
                source = available.Value;
                break;
            case AssemblyImageAccessResult<SourceAcquisition>.Rejected:
                return Unavailable(
                    focusAssembly,
                    focusType);
            default:
                throw new InvalidOperationException(
                    "Unknown focus image access result.");
        }

        if (source.TypeHandle.IsNil
            || source.Receipt is null
            || source.Receipt.Completion
                != Planning.MethodDefinitionSourceCompletion.Exhausted)
        {
            return new(
                focusAssembly,
                focusType,
                [],
                source.Calls.Select(call =>
                    Unresolved(
                        focusSubject,
                        call,
                        "Focused Method-source acquisition was incomplete."))
                    .ToImmutableArray(),
                [],
                TypeCallCompatibilityTopologyDisposition.Unavailable);
        }

        ResolutionContextInputs resolution =
            CreateResolutionContextInputs(
                group,
                focusAssembly,
                source.Snapshot);

        var unresolved =
            ImmutableArray.CreateBuilder<
                TypeCallCompatibilityUnresolvedOccurrence>();
        var resolved =
            ImmutableArray.CreateBuilder<ResolvedCall>();
        var targetReceipts =
            ImmutableArray.CreateBuilder<
                TypeCallCompatibilityTargetSourceReceipt>();
        var external = new Dictionary<
            Analysis.MemberRef,
            ExternalCallPlan>();
        var currentTargets =
            new Dictionary<MethodDefinitionHandle, List<SourceCall>>();

        foreach (SourceCall call in source.Calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (call.Site.Target)
            {
                case Planning.MethodCallTarget.CurrentModule current:
                    if (DeclaringType(
                            source.Snapshot,
                            current.Method)
                        == source.TypeHandle)
                    {
                        continue;
                    }
                    if (!currentTargets.TryGetValue(
                            current.Method,
                            out List<SourceCall>? calls))
                    {
                        calls = [];
                        currentTargets.Add(current.Method, calls);
                    }
                    calls.Add(call);
                    break;
                case Planning.MethodCallTarget.External:
                    if (!external.TryGetValue(
                            call.Site.Call.Callee,
                            out ExternalCallPlan? plan))
                    {
                        plan = new(
                            call.Site.Call.Callee,
                            DeclaringTypePlan.Create(
                                resolution.FocusAssembly,
                                call.Site.Call.Callee));
                        external.Add(call.Site.Call.Callee, plan);
                    }
                    (plan ?? throw new InvalidOperationException(
                        "External call planning did not produce a plan."))
                        .Calls.Add(call);
                    break;
                default:
                    unresolved.Add(
                        Unresolved(
                            focusSubject,
                            call,
                            "The call has no exact metadata target."));
                    break;
            }
        }

        ResolveCurrentModuleTargets(
            source,
            focusSubject,
            currentTargets,
            resolved,
            unresolved,
            targetReceipts);
        ResolveExternalDeclaringTypes(
            resolution,
            external.Values,
            unresolved,
            focusSubject);

        ImmutableArray<TargetImage> targetImages =
            AcquireTargetImages(
                group,
                source,
                external.Values,
                unresolved,
                focusSubject,
                cancellationToken);
        ResolveExternalMethods(
            resolution,
            external.Values,
            targetImages,
            resolved,
            unresolved,
            focusSubject);

        ImmutableArray<TypeCallCompatibilityOccurrence> occurrences =
        [
            .. resolved.Select(item =>
                new TypeCallCompatibilityOccurrence(
                    focusSubject,
                    item.Call.Source,
                    item.Target.Subject,
                    item.Target.Method,
                    item.Call.Site)),
        ];
        ImmutableArray<TypeCallCompatibilityTarget> targets =
            InspectTargets(
                source,
                targetImages,
                resolved,
                cancellationToken);
        targetReceipts.AddRange(
            targetImages.Select(image =>
                new TypeCallCompatibilityTargetSourceReceipt(
                    image.Subject,
                    image.Receipt)));
        int distinctCallees =
            source.Calls.Select(call => call.Site.Call.Callee).Distinct().Count();
        int unavailableTargetMetadata =
            external.Values.Count(static plan =>
                plan.MetadataDisposition
                    == TypeCallCompatibilityTargetDisposition.Unavailable);
        int limitedTargetMetadata =
            external.Values.Count(static plan =>
                plan.MetadataDisposition
                    == TypeCallCompatibilityTargetDisposition.Limited);
        var receipt = new TypeCallCompatibilityAcquisitionReceipt(
            source.Receipt,
            targetReceipts.ToImmutable(),
            distinctCallees,
            targets.Length);
        bool sourceComplete =
            source.Bodies.All(body =>
                body.Diagnostics.IsEmpty
                && (!body.HasManagedBody || body.Method is not null));
        TypeCallCompatibilityTopologyDisposition topology =
            sourceComplete && unresolved.Count == 0
                ? TypeCallCompatibilityTopologyDisposition.Complete
                : TypeCallCompatibilityTopologyDisposition.Partial;
        return new(
            focusAssembly,
            focusType,
            occurrences,
            unresolved.ToImmutable(),
            targets,
            topology,
            receipt,
            unavailableTargetMetadata,
            limitedTargetMetadata);
    }

    static SourceAcquisition AcquireSource(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject,
        MetadataTypeDefinitionName focusType)
    {
        TypeDefinitionHandle typeHandle =
            FindType(snapshot, focusType);
        if (typeHandle.IsNil)
        {
            return new(
                snapshot,
                typeHandle,
                [],
                [],
                null);
        }

        Execution<Planning.FocusedMethodCallProducerResult> execution =
            Execute(
                snapshot,
                subject,
                FocusedProducer,
                FocusedWork,
                Planning.MethodDefinitionSourceBreadth.ExactTypes(typeHandle),
                new(
                    maximumBodies: int.MaxValue,
                    maximumEncodedIlBytes: long.MaxValue));
        ImmutableArray<Planning.FocusedMethodCallBody> bodies =
            execution.Value.Bodies;
        ImmutableArray<SourceCall> calls =
        [
            .. bodies
                .Where(static body => body.Method is not null)
                .SelectMany(body => body.Calls
                    .Where(static site => site.Call.Kind
                        is Analysis.CallKind.Call
                            or Analysis.CallKind.CallVirtual
                            or Analysis.CallKind.NewObject)
                    .Select(site => new SourceCall(body.Method!, site))),
        ];
        return new(
            snapshot,
            typeHandle,
            bodies,
            calls,
            execution.Receipt);
    }

    static void ResolveCurrentModuleTargets(
        SourceAcquisition source,
        AssemblyContextSubject focusSubject,
        IReadOnlyDictionary<MethodDefinitionHandle, List<SourceCall>> targets,
        ImmutableArray<ResolvedCall>.Builder resolved,
        ImmutableArray<TypeCallCompatibilityUnresolvedOccurrence>.Builder
            unresolved,
        ImmutableArray<TypeCallCompatibilityTargetSourceReceipt>.Builder
            receipts)
    {
        if (targets.Count == 0)
            return;

        Execution<Planning.MethodIdentityProducerResult> execution =
            Execute(
                source.Snapshot,
                focusSubject,
                IdentityProducer,
                IdentityWork,
                Planning.MethodDefinitionSourceBreadth.ExactMethods(
                    targets.Keys.ToImmutableArray()),
                new(
                    maximumBodies: int.MaxValue,
                    maximumEncodedIlBytes: long.MaxValue));
        Dictionary<int, Planning.MethodIdentityBody> identities =
            execution.Value.Methods.ToDictionary(
                static method => method.MethodToken);
        receipts.Add(
            new(
                focusSubject,
                execution.Receipt));
        foreach ((MethodDefinitionHandle handle, List<SourceCall> calls)
            in targets)
        {
            int token = MetadataTokens.GetToken(handle);
            if (!identities.TryGetValue(
                    token,
                    out Planning.MethodIdentityBody? identity)
                || identity.Method is null)
            {
                foreach (SourceCall call in calls)
                {
                    unresolved.Add(
                        Unresolved(
                            focusSubject,
                            call,
                            identity?.Limitation
                                ?? "The same-module target was unavailable."));
                }
                continue;
            }

            var target = new ResolvedTarget(
                focusSubject,
                identity.Method);
            foreach (SourceCall call in calls)
                resolved.Add(new(call, target));
        }
    }

    static void ResolveExternalDeclaringTypes(
        ResolutionContextInputs resolution,
        IEnumerable<ExternalCallPlan> plans,
        ImmutableArray<TypeCallCompatibilityUnresolvedOccurrence>.Builder
            unresolved,
        AssemblyContextSubject focusSubject)
    {
        ExternalCallPlan[] snapshot = [.. plans];
        ImmutableArray<TypeResolutionRequest> requests =
        [
            .. snapshot.SelectMany(plan => plan.DeclaringType.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance),
        ];
        using TypeResolutionContext context =
            resolution.CreateContext(requests);
        foreach (ExternalCallPlan plan in snapshot)
        {
            switch (plan.DeclaringType.Project(context))
            {
                case DeclaringTypeOutcome.Resolved resolved:
                    plan.TargetAssembly = resolved.Assembly;
                    plan.TargetType = resolved.Address;
                    break;
                default:
                    plan.Limitation =
                        "The external declaring Type did not resolve exactly.";
                    plan.MetadataDisposition =
                        TypeCallCompatibilityTargetDisposition.Unavailable;
                    foreach (SourceCall call in plan.Calls)
                    {
                        unresolved.Add(
                            Unresolved(
                                focusSubject,
                                call,
                                plan.Limitation));
                    }
                    break;
            }
        }
    }

    static ImmutableArray<TargetImage> AcquireTargetImages(
        AssemblyContextGroup group,
        SourceAcquisition source,
        IEnumerable<ExternalCallPlan> plans,
        ImmutableArray<TypeCallCompatibilityUnresolvedOccurrence>.Builder
            unresolved,
        AssemblyContextSubject focusSubject,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<TargetImage>();
        foreach (IGrouping<AssemblyAcquisitionRegistration, ExternalCallPlan>
            targetGroup in plans
                .Where(static plan =>
                    plan.TargetAssembly is not null
                    && plan.TargetType is not null)
                .GroupBy(plan =>
                    plan.TargetAssembly!.Registration))
        {
            AssemblyContextParticipant participant =
                RequireParticipant(group, targetGroup.Key);
            ExternalCallPlan[] targetPlans = [.. targetGroup];
            AssemblyImageAccessResult<TargetImage> access =
                group.UseSnapshot(
                    participant,
                    cancellationToken,
                    snapshot => AcquireTargetImage(
                        snapshot,
                        new(participant.Assembly),
                        targetPlans));
            switch (access)
            {
                case AssemblyImageAccessResult<TargetImage>.Available available:
                    results.Add(available.Value);
                    break;
                case AssemblyImageAccessResult<TargetImage>.Rejected:
                    foreach (ExternalCallPlan plan in targetPlans)
                    {
                        plan.Limitation =
                            "The target assembly image was unavailable.";
                        plan.MetadataDisposition =
                            TypeCallCompatibilityTargetDisposition.Unavailable;
                        foreach (SourceCall call in plan.Calls)
                        {
                            unresolved.Add(
                                Unresolved(
                                    focusSubject,
                                    call,
                                    plan.Limitation));
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown target image access result.");
            }
        }
        return results.ToImmutable();
    }

    static TargetImage AcquireTargetImage(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject,
        ExternalCallPlan[] plans)
    {
        ImmutableArray<MetadataTypeDefinitionAddress> addresses =
        [
            .. plans.Select(plan => plan.TargetType!.Value).Distinct(),
        ];
        TypeDefinitionHandle[] handles;
        using (var image = new PEReader(snapshot.Content))
        {
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(image);
            handles =
            [
                .. addresses.Select(address =>
                    address.TryResolve(reader, out TypeDefinitionHandle handle)
                        ? handle
                        : default),
            ];
        }
        if (handles.Any(static handle => handle.IsNil))
        {
            throw new BadImageFormatException(
                "A resolved target Type did not match its acquired image.");
        }

        Execution<Planning.MethodIdentityProducerResult> execution =
            Execute(
                snapshot,
                subject,
                IdentityProducer,
                IdentityWork,
                Planning.MethodDefinitionSourceBreadth.ExactTypes(handles),
                new(
                    maximumBodies: int.MaxValue,
                    maximumEncodedIlBytes: long.MaxValue));
        var methods = execution.Value.Methods
            .Where(static method => method.Method is not null)
            .Select(static method => method.Method!)
            .ToImmutableArray();
        return new(
            subject,
            snapshot,
            addresses,
            methods,
            execution.Receipt);
    }

    static void ResolveExternalMethods(
        ResolutionContextInputs resolution,
        IEnumerable<ExternalCallPlan> plans,
        ImmutableArray<TargetImage> images,
        ImmutableArray<ResolvedCall>.Builder resolved,
        ImmutableArray<TypeCallCompatibilityUnresolvedOccurrence>.Builder
            unresolved,
        AssemblyContextSubject focusSubject)
    {
        var selectionPlans =
            new List<(ExternalCallPlan Call, SelectionPlan Selection,
                TargetImage Image)>();
        foreach (ExternalCallPlan plan in plans.Where(static plan =>
            plan.TargetAssembly is not null
            && plan.TargetType is not null
            && plan.Limitation is null))
        {
            TargetImage? image = images.SingleOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Subject.Registration,
                    plan.TargetAssembly!.Registration));
            if (image is null)
                continue;
            MetadataTypeDefinitionAddress targetType =
                plan.TargetType
                ?? throw new InvalidOperationException(
                    "A selected external call has no target Type.");
            ImmutableArray<Analysis.MethodIdentity> candidates =
            [
                .. image.Methods.Where(method =>
                    IsMethodOfType(
                        image.Snapshot,
                        method,
                        targetType)),
            ];
            SelectionPlan selection = SelectionPlan.Create(
                resolution.FocusAssembly,
                plan.Member,
                plan.TargetAssembly!,
                image.Snapshot,
                targetType,
                candidates);
            selectionPlans.Add((plan, selection, image));
        }

        ImmutableArray<TypeResolutionRequest> requests =
        [
            .. selectionPlans.SelectMany(item => item.Selection.Requests)
                .Distinct(TypeResolutionRequestComparer.Instance),
        ];
        using TypeResolutionContext context =
            resolution.CreateContext(requests);
        foreach ((ExternalCallPlan plan, SelectionPlan selection,
            TargetImage image) in selectionPlans)
        {
            SelectionOutcome outcome = selection.Project(context);
            switch (outcome)
            {
                case SelectionOutcome.Exact exact:
                    var target = new ResolvedTarget(
                        image.Subject,
                        exact.Method);
                    foreach (SourceCall call in plan.Calls)
                        resolved.Add(new(call, target));
                    break;
                default:
                    plan.Limitation =
                        "The external target Method did not resolve exactly: "
                            + (outcome
                                is SelectionOutcome.Unavailable unavailable
                                    ? unavailable.Detail
                                    : outcome.GetType().Name);
                    plan.MetadataDisposition =
                        TypeCallCompatibilityTargetDisposition.Limited;
                    foreach (SourceCall call in plan.Calls)
                    {
                        unresolved.Add(
                            Unresolved(
                                focusSubject,
                                call,
                                plan.Limitation));
                    }
                    break;
            }
        }
    }

    static ImmutableArray<TypeCallCompatibilityTarget> InspectTargets(
        SourceAcquisition source,
        ImmutableArray<TargetImage> targetImages,
        ImmutableArray<ResolvedCall>.Builder resolved,
        CancellationToken cancellationToken)
    {
        ResolvedTarget[] targets =
        [
            .. resolved.Select(item => item.Target)
                .DistinctBy(target => (
                    target.Subject.Registration,
                    target.Method)),
        ];
        var results =
            ImmutableArray.CreateBuilder<TypeCallCompatibilityTarget>(
                targets.Length);
        foreach (IGrouping<AssemblyAcquisitionRegistration, ResolvedTarget>
            group in targets.GroupBy(target => target.Subject.Registration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyImageSnapshot? snapshot =
                ReferenceEquals(group.Key, source.Snapshot.Registration)
                    ? source.Snapshot
                    : targetImages.SingleOrDefault(image =>
                        ReferenceEquals(
                            image.Subject.Registration,
                            group.Key))?.Snapshot;
            if (snapshot is null)
            {
                results.AddRange(group.Select(target =>
                    new TypeCallCompatibilityTarget(
                        target.Subject,
                        target.Method,
                        TypeCallCompatibilityTargetDisposition.Unavailable,
                        [])));
                continue;
            }

            results.AddRange(
                InspectTargetSnapshot(
                    snapshot,
                    group,
                    cancellationToken));
        }
        return results.ToImmutable();
    }

    static ImmutableArray<TypeCallCompatibilityTarget> InspectTargetSnapshot(
        AssemblyImageSnapshot snapshot,
        IEnumerable<ResolvedTarget> targets,
        CancellationToken cancellationToken)
    {
        ResolvedTarget[] targetSnapshot = [.. targets];
        try
        {
            using var image = new PEReader(snapshot.Content);
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(image);
            var results =
                ImmutableArray.CreateBuilder<TypeCallCompatibilityTarget>(
                    targetSnapshot.Length);
            foreach (ResolvedTarget target in targetSnapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(
                    InspectTarget(
                        reader,
                        snapshot.ModuleVersionId,
                        target));
            }
            return results.MoveToImmutable();
        }
        catch (BadImageFormatException error)
        {
            return
            [
                .. targetSnapshot.Select(target =>
                    Limited(target, error.Message)),
            ];
        }
    }

    static TypeCallCompatibilityTarget InspectTarget(
        MetadataReader reader,
        Guid moduleVersionId,
        ResolvedTarget target)
    {
        if (target.Method.ModuleVersionId != moduleVersionId
            || (target.Method.MetadataToken & unchecked((int)0xff000000))
                != 0x06000000)
        {
            return Limited(
                target,
                "The target method identity did not match the image.");
        }

        int row = target.Method.MetadataToken & 0x00ffffff;
        if (row == 0 || row > reader.MethodDefinitions.Count)
            return Limited(
                target,
                "The target method token was outside the image.");

        MethodDefinition definition = reader.GetMethodDefinition(
            MetadataTokens.MethodDefinitionHandle(row));
        var attributes =
            ImmutableArray.CreateBuilder<CompatibilityAttributeEvidence>();
        bool unresolved = false;
        foreach (CustomAttributeHandle handle in
            definition.GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            string? name = AttributeReader.GetAttributeTypeName(
                reader,
                attribute.Constructor);
            if (name is null)
            {
                unresolved = true;
                continue;
            }

            if (name is
                KnownAttributeNames.RequiresUnreferencedCodeAttribute
                or KnownAttributeNames.RequiresDynamicCodeAttribute
                or KnownAttributeNames.RequiresAssemblyFilesAttribute)
            {
                attributes.Add(
                    new(
                        target.Method,
                        MetadataTokens.GetToken(handle),
                        name));
            }
        }

        return unresolved
            ? new(
                target.Subject,
                target.Method,
                TypeCallCompatibilityTargetDisposition.Limited,
                attributes.ToImmutable(),
                Limitation:
                    "At least one custom attribute type could not be resolved.")
            : new(
                target.Subject,
                target.Method,
                TypeCallCompatibilityTargetDisposition.Available,
                attributes.ToImmutable());
    }

    static TypeCallCompatibilityTarget Limited(
        ResolvedTarget target,
        string detail) =>
        new(
            target.Subject,
            target.Method,
            TypeCallCompatibilityTargetDisposition.Limited,
            [],
            Limitation: detail);

    static Execution<TResult> Execute<TResult>(
        AssemblyImageSnapshot snapshot,
        AssemblyContextSubject subject,
        Planning.ProducerDeclaration<TResult> producer,
        Planning.WorkDescription work,
        Planning.MethodDefinitionSourceBreadth breadth,
        Planning.MethodDefinitionTerminalWorkLimits limits)
    {
        Planning.MethodDefinitionSourceRequest<TResult> request =
            Planning.MethodDefinitionSourceRequest<TResult>.Create(
                work,
                producer,
                breadth,
                limits);
        Planning.AssemblyAnalysisOperation<TResult> operation =
            Planning.AssemblyAnalysisOperation<TResult>.Create(
                AssemblyContextAnalysisSource.Name(subject),
                request.AsAssemblyAnalysisSource());
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(snapshot);
        Planning.AssemblyAnalysisServiceResult<TResult> result =
            session.SnapshotOperation(
                operation,
                access => Planning.AssemblyAnalysisService.Instance.Execute(
                    operation,
                    access));
        if (result
            is not Planning.AssemblyAnalysisServiceResult<TResult>.Completed
                completed)
        {
            throw new InvalidOperationException(
                "The focused Method-source operation was rejected.");
        }

        Planning.ProducerResult<TResult> producerResult =
            completed.Execution.ResultOf(producer);
        if (!producerResult.HasValue)
        {
            throw new InvalidOperationException(
                producerResult.Failure?.Message
                    ?? producerResult.Critical?.Message
                    ?? "The focused Method-source producer failed.");
        }
        return new(
            producerResult.Value!,
            (Planning.MethodDefinitionSourceReceipt)
                completed.Execution.SourceReceipt);
    }

    static TypeDefinitionHandle FindType(
        AssemblyImageSnapshot snapshot,
        MetadataTypeDefinitionName type)
    {
        using var image = new PEReader(snapshot.Content);
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);
        TypeDefinitionHandle match = default;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionNameMatchResult result =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    handle,
                    type,
                    out _);
            if (result != MetadataTypeDefinitionNameMatchResult.Match)
                continue;
            if (!match.IsNil)
                return default;
            match = handle;
        }
        return match;
    }

    static TypeDefinitionHandle DeclaringType(
        AssemblyImageSnapshot snapshot,
        MethodDefinitionHandle method)
    {
        using var image = new PEReader(snapshot.Content);
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);
        return reader.GetMethodDefinition(method).GetDeclaringType();
    }

    static bool IsMethodOfType(
        AssemblyImageSnapshot snapshot,
        Analysis.MethodIdentity method,
        MetadataTypeDefinitionAddress type)
    {
        if (method.ModuleVersionId != snapshot.ModuleVersionId)
            return false;
        using var image = new PEReader(snapshot.Content);
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);
        EntityHandle handle =
            MetadataTokens.EntityHandle(method.MetadataToken);
        return handle.Kind == HandleKind.MethodDefinition
            && type.TryResolve(reader, out TypeDefinitionHandle typeHandle)
            && reader.GetMethodDefinition(
                    (MethodDefinitionHandle)handle)
                .GetDeclaringType() == typeHandle;
    }

    static ResolutionContextInputs CreateResolutionContextInputs(
        AssemblyContextGroup group,
        AssemblyAcquisitionRegistration focusAssembly,
        AssemblyImageSnapshot focusSnapshot)
    {
        ImmutableArray<(
            AssemblyContextParticipant Participant,
            ResolvedAssemblyReference Assembly)> retained =
        [
            .. group.Participants.Select(participant =>
                (
                    participant,
                    ReferenceEquals(
                        participant.Assembly.Registration,
                        focusAssembly)
                        ? focusSnapshot.RetainAssemblyReference(
                            participant.Assembly)
                        : participant.Assembly)),
        ];
        IAssemblyBindingPolicy policy =
            SourceRelativeAssemblyGroupBindingPolicy
                .CreateParticipantBoundSelections(
                    retained.Select(item => (
                        item.Assembly,
                        item.Participant.BindingPolicy)));
        ResolvedAssemblyReference focus = retained
            .Single(item => ReferenceEquals(
                item.Participant.Assembly.Registration,
                focusAssembly))
            .Assembly;
        return new(retained, policy, focus);
    }

    static Planning.WorkDescription CreateFocusedWork<TResult>(
        Planning.ProducerDeclaration<TResult> producer) =>
        Planning.ProducerPlanner.Plan(
            [
                new Planning.ProducerRequest(
                    producer,
                    Planning.ProducerTerminal.Rows),
            ])
        is Planning.ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new Planning.ProducerContractException(
                "The focused Method-source request must plan.");

    static TypeCallCompatibilityUnresolvedOccurrence Unresolved(
        AssemblyContextSubject source,
        SourceCall call,
        string limitation) =>
        new(
            source,
            call.Source,
            call.Site,
            limitation);

    static AssemblyContextTypeCallCompatibilityResult Unavailable(
        AssemblyAcquisitionRegistration focusAssembly,
        MetadataTypeDefinitionName focusType) =>
        new(
            focusAssembly,
            focusType,
            [],
            [],
            [],
            TypeCallCompatibilityTopologyDisposition.Unavailable);

    static AssemblyContextParticipant RequireParticipant(
        AssemblyContextGroup group,
        AssemblyAcquisitionRegistration registration)
    {
        AssemblyContextParticipant? participant =
            group.Participants.SingleOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Assembly.Registration,
                    registration));
        return participant
            ?? throw new ArgumentException(
                "The exact assembly registration is not a group participant.",
                nameof(registration));
    }

    sealed record Execution<TResult>(
        TResult Value,
        Planning.MethodDefinitionSourceReceipt Receipt);

    sealed record SourceCall(
        Analysis.MethodIdentity Source,
        Planning.FocusedMethodCallSite Site);

    sealed record SourceAcquisition(
        AssemblyImageSnapshot Snapshot,
        TypeDefinitionHandle TypeHandle,
        ImmutableArray<Planning.FocusedMethodCallBody> Bodies,
        ImmutableArray<SourceCall> Calls,
        Planning.MethodDefinitionSourceReceipt? Receipt);

    sealed class ExternalCallPlan(
        Analysis.MemberRef member,
        DeclaringTypePlan declaringType)
    {
        public Analysis.MemberRef Member { get; } = member;
        public DeclaringTypePlan DeclaringType { get; } = declaringType;
        public List<SourceCall> Calls { get; } = [];
        public ResolvedAssemblyReference? TargetAssembly { get; set; }
        public MetadataTypeDefinitionAddress? TargetType { get; set; }
        public string? Limitation { get; set; }
        public TypeCallCompatibilityTargetDisposition? MetadataDisposition
        { get; set; }
    }

    sealed record TargetImage(
        AssemblyContextSubject Subject,
        AssemblyImageSnapshot Snapshot,
        ImmutableArray<MetadataTypeDefinitionAddress> Types,
        ImmutableArray<Analysis.MethodIdentity> Methods,
        Planning.MethodDefinitionSourceReceipt Receipt);

    sealed record ResolvedTarget(
        AssemblyContextSubject Subject,
        Analysis.MethodIdentity Method);

    sealed record ResolvedCall(
        SourceCall Call,
        ResolvedTarget Target);

    sealed class ResolutionContextInputs(
        ImmutableArray<(
            AssemblyContextParticipant Participant,
            ResolvedAssemblyReference Assembly)> participants,
        IAssemblyBindingPolicy policy,
        ResolvedAssemblyReference focusAssembly)
    {
        public ResolvedAssemblyReference FocusAssembly { get; } =
            focusAssembly;

        public TypeResolutionContext CreateContext(
            IEnumerable<TypeResolutionRequest> requests) =>
            TypeResolutionContext.Create(
                policy,
                participants.Select(static item => item.Assembly),
                requests);
    }
}
