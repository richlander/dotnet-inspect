using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

static class PlatformTypeDefinitionResolutionProjection
{
    public static PlatformTypeDefinitionResolutionResult Project(
        TypeResolutionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new ProjectionContext().Outcome(outcome);
    }

    sealed class ProjectionContext
    {
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            PlatformTypeResolutionAssemblyEvidence> _assemblies =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<
            AssemblyBindingLineage,
            PlatformTypeResolutionLineageIdentity> _lineages =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<
            UnresolvedBindingReference,
            PlatformTypeResolutionUnresolvedBindingIdentity>
                _unresolvedBindings =
                    new(ReferenceEqualityComparer.Instance);

        public PlatformTypeDefinitionResolutionResult Outcome(
            TypeResolutionOutcome outcome)
        {
            ImmutableArray<PlatformTypeForwardingHopEvidence> hops =
            [
                .. outcome.Hops.Select(Hop),
            ];
            PlatformTypeResolutionOccurrenceEvidence? terminal =
                outcome.TerminalOccurrence is { } occurrence
                    ? Occurrence(occurrence)
                    : null;
            return outcome switch
            {
                TypeResolutionOutcome.Resolved resolved =>
                    new PlatformTypeDefinitionResolutionResult.Resolved(
                        Definition(resolved.Definition),
                        hops,
                        terminal,
                        outcome.TerminalAssemblyIdentity),
                TypeResolutionOutcome.NotFound notFound =>
                    new PlatformTypeDefinitionResolutionResult.NotFound(
                        Candidate(notFound.LastAssembly),
                        Occurrence(notFound.LastOccurrence),
                        hops,
                        terminal,
                        outcome.TerminalAssemblyIdentity),
                TypeResolutionOutcome.UnboundBinding unbound =>
                    new PlatformTypeDefinitionResolutionResult.UnboundBinding(
                        UnresolvedBinding(unbound.Binding),
                        unbound.Target,
                        Origin(unbound.Origin),
                        unbound.Scope,
                        hops,
                        terminal,
                        outcome.TerminalAssemblyIdentity),
                TypeResolutionOutcome.Unavailable unavailable =>
                    new PlatformTypeDefinitionResolutionResult.Unavailable(
                        UnresolvedBinding(unavailable.Binding),
                        unavailable.Target,
                        Origin(unavailable.Origin),
                        unavailable.Scope,
                        unavailable.Failure,
                        hops,
                        terminal,
                        outcome.TerminalAssemblyIdentity),
                TypeResolutionOutcome.Ambiguous ambiguous =>
                    new PlatformTypeDefinitionResolutionResult.Ambiguous(
                        Ambiguity(ambiguous.Ambiguity),
                        hops,
                        terminal,
                        outcome.TerminalAssemblyIdentity),
                TypeResolutionOutcome.Rejected rejected =>
                    new PlatformTypeDefinitionResolutionResult.Rejected(
                        Failure(rejected.Failure),
                        hops,
                        terminal,
                        outcome.TerminalAssemblyIdentity),
                _ => throw new InvalidOperationException(
                    "Unknown type-resolution outcome."),
            };
        }

        PlatformTypeResolutionDefinitionEvidence Definition(
            ResolvedTypeDefinition definition) =>
            new(
                definition.Address,
                Candidate(definition.Assembly),
                Occurrence(definition.Occurrence),
                definition.Type,
                definition.Kind,
                definition.DeclaringAssemblyDefinesCoreLibraryRoot,
                definition.KindResolutionFailure is { } failure
                    ? Failure(failure)
                    : null,
                definition.KindResolutionDependencyAssembly);

        PlatformTypeForwardingHopEvidence Hop(TypeForwardingHop hop) =>
            new(
                Candidate(hop.SourceAssembly),
                Occurrence(hop.SourceOccurrence),
                hop.Declarations,
                hop.TargetReference,
                hop.Scope);

        PlatformTypeResolutionCandidateEvidence Candidate(
            ResolvedAssemblyCandidate candidate) =>
            new(Assembly(candidate.Assembly));

        PlatformTypeResolutionOccurrenceEvidence Occurrence(
            AssemblyBindingOccurrence occurrence) =>
            new(
                Assembly(occurrence.Assembly),
                Lineage(occurrence.Lineage));

        PlatformTypeResolutionAssemblyEvidence Assembly(
            ResolvedAssemblyReference assembly)
        {
            AssemblyAcquisitionRegistration registration =
                assembly.Registration;
            if (_assemblies.TryGetValue(
                    registration,
                    out PlatformTypeResolutionAssemblyEvidence? evidence))
            {
                return evidence;
            }

            evidence = new(
                registration,
                assembly.Identity,
                registration.ModuleVersionId,
                assembly.Provenance);
            _assemblies.Add(registration, evidence);
            return evidence;
        }

        PlatformTypeResolutionLineageIdentity Lineage(
            AssemblyBindingLineage lineage)
        {
            if (!_lineages.TryGetValue(
                    lineage,
                    out PlatformTypeResolutionLineageIdentity? identity))
            {
                identity = new();
                _lineages.Add(lineage, identity);
            }
            return identity;
        }

        PlatformTypeResolutionUnresolvedBindingIdentity UnresolvedBinding(
            UnresolvedBindingReference binding)
        {
            if (!_unresolvedBindings.TryGetValue(
                    binding,
                    out PlatformTypeResolutionUnresolvedBindingIdentity?
                        identity))
            {
                identity = new();
                _unresolvedBindings.Add(binding, identity);
            }
            return identity;
        }

        PlatformTypeResolutionBindingOriginEvidence Origin(
            AssemblyBindingOrigin origin) =>
            origin switch
            {
                AssemblyBindingOrigin.GlobalOrigin =>
                    new PlatformTypeResolutionBindingOriginEvidence.Global(),
                AssemblyBindingOrigin.RequestingAssembly requesting =>
                    new PlatformTypeResolutionBindingOriginEvidence
                        .RequestingAssembly(
                            Assembly(requesting.Assembly),
                            requesting.Occurrence is { } occurrence
                                ? Occurrence(occurrence)
                                : null,
                            requesting.Lineage is { } lineage
                                ? Lineage(lineage)
                                : null),
                _ => throw new InvalidOperationException(
                    "Unknown assembly-binding origin."),
            };

        PlatformTypeResolutionAmbiguityEvidence Ambiguity(
            TypeResolutionAmbiguity ambiguity) =>
            ambiguity switch
            {
                TypeResolutionAmbiguity.AssemblyBinding binding =>
                    new PlatformTypeResolutionAmbiguityEvidence.AssemblyBinding(
                        binding.Target,
                        Origin(binding.Origin),
                        binding.Scope,
                        [.. binding.Candidates.Select(Candidate)]),
                TypeResolutionAmbiguity.TypeDeclaration declaration =>
                    new PlatformTypeResolutionAmbiguityEvidence.TypeDeclaration(
                        Candidate(declaration.Assembly),
                        Occurrence(declaration.Occurrence),
                        declaration.Type,
                        [.. declaration.Candidates.Select(Declaration)]),
                _ => throw new InvalidOperationException(
                    "Unknown type-resolution ambiguity."),
            };

        static PlatformTypeDeclarationCandidateEvidence Declaration(
            TypeDeclarationCandidate declaration) =>
            declaration switch
            {
                TypeDeclarationCandidate.Definition definition =>
                    new PlatformTypeDeclarationCandidateEvidence.Definition(
                        definition.Token,
                        definition.Kind,
                        definition.IsInterface,
                        definition.IsValueType,
                        definition.KindFailure),
                TypeDeclarationCandidate.Forwarder forwarder =>
                    new PlatformTypeDeclarationCandidateEvidence.Forwarder(
                        forwarder.Declarations,
                        forwarder.Target),
                TypeDeclarationCandidate.ModuleExport module =>
                    new PlatformTypeDeclarationCandidateEvidence.ModuleExport(
                        module.Declarations,
                        module.Module),
                _ => throw new InvalidOperationException(
                    "Unknown type-declaration candidate."),
            };

        PlatformTypeResolutionFailureEvidence Failure(
            TypeResolutionFailure failure) =>
            failure switch
            {
                TypeResolutionFailure.DeclarationRejected rejected =>
                    new PlatformTypeResolutionFailureEvidence
                        .DeclarationRejected(rejected.Rejection),
                TypeResolutionFailure.DeclarationBudgetExceeded exceeded =>
                    new PlatformTypeResolutionFailureEvidence
                        .DeclarationBudgetExceeded(
                            exceeded.Budget,
                            exceeded.Detail),
                TypeResolutionFailure.DefinitionKindUnavailable unavailable =>
                    new PlatformTypeResolutionFailureEvidence
                        .DefinitionKindUnavailable(unavailable.Failure),
                TypeResolutionFailure.ForwarderCycle =>
                    new PlatformTypeResolutionFailureEvidence.ForwarderCycle(),
                TypeResolutionFailure.HopBudgetExceeded exceeded =>
                    new PlatformTypeResolutionFailureEvidence
                        .HopBudgetExceeded(exceeded.Budget),
                TypeResolutionFailure.RequestBudgetExceeded exceeded =>
                    new PlatformTypeResolutionFailureEvidence
                        .RequestBudgetExceeded(exceeded.Budget),
                TypeResolutionFailure.UnsupportedModuleExport unsupported =>
                    new PlatformTypeResolutionFailureEvidence
                        .UnsupportedModuleExport(unsupported.Module),
                TypeResolutionFailure.UnsupportedModuleReference unsupported =>
                    new PlatformTypeResolutionFailureEvidence
                        .UnsupportedModuleReference(unsupported.ModuleName),
                TypeResolutionFailure.UnregisteredAssembly unregistered =>
                    new PlatformTypeResolutionFailureEvidence
                        .UnregisteredAssembly(unregistered.Registration),
                TypeResolutionFailure.InvalidBindingPolicy invalid =>
                    new PlatformTypeResolutionFailureEvidence
                        .InvalidBindingPolicy(invalid.Failure),
                TypeResolutionFailure.CandidateOpenFailed open =>
                    new PlatformTypeResolutionFailureEvidence
                        .CandidateOpenFailed(
                            Assembly(open.Assembly),
                            open.Failure),
                TypeResolutionFailure.KindDependencyUnbound unbound =>
                    new PlatformTypeResolutionFailureEvidence
                        .KindDependencyUnbound(
                            unbound.Target,
                            Origin(unbound.Origin),
                            unbound.Scope),
                TypeResolutionFailure.KindDependencyUnavailable unavailable =>
                    new PlatformTypeResolutionFailureEvidence
                        .KindDependencyUnavailable(
                            unavailable.Target,
                            Origin(unavailable.Origin),
                            unavailable.Scope,
                            unavailable.Failure),
                TypeResolutionFailure.KindDependencyCycle cycle =>
                    new PlatformTypeResolutionFailureEvidence
                        .KindDependencyCycle(
                            cycle.Target,
                            Origin(cycle.Origin),
                            cycle.Scope),
                TypeResolutionFailure.KindDependencyTypeNotFound notFound =>
                    new PlatformTypeResolutionFailureEvidence
                        .KindDependencyTypeNotFound(
                            Candidate(notFound.Assembly),
                            Occurrence(notFound.Occurrence),
                            notFound.Type),
                TypeResolutionFailure.KindDependencyAmbiguous ambiguous =>
                    new PlatformTypeResolutionFailureEvidence
                        .KindDependencyAmbiguous(
                            Ambiguity(ambiguous.Ambiguity),
                            ambiguous.Type),
                TypeResolutionFailure.DiscoveryBudgetExceeded exceeded =>
                    new PlatformTypeResolutionFailureEvidence
                        .DiscoveryBudgetExceeded(exceeded.Budget),
                TypeResolutionFailure.PlanExpansionRequired required =>
                    new PlatformTypeResolutionFailureEvidence
                        .PlanExpansionRequired(
                            PlanRequest(required.Request)),
                _ => throw new InvalidOperationException(
                    "Unknown type-resolution failure."),
            };

        PlatformResolutionPlanRequestEvidence PlanRequest(
            ResolutionPlanRequest request) =>
            request switch
            {
                ResolutionPlanRequest.Type type =>
                    new PlatformResolutionPlanRequestEvidence.Type(
                        new(
                            Start(type.Request.Start),
                            type.Request.Type)),
                ResolutionPlanRequest.Binding binding =>
                    new PlatformResolutionPlanRequestEvidence.Binding(
                        new(
                            binding.Request.Target,
                            Origin(binding.Request.Origin),
                            binding.Request.Scope)),
                _ => throw new InvalidOperationException(
                    "Unknown resolution-plan request."),
            };

        PlatformTypeResolutionStartEvidence Start(TypeResolutionStart start) =>
            start switch
            {
                TypeResolutionStart.Assembly assembly =>
                    new PlatformTypeResolutionStartEvidence.Assembly(
                        Assembly(assembly.Value),
                        assembly.Occurrence is { } occurrence
                            ? Occurrence(occurrence)
                            : null,
                        assembly.Scope),
                TypeResolutionStart.Reference reference =>
                    new PlatformTypeResolutionStartEvidence.Reference(
                        reference.Value,
                        Origin(reference.Origin),
                        reference.Scope),
                TypeResolutionStart.CoreLibrary core =>
                    new PlatformTypeResolutionStartEvidence.CoreLibrary(
                        (PlatformTypeResolutionBindingOriginEvidence
                            .RequestingAssembly)Origin(core.Origin),
                        core.Scope),
                TypeResolutionStart.Module module =>
                    new PlatformTypeResolutionStartEvidence.Module(
                        module.Name,
                        (PlatformTypeResolutionBindingOriginEvidence
                            .RequestingAssembly)Origin(module.Origin)),
                _ => throw new InvalidOperationException(
                    "Unknown type-resolution start."),
            };
    }
}
