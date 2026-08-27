using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Composes async and lifted-method ownership into declared source methods,
/// including scoped evidence expansion and final declared-source publication.
/// It consumes acquisition-scoped resolvers without owning metadata lifetime.
/// <c>DirectCalls_AsyncLiftedMoveNextComposesToDeclaredOwner</c> and
/// <c>ScopeDiagnosticAggregation_FinalPublicationRetainsMetadataOrder</c>
/// gate representative composition and publication behavior.
/// </summary>
internal sealed class LibraryBodyDeclaredSourceResolver(
    MetadataReader reader,
    LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
    LibraryBodyLiftedSourceOwnerResolver liftedSourceOwnerResolver,
    LibraryBodyAsyncSourceResolver asyncSourceResolver)
{
    readonly MetadataReader _reader = reader;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver = primaryMetadataResolver;
    readonly LibraryBodyLiftedSourceOwnerResolver
        _liftedSourceOwnerResolver = liftedSourceOwnerResolver;
    readonly LibraryBodyAsyncSourceResolver
        _asyncSourceResolver = asyncSourceResolver;

    internal bool TryResolveAsyncSiblingSource(
        MethodIdentity method,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        [NotNullWhen(true)] ref MethodIdentity? asyncSource)
    {
        asyncSource = _asyncSourceResolver.ResolveSourceMethod(
            method,
            methodDefinition,
            typeSourceGenerated);
        if (asyncSource is null)
            return false;
        if (CompilerGeneratedNames
                .IsLocalFunctionOrLambda(asyncSource.Name))
        {
            AuthenticatedSourceOwner sourceOwner =
                CreateAuthenticatedSourceOwner(asyncSource);
            if (!TryResolveUltimateLiftedOwner(
                    sourceOwner,
                    out AuthenticatedSourceOwner ultimateOwner)
                || ultimateOwner.SuppressesOpportunities)
            {
                return false;
            }
        }
        else if (CreateAuthenticatedSourceOwner(
                asyncSource).SuppressesOpportunities)
            return false;
        return true;
    }

    internal bool TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out AuthenticatedSourceOwner sourceOwner,
        IReadOnlySet<int>? ownerMethodScope,
        Func<TypeRef, bool>? ownerTypeScope,
        bool directlySelectedBody) =>
        _liftedSourceOwnerResolver.TryResolve(
            liftedHandle,
            liftedMethod,
            liftedIdentity,
            out sourceOwner,
            ownerMethodScope,
            ownerTypeScope,
            directlySelectedBody);

    internal MethodIdentity? ResolveDeclaredMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodIdentity method,
        bool typeSourceGenerated,
        IReadOnlySet<int>? ownerMethodScope,
        Func<TypeRef, bool>? ownerTypeScope,
        IReadOnlySet<int>? requestedMethodScope,
        bool directlySelectedBody)
    {
        if (_liftedSourceOwnerResolver.TryResolve(
                methodHandle,
                methodDefinition,
                method,
                out AuthenticatedSourceOwner sourceOwner,
                ownerMethodScope,
                ownerTypeScope,
                directlySelectedBody))
        {
            return IsMalformedGeneratedLiftedOwner(sourceOwner)
                ? null
                : sourceOwner.Method;
        }

        MethodIdentity? asyncSource =
            _asyncSourceResolver.ResolveDeclaredSourceMethod(
                method,
                methodDefinition,
                typeSourceGenerated);
        if (asyncSource is null
            || asyncSource == method)
            return asyncSource;

        if (!CompilerGeneratedNames
            .IsLocalFunctionOrLambda(asyncSource.Name))
        {
            return asyncSource;
        }

        EntityHandle asyncSourceHandle =
            MetadataTokens.EntityHandle(
                asyncSource.MetadataToken);
        if (asyncSourceHandle.Kind
                == HandleKind.MethodDefinition
            && _liftedSourceOwnerResolver.TryResolve(
                (MethodDefinitionHandle)asyncSourceHandle,
                _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)asyncSourceHandle),
                asyncSource,
                out sourceOwner,
                ownerMethodScope,
                ownerTypeScope,
                directlySelectedBody
                    || requestedMethodScope?.Contains(
                        asyncSource.MetadataToken)
                        == true))
        {
            return IsMalformedGeneratedLiftedOwner(sourceOwner)
                ? null
                : sourceOwner.Method;
        }

        return TryResolveUltimateLiftedOwner(
            CreateAuthenticatedSourceOwner(asyncSource),
            out sourceOwner)
            ? sourceOwner.Method
            : null;
    }

    internal DeclaredOwnerResolution ResolveUltimateDeclaredMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodIdentity method,
        bool typeSourceGenerated,
        out AuthenticatedSourceOwner? immediateOwner,
        out AuthenticatedSourceOwner? ultimateOwner)
    {
        immediateOwner = null;
        LiftedSourceOwnerResolution liftedResolution =
            _liftedSourceOwnerResolver.Resolve(
                methodHandle,
                methodDefinition,
                method,
                out AuthenticatedSourceOwner liftedOwner,
                ownerMethodScope: null,
                ownerTypeScope: null,
                directlySelectedBody: false);
        if (liftedResolution
            == LiftedSourceOwnerResolution.Resolved)
        {
            immediateOwner = liftedOwner;
            DeclaredOwnerResolution resolution =
                ResolveUltimateLiftedOwner(
                liftedOwner,
                out AuthenticatedSourceOwner resolvedOwner);
            ultimateOwner =
                resolution == DeclaredOwnerResolution.Resolved
                    ? resolvedOwner
                    : null;
            return resolution;
        }
        if (liftedResolution
            == LiftedSourceOwnerResolution.Rejected)
        {
            ultimateOwner = null;
            return DeclaredOwnerResolution.Rejected;
        }

        AsyncSourceResolution asyncResolution =
            _asyncSourceResolver.ResolveSourceOwnership(
                method,
                methodDefinition,
                typeSourceGenerated,
                out MethodIdentity? asyncSource);
        if (asyncResolution == AsyncSourceResolution.Unresolved)
        {
            ultimateOwner = null;
            return DeclaredOwnerResolution.Rejected;
        }
        if (asyncResolution == AsyncSourceResolution.None
            || asyncSource is null
            || asyncSource == method)
        {
            ultimateOwner = null;
            bool stateMachineExecutionBody =
                _asyncSourceResolver
                    .IsAsyncStateMachineExecutionMethod(
                        methodHandle,
                        methodDefinition);
            if (stateMachineExecutionBody
                && CompilerGeneratedNames
                    .IsMalformedLiftedStateMachineLeaf(
                        method.DeclaringType))
            {
                return DeclaredOwnerResolution.Rejected;
            }
            bool canonicalOwnerRequiredBody =
                CompilerGeneratedNames
                    .IsLocalFunctionOrLambda(method.Name)
                || stateMachineExecutionBody;
            return canonicalOwnerRequiredBody
                ? DeclaredOwnerResolution.Unresolved
                : DeclaredOwnerResolution.None;
        }

        if (CompilerGeneratedNames
                .IsLocalFunctionOrLambda(asyncSource.Name))
        {
            AuthenticatedSourceOwner asyncSourceOwner =
                CreateAuthenticatedSourceOwner(asyncSource);
            immediateOwner = asyncSourceOwner;
            DeclaredOwnerResolution resolution =
                ResolveUltimateLiftedOwner(
                    asyncSourceOwner,
                    out AuthenticatedSourceOwner resolvedOwner);
            ultimateOwner =
                resolution == DeclaredOwnerResolution.Resolved
                    ? resolvedOwner
                    : null;
            return resolution;
        }

        AuthenticatedSourceOwner sourceOwner =
            CreateAuthenticatedSourceOwner(asyncSource);
        immediateOwner = sourceOwner;
        ultimateOwner = sourceOwner;
        return DeclaredOwnerResolution.Resolved;
    }

    internal LibraryBodyAnalysisPlan ExpandEvidenceScope(
        LibraryBodyAnalysisPlan plan)
    {
        // A lifted source method can itself be async, so expand source owners
        // before asking the async resolver for the resulting state-machine body.
        plan = _asyncSourceResolver.ExpandEvidenceScope(
            plan,
            IsAsyncScopeSourceAdmissible);
        plan = ExpandLiftedEvidenceScope(plan);
        return _asyncSourceResolver.ExpandEvidenceScope(
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
            _asyncSourceResolver
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
                        _asyncSourceResolver
                            .CreateAuthenticatedSourceOwner(
                                source);
                    if (TryResolveUltimateLiftedOwner(
                            sourceOwner,
                            out AuthenticatedSourceOwner
                                ultimateOwner))
                    {
                        declaredSources.Add(
                            token,
                            ultimateOwner.Method);
                    }
                    else if (!plan.IsScoped
                        && !IsMalformedGeneratedLiftedOwner(
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
            CreateAuthenticatedSourceOwner(source);
        if (IsMalformedGeneratedLiftedOwner(sourceOwner))
            return false;

        return !_liftedSourceOwnerResolver
            .HasMalformedPotentialOwnerChain(source);
    }

    bool TryResolveUltimateLiftedOwner(
        AuthenticatedSourceOwner source,
        out AuthenticatedSourceOwner ultimateOwner) =>
        ResolveUltimateLiftedOwner(
            source,
            out ultimateOwner)
            == DeclaredOwnerResolution.Resolved;

    DeclaredOwnerResolution ResolveUltimateLiftedOwner(
        AuthenticatedSourceOwner source,
        out AuthenticatedSourceOwner ultimateOwner)
    {
        AuthenticatedSourceOwner current = source;
        Span<int> visited =
            stackalloc int[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        int count = 0;
        while (CompilerGeneratedNames
            .IsLocalFunctionOrLambda(
                current.Method.Name))
        {
            if (count == visited.Length)
            {
                ultimateOwner = default;
                return DeclaredOwnerResolution.Rejected;
            }
            for (int i = 0; i < count; i++)
            {
                if (visited[i]
                    == current.Method.MetadataToken)
                {
                    ultimateOwner = default;
                    return DeclaredOwnerResolution.Rejected;
                }
            }
            visited[count++] =
                current.Method.MetadataToken;
            EntityHandle currentHandle =
                MetadataTokens.EntityHandle(
                    current.Method.MetadataToken);
            if (currentHandle.Kind
                    != HandleKind.MethodDefinition)
            {
                ultimateOwner = default;
                return DeclaredOwnerResolution.Rejected;
            }
            MethodDefinition currentDefinition =
                _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)currentHandle);
            LiftedSourceOwnerResolution resolution =
                _liftedSourceOwnerResolver.Resolve(
                    (MethodDefinitionHandle)currentHandle,
                    currentDefinition,
                    current.Method,
                    out AuthenticatedSourceOwner sourceOwner,
                    ownerMethodScope: null,
                    ownerTypeScope: null,
                    directlySelectedBody: false);
            if (resolution != LiftedSourceOwnerResolution.Resolved)
            {
                ultimateOwner = default;
                return resolution
                    == LiftedSourceOwnerResolution.Rejected
                        ? DeclaredOwnerResolution.Rejected
                        : DeclaredOwnerResolution.Unresolved;
            }
            current = sourceOwner;
        }

        if (IsMalformedGeneratedLiftedOwner(current))
        {
            ultimateOwner = default;
            return DeclaredOwnerResolution.Rejected;
        }

        ultimateOwner = current;
        return DeclaredOwnerResolution.Resolved;
    }

    bool IsMalformedGeneratedLiftedOwner(
        AuthenticatedSourceOwner source) =>
        _asyncSourceResolver
            .HasMalformedCompilerGeneratedLiftedName(source);

    AuthenticatedSourceOwner CreateAuthenticatedSourceOwner(
        MethodIdentity source)
    {
        AuthenticatedSourceOwner owner =
            _asyncSourceResolver
                .CreateAuthenticatedSourceOwner(source);
        return owner with
        {
            IsAuthenticatedTopLevelEntryPoint =
                _liftedSourceOwnerResolver
                    .IsAuthenticatedTopLevelEntryPoint(source),
        };
    }

    LibraryBodyAnalysisPlan ExpandLiftedEvidenceScope(
        LibraryBodyAnalysisPlan plan)
    {
        if (!plan.Includes(
                LibraryBodyAnalysisFeatures.MethodEvidence)
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
                    if (_liftedSourceOwnerResolver.TryResolve(
                            methodHandle,
                            methodDefinition,
                            method,
                            out AuthenticatedSourceOwner sourceOwner,
                            plan.MethodScope,
                            plan.TypeScope,
                            directlySelectedBody))
                    {
                        if (IsMalformedGeneratedLiftedOwner(
                                sourceOwner))
                        {
                            continue;
                        }
                        ownersByBody[method] =
                            sourceOwner.Method;
                    }
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
}
