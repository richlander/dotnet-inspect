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
                .IsLocalFunctionOrLambda(asyncSource.Name)
            && !TryResolveUltimateLiftedOwner(
                asyncSource,
                out _))
        {
            return false;
        }
        return true;
    }

    internal bool TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out MethodIdentity? sourceOwner,
        out bool sourceGenerated,
        IReadOnlySet<int>? ownerMethodScope,
        Func<TypeRef, bool>? ownerTypeScope,
        bool directlySelectedBody) =>
        _liftedSourceOwnerResolver.TryResolve(
            liftedHandle,
            liftedMethod,
            liftedIdentity,
            out sourceOwner,
            out sourceGenerated,
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
                out MethodIdentity? sourceOwner,
                out _,
                ownerMethodScope,
                ownerTypeScope,
                directlySelectedBody))
        {
            return sourceOwner;
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
                out _,
                ownerMethodScope,
                ownerTypeScope,
                directlySelectedBody
                    || requestedMethodScope?.Contains(
                        asyncSource.MetadataToken)
                        == true))
        {
            return sourceOwner;
        }

        return TryResolveUltimateLiftedOwner(
            asyncSource,
            out sourceOwner)
            ? sourceOwner
            : null;
    }

    internal DeclaredOwnerResolution ResolveUltimateDeclaredMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodIdentity method,
        bool typeSourceGenerated,
        out MethodIdentity? ultimateOwner)
    {
        if (_liftedSourceOwnerResolver.TryResolve(
                methodHandle,
                methodDefinition,
                method,
                out MethodIdentity? liftedOwner,
                out _,
                ownerMethodScope: null,
                ownerTypeScope: null,
                directlySelectedBody: false)
            && liftedOwner is not null)
        {
            return TryResolveUltimateLiftedOwner(
                liftedOwner,
                out ultimateOwner)
                ? DeclaredOwnerResolution.Resolved
                : DeclaredOwnerResolution.Unresolved;
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
            return DeclaredOwnerResolution.Unresolved;
        }
        if (asyncResolution == AsyncSourceResolution.None
            || asyncSource is null
            || asyncSource == method)
        {
            ultimateOwner = null;
            return DeclaredOwnerResolution.None;
        }

        if (CompilerGeneratedNames
                .IsLocalFunctionOrLambda(asyncSource.Name))
        {
            return TryResolveUltimateLiftedOwner(
                asyncSource,
                out ultimateOwner)
                ? DeclaredOwnerResolution.Resolved
                : DeclaredOwnerResolution.Unresolved;
        }

        ultimateOwner = asyncSource;
        return DeclaredOwnerResolution.Resolved;
    }

    internal LibraryBodyAnalysisPlan ExpandEvidenceScope(
        LibraryBodyAnalysisPlan plan)
    {
        // A lifted source method can itself be async, so expand source owners
        // before asking the async resolver for the resulting state-machine body.
        plan = _asyncSourceResolver.ExpandEvidenceScope(plan);
        plan = ExpandLiftedEvidenceScope(plan);
        return _asyncSourceResolver.ExpandEvidenceScope(plan);
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
        LibraryBodyAnalysisResult analysis)
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
                if (!declaredSources.ContainsKey(token)
                    && TryResolveUltimateLiftedOwner(
                        source,
                        out MethodIdentity? ultimateOwner)
                    && ultimateOwner is not null)
                {
                    declaredSources.Add(
                        token,
                        ultimateOwner);
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

    bool TryResolveUltimateLiftedOwner(
        MethodIdentity source,
        out MethodIdentity? ultimateOwner)
    {
        MethodIdentity current = source;
        Span<int> visited =
            stackalloc int[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        int count = 0;
        while (CompilerGeneratedNames
            .IsLocalFunctionOrLambda(current.Name))
        {
            if (count == visited.Length)
            {
                ultimateOwner = null;
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                if (visited[i]
                    == current.MetadataToken)
                {
                    ultimateOwner = null;
                    return false;
                }
            }
            visited[count++] = current.MetadataToken;
            EntityHandle currentHandle =
                MetadataTokens.EntityHandle(
                    current.MetadataToken);
            if (currentHandle.Kind
                    != HandleKind.MethodDefinition)
            {
                ultimateOwner = null;
                return false;
            }
            MethodDefinition currentDefinition =
                _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)currentHandle);
            if (!_liftedSourceOwnerResolver.TryResolve(
                    (MethodDefinitionHandle)currentHandle,
                    currentDefinition,
                    current,
                    out MethodIdentity? sourceOwner,
                    out _,
                    ownerMethodScope: null,
                    ownerTypeScope: null,
                    directlySelectedBody: false)
                || sourceOwner is null)
            {
                ultimateOwner = null;
                return false;
            }
            current = sourceOwner;
        }

        ultimateOwner = current;
        return true;
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
                            out MethodIdentity? sourceOwner,
                            out _,
                            plan.MethodScope,
                            plan.TypeScope,
                            directlySelectedBody)
                        && sourceOwner is not null)
                    {
                        ownersByBody[method] = sourceOwner;
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
