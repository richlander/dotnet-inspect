using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Adapts the declared-source relation to Library Body Analysis plans and
/// results: it widens a scoped plan to the generated bodies whose declared
/// source is in scope, and publishes declared sources onto the aggregate
/// result. It holds no relation judgment of its own and is deleted with
/// Library Body Analysis (#9959).
/// <c>ScopeDiagnosticAggregation_FinalPublicationRetainsMetadataOrder</c>
/// gates final publication.
/// </summary>
internal sealed class LibraryBodyDeclaredSourcePublication(
    MetadataReader reader,
    LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
    LiftedSourceOwnerResolver liftedSourceOwnerResolver,
    StateMachineSourceResolver stateMachineSourceResolver,
    DeclaredSourceResolver declaredSourceResolver,
    ImplementationMetricWorkBudget? implementationMetricWork)
{
    readonly MetadataReader _reader = reader;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver = primaryMetadataResolver;
    readonly LiftedSourceOwnerResolver
        _liftedSourceOwnerResolver = liftedSourceOwnerResolver;
    readonly StateMachineSourceResolver
        _stateMachineSourceResolver = stateMachineSourceResolver;
    readonly DeclaredSourceResolver
        _declaredSourceResolver = declaredSourceResolver;
    readonly ImplementationMetricWorkBudget?
        _implementationMetricWork = implementationMetricWork;

    internal LibraryBodyAnalysisPlan ExpandEvidenceScope(
        LibraryBodyAnalysisPlan plan)
    {
        // A lifted source method can itself be async, so expand source owners
        // before asking the async resolver for the resulting state-machine body.
        plan = ExpandStateMachineEvidenceScope(
            plan,
            IsAsyncScopeSourceAdmissible);
        plan = ExpandLiftedEvidenceScope(plan);
        return ExpandStateMachineEvidenceScope(
            plan,
            IsAsyncScopeSourceAdmissible);
    }

    internal LibraryBodyAnalysisResult MergeScopeExpansionDiagnostics(
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan)
    {
        if (plan.ScopeExpansionDiagnostics.IsDefaultOrEmpty)
            return analysis;

        return analysis with
        {
            Diagnostics = AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    analysis.Diagnostics,
                    plan.ScopeExpansionDiagnostics),
        };
    }

    internal LibraryBodyAnalysisResult PublishDeclaredSources(
        LibraryBodyAnalysisResult analysis,
        LibraryBodyAnalysisPlan plan)
    {
        IReadOnlyDictionary<int, MethodIdentity> asyncSources =
            _stateMachineSourceResolver
                .DeclaredSourceMethodsByMoveNextToken();
        if (asyncSources.Count == 0)
            return analysis;

        var declaredSources = new Dictionary<int, MethodIdentity>(
            analysis.Methods.DeclaredSources);
        var publicationDiagnostics =
            ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        foreach ((int token, MethodIdentity source) in asyncSources)
        {
            try
            {
                if (!declaredSources.ContainsKey(token))
                {
                    AuthenticatedSourceOwner sourceOwner =
                        _stateMachineSourceResolver
                            .CreateAuthenticatedSourceOwner(
                                source);
                    if (_declaredSourceResolver.TryResolveUltimateLiftedOwner(
                            sourceOwner,
                            out AuthenticatedSourceOwner
                                ultimateOwner))
                    {
                        declaredSources.Add(
                            token,
                            ultimateOwner.Method);
                    }
                    else if (!plan.IsScoped
                        && !_declaredSourceResolver.IsMalformedGeneratedLiftedOwner(
                            sourceOwner))
                    {
                        declaredSources.Add(token, source);
                    }
                }
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                var sourceHandle =
                    (MethodDefinitionHandle)
                    MetadataTokens.EntityHandle(
                        source.MetadataToken);
                MethodDefinition sourceDefinition =
                    _reader.GetMethodDefinition(
                        sourceHandle);
                var diagnostic = new AnalysisDiagnostic(
                    source.MetadataToken,
                    LibraryMethodAnalysisRunner.MethodLabel(
                        _reader,
                        sourceDefinition.GetDeclaringType(),
                        sourceHandle),
                    $"{ex.GetType().Name}: {ex.Message}",
                    DeclaringType: source.DeclaringType);
                publicationDiagnostics.Add(diagnostic);
            }
        }
        return analysis with
        {
            Diagnostics = AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    analysis.Diagnostics,
                    publicationDiagnostics.ToImmutable()),
            Methods = analysis.Methods with
            {
                DeclaredSources = declaredSources,
            },
        };
    }

    bool IsAsyncScopeSourceAdmissible(
        MethodIdentity source)
    {
        AuthenticatedSourceOwner sourceOwner =
            _stateMachineSourceResolver
                .CreateAuthenticatedSourceOwner(source);
        if (_declaredSourceResolver.IsMalformedGeneratedLiftedOwner(sourceOwner))
            return false;

        return !_liftedSourceOwnerResolver
            .HasMalformedPotentialOwnerChain(source);
    }

    LibraryBodyAnalysisPlan ExpandLiftedEvidenceScope(
        LibraryBodyAnalysisPlan plan)
    {
        if (!(plan.Includes(
                LibraryBodyAnalysisFeatures.MethodEvidence)
                || plan.ImplementationMetrics is not null)
            || !plan.IsScoped)
        {
            return plan;
        }

        var ownersByBody =
            new Dictionary<MethodIdentity, MethodIdentity>();
        ImmutableArray<AnalysisDiagnostic>.Builder diagnostics =
            plan.ScopeExpansionDiagnostics.IsDefault
                ? ImmutableArray.CreateBuilder<AnalysisDiagnostic>()
                : plan.ScopeExpansionDiagnostics.ToBuilder();
        bool attributionBudgetExhausted = false;
        foreach (TypeDefinitionHandle typeHandle
            in _reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition =
                _reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle
                in typeDefinition.GetMethods())
            {
                MethodIdentity method;
                try
                {
                    MethodDefinition methodDefinition =
                        _reader.GetMethodDefinition(methodHandle);
                    GenericScope scope =
                        _primaryMetadataResolver.CreateScope(
                            typeDefinition,
                            methodDefinition);
                    method =
                        _primaryMetadataResolver.CreateMethodIdentity(
                            typeHandle,
                            methodHandle,
                            methodDefinition,
                            scope);
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(ex))
                {
                    continue;
                }

                try
                {
                    MethodDefinition methodDefinition =
                        _reader.GetMethodDefinition(methodHandle);
                    bool directlySelectedBody =
                        plan.RequestedMethodScope?.Contains(
                            MetadataTokens.GetToken(methodHandle))
                            == true
                        || plan.TypeScope?.Invoke(
                            method.DeclaringType)
                            == true;
                    if (_liftedSourceOwnerResolver
                        .TryResolveForScopeExpansion(
                            methodHandle,
                            methodDefinition,
                            method,
                            out AuthenticatedSourceOwner sourceOwner,
                            plan.MethodScope,
                            plan.TypeScope,
                            directlySelectedBody))
                    {
                        if (_declaredSourceResolver.IsMalformedGeneratedLiftedOwner(
                                sourceOwner))
                        {
                            continue;
                        }
                        ownersByBody[method] =
                            sourceOwner.Method;
                    }
                }
                catch (
                    ImplementationMetricWorkLimitExceededException ex)
                {
                    if (!attributionBudgetExhausted)
                    {
                        diagnostics.Add(new AnalysisDiagnostic(
                            ex.MethodToken,
                            LibraryMethodAnalysisRunner
                                .MethodLabel(
                                    _reader,
                                    typeHandle,
                                    methodHandle),
                            ex.Message,
                            DeclaringType: method.DeclaringType));
                    }
                    attributionBudgetExhausted = true;
                }
                catch (Exception ex)
                    when (LibraryMethodAnalysisRunner
                        .IsRecoverableMethodFailure(ex))
                {
                    diagnostics.Add(new AnalysisDiagnostic(
                        method.MetadataToken,
                        LibraryMethodAnalysisRunner
                            .MethodLabel(
                                _reader,
                                typeHandle,
                                methodHandle),
                        $"{ex.GetType().Name}: {ex.Message}",
                        DeclaringType: method.DeclaringType));
                }
            }
        }
        ImplementationMetricWorkBudgetSnapshot? work =
            _implementationMetricWork?.Snapshot();
        if (!attributionBudgetExhausted
            && work?.AttributionExhaustedLimit is { }
                attributionLimit
            && work.AttributionExhaustedMethodToken is { }
                attributionToken)
        {
            diagnostics.Add(new AnalysisDiagnostic(
                attributionToken,
                $"0x{attributionToken:X8}",
                ImplementationMetricWorkLimitExceededException
                    .MessageFor(attributionLimit)));
        }

        IReadOnlySet<int>? methodScope = plan.MethodScope;
        if (methodScope is not null)
        {
            var expanded = new HashSet<int>(methodScope);
            foreach ((
                MethodIdentity body,
                MethodIdentity owner)
                in ownersByBody)
            {
                MethodIdentity declared =
                    ResolveDeclaredMethod(
                        owner,
                        ownersByBody);
                if (methodScope.Contains(
                        declared.MetadataToken))
                {
                    expanded.Add(body.MetadataToken);
                }
            }
            methodScope = expanded;
        }

        Dictionary<int, ImmutableArray<TypeRef>>?
            evidenceSources =
            plan.TypeScopeEvidenceSources is null
                ? null
                : new Dictionary<
                    int,
                    ImmutableArray<TypeRef>>(
                    plan.TypeScopeEvidenceSources);
        if (plan.TypeScope is not null)
        {
            evidenceSources ??= [];
            foreach ((
                MethodIdentity body,
                MethodIdentity owner)
                in ownersByBody)
            {
                TypeRef declaredSourceType =
                    ResolveDeclaredMethod(
                        owner,
                        ownersByBody)
                    .DeclaringType;
                ImmutableArray<TypeRef> existing =
                    evidenceSources.GetValueOrDefault(
                        body.MetadataToken);
                if (existing.IsDefault)
                    existing = [];
                if (!existing.Contains(declaredSourceType))
                {
                    evidenceSources[body.MetadataToken] =
                        existing.Add(declaredSourceType);
                }
            }
        }

        return plan with
        {
            MethodScope = methodScope,
            TypeScopeEvidenceSources = evidenceSources,
            ScopeExpansionDiagnostics = diagnostics.ToImmutable(),
        };
    }

    static MethodIdentity ResolveDeclaredMethod(
        MethodIdentity method,
        IReadOnlyDictionary<MethodIdentity, MethodIdentity>
            ownersByBody)
    {
        MethodIdentity current = method;
        for (int depth = 0;
            depth <= ownersByBody.Count;
            depth++)
        {
            if (!ownersByBody.TryGetValue(
                    current,
                    out MethodIdentity? owner)
                || owner == current)
            {
                return current;
            }
            current = owner;
        }

        throw new InvalidOperationException(
            "Lifted source-owner resolution contains a cycle.");
    }

    LibraryBodyAnalysisPlan ExpandStateMachineEvidenceScope(
        LibraryBodyAnalysisPlan plan,
        Func<MethodIdentity, bool> sourceAdmitted)
    {
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        Dictionary<int, ImmutableArray<TypeRef>>?
            typeScopeEvidenceSources =
                plan.TypeScopeEvidenceSources is null
                    ? null
                    : new Dictionary<
                        int,
                        ImmutableArray<TypeRef>>(
                        plan.TypeScopeEvidenceSources);
        if ((plan.Includes(
                LibraryBodyAnalysisFeatures.MethodEvidence)
                || plan.ImplementationMetrics is not null)
            && (bodyScope is not null
                || plan.TypeScope is not null))
        {
            bool mapRequired =
                plan.TypeScope is not null
                || bodyScope is not null
                    && _stateMachineSourceResolver.ScopeMayRequireStateMachineBody(
                        bodyScope);
            if (mapRequired)
            {
                var expandedScope =
                    bodyScope is null
                        ? new HashSet<int>()
                        : new HashSet<int>(bodyScope);
                typeScopeEvidenceSources ??= [];
                foreach ((
                    int moveNextToken,
                    MethodIdentity source)
                    in _stateMachineSourceResolver.ExecutionSourceMethodsByMoveNextToken())
                {
                    if (_stateMachineSourceResolver.HasMalformedCompilerGeneratedLiftedName(
                            source)
                        || !sourceAdmitted(source))
                    {
                        continue;
                    }
                    AddEvidenceSource(
                        typeScopeEvidenceSources,
                        moveNextToken,
                        source.DeclaringType);
                    if (typeScopeEvidenceSources.TryGetValue(
                            source.MetadataToken,
                            out ImmutableArray<TypeRef>
                                liftedSourceTypes))
                    {
                        foreach (TypeRef liftedSourceType
                            in liftedSourceTypes)
                        {
                            AddEvidenceSource(
                                typeScopeEvidenceSources,
                                moveNextToken,
                                liftedSourceType);
                        }
                    }
                    if (bodyScope?.Contains(
                            source.MetadataToken)
                        == true)
                    {
                        expandedScope.Add(moveNextToken);
                    }
                }
                if (bodyScope is not null)
                    bodyScope = expandedScope;
            }
        }
        return plan with
        {
            MethodScope = bodyScope,
            TypeScopeEvidenceSources =
                typeScopeEvidenceSources,
        };
    }

    static void AddEvidenceSource(
        Dictionary<int, ImmutableArray<TypeRef>> sources,
        int evidenceToken,
        TypeRef sourceType)
    {
        ImmutableArray<TypeRef> existing =
            sources.GetValueOrDefault(evidenceToken);
        if (existing.IsDefault)
            existing = [];
        if (!existing.Contains(sourceType))
        {
            sources[evidenceToken] =
                existing.Add(sourceType);
        }
    }
}
