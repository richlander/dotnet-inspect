using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Composes async and lifted-method ownership into declared source methods.
/// It consumes acquisition-scoped resolvers without owning metadata lifetime.
/// <c>DirectCalls_AsyncLiftedMoveNextComposesToDeclaredOwner</c> gates
/// representative composition.
/// </summary>
/// <remarks>
/// Owned by <c>docs/design/method-query-source.md#declared-source-relation</c>.
/// Library Body Analysis reaches it through
/// <see cref="LibraryBodyDeclaredSourcePublication"/>.
/// </remarks>
internal sealed class DeclaredSourceResolver(
    MetadataReader reader,
    LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
    LiftedSourceOwnerResolver liftedSourceOwnerResolver,
    StateMachineSourceResolver stateMachineSourceResolver)
{
    readonly MetadataReader _reader = reader;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver = primaryMetadataResolver;
    readonly LiftedSourceOwnerResolver
        _liftedSourceOwnerResolver = liftedSourceOwnerResolver;
    readonly StateMachineSourceResolver
        _stateMachineSourceResolver = stateMachineSourceResolver;

    internal bool TryResolveAsyncSiblingSource(
        MethodIdentity method,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        [NotNullWhen(true)] ref MethodIdentity? asyncSource)
    {
        asyncSource = _stateMachineSourceResolver.ResolveSourceMethod(
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
            _stateMachineSourceResolver.ResolveDeclaredSourceMethod(
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
        out AuthenticatedSourceOwner? ultimateOwner,
        bool exactAttribution = false)
    {
        immediateOwner = null;
        LiftedSourceOwnerResolution liftedResolution =
            exactAttribution
                ? _liftedSourceOwnerResolver.ResolveExact(
                    methodHandle,
                    methodDefinition,
                    method,
                    out AuthenticatedSourceOwner liftedOwner)
                : _liftedSourceOwnerResolver.Resolve(
                    methodHandle,
                    methodDefinition,
                    method,
                    out liftedOwner,
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
                out AuthenticatedSourceOwner resolvedOwner,
                exactAttribution);
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
            _stateMachineSourceResolver.ResolveSourceOwnership(
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
                _stateMachineSourceResolver
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
                    out AuthenticatedSourceOwner resolvedOwner,
                    exactAttribution);
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

    internal MethodDefinitionGeneratedExpansionResult
        ExpandGeneratedExecutionBodies(
            ImmutableArray<MethodDefinitionHandle> directMethods,
            MethodDefinitionGeneratedExpansionWork work,
            MethodDefinitionGeneratedBodyRoles roles =
                MethodDefinitionGeneratedBodyRoles.Execution,
            ImmutableArray<TypeDefinitionHandle> reachFromTypes = default,
            bool includeUnauthenticatedCandidates = false)
    {
        ArgumentNullException.ThrowIfNull(work);
        MethodDefinitionGeneratedBodyRoles helperRoles =
            roles
            & (MethodDefinitionGeneratedBodyRoles.UserHelper
                | MethodDefinitionGeneratedBodyRoles.Scaffolding);
        // Nested-type reach classifies every method of a reached
        // state-machine type, so it attributes helpers outside the requested
        // roles too.
        bool reachesNestedTypes = !reachFromTypes.IsDefaultOrEmpty;
        var attributed = new HashSet<int>();

        var selected = directMethods
            .Select(static handle =>
                MetadataTokens.GetToken(handle))
            .ToHashSet();
        var declaredOwnerByToken =
            directMethods.ToDictionary(
                static handle => MetadataTokens.GetToken(handle),
                static handle => handle);
        var pending = new Queue<MethodDefinitionHandle>(directMethods);
        var expanded =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
        expanded.AddRange(directMethods);

        MethodDefinitionGeneratedExpansionCoverage coverage;
        MethodDefinitionGeneratedExpansionBoundException? reached = null;
        try
        {
            while (pending.Count > 0)
            {
                MethodDefinitionHandle sourceHandle = pending.Dequeue();
                MethodDefinition sourceMethod =
                    _reader.GetMethodDefinition(sourceHandle);

                if (_stateMachineSourceResolver
                    .TryResolveTargetedStateMachineExecutionMethod(
                        sourceHandle,
                        sourceMethod,
                        work,
                        out MethodDefinitionHandle executionMethod,
                        out TypeDefinitionHandle stateMachineType))
                {
                    work.RecordCandidateDefinition(executionMethod);
                    work.RecordRelationshipNode(executionMethod);
                    MethodDefinitionHandle stateMachineOwner =
                        declaredOwnerByToken[
                            MetadataTokens.GetToken(sourceHandle)];
                    AddGenerated(
                        executionMethod,
                        stateMachineOwner,
                        MethodDefinitionGeneratedExpansionOriginKind
                            .StateMachineExecutionBody);
                    if (helperRoles != MethodDefinitionGeneratedBodyRoles.None
                        || reachesNestedTypes)
                    {
                        foreach ((MethodDefinitionHandle helper,
                            MethodDefinitionGeneratedBodyRoles role)
                            in _stateMachineSourceResolver.StateMachineHelperMethods(
                                stateMachineType,
                                executionMethod,
                                work))
                        {
                            if ((role & helperRoles) == 0)
                            {
                                attributed.Add(MetadataTokens.GetToken(helper));
                                work.RecordAttributedOutsideRoles(
                                    new(
                                        helper,
                                        stateMachineOwner,
                                        MethodDefinitionGeneratedExpansionOriginKind
                                            .StateMachineExecutionBody,
                                        role));
                                continue;
                            }
                            AddGenerated(
                                helper,
                                stateMachineOwner,
                                MethodDefinitionGeneratedExpansionOriginKind
                                    .StateMachineExecutionBody,
                                role);
                        }
                    }
                }

                foreach (MethodDefinitionHandle liftedHandle
                    in _liftedSourceOwnerResolver
                        .PotentialLiftedMethods(sourceHandle))
                {
                    MethodDefinition liftedMethod =
                        _reader.GetMethodDefinition(liftedHandle);
                    TypeDefinitionHandle liftedTypeHandle =
                        liftedMethod.GetDeclaringType();
                    TypeDefinition liftedType =
                        _reader.GetTypeDefinition(liftedTypeHandle);
                    MethodIdentity liftedIdentity =
                        _primaryMetadataResolver.CreateMethodIdentity(
                            liftedTypeHandle,
                            liftedHandle,
                            liftedMethod,
                            _primaryMetadataResolver.CreateScope(
                                liftedType,
                                liftedMethod));
                    if (!_liftedSourceOwnerResolver
                        .TryResolveForScopeExpansion(
                            liftedHandle,
                            liftedMethod,
                            liftedIdentity,
                            out AuthenticatedSourceOwner owner,
                            selected,
                            ownerTypeScope: null,
                            directlySelectedBody: false))
                    {
                        continue;
                    }

                    EntityHandle ownerEntity =
                        MetadataTokens.EntityHandle(
                            owner.Method.MetadataToken);
                    if (ownerEntity.Kind
                        != HandleKind.MethodDefinition)
                    {
                        throw new BadImageFormatException(
                            "A generated body resolved to a non-MethodDef owner.");
                    }
                    var ownerHandle =
                        (MethodDefinitionHandle)ownerEntity;
                    if (declaredOwnerByToken.TryGetValue(
                            owner.Method.MetadataToken,
                            out MethodDefinitionHandle declaredOwner))
                    {
                        ownerHandle = declaredOwner;
                    }
                    AddGenerated(
                        liftedHandle,
                        ownerHandle,
                        MethodDefinitionGeneratedExpansionOriginKind
                            .LiftedExecutionBody);
                }
            }

            // A generated-named lifted body declared directly in breadth that no
            // owner reaches is unauthenticated, not silently ownerless.
            foreach (MethodDefinitionHandle direct in directMethods)
            {
                if (!attributed.Contains(MetadataTokens.GetToken(direct))
                    && CompilerGeneratedNames.IsLocalFunctionOrLambda(
                        _reader.GetString(
                            _reader.GetMethodDefinition(direct).Name)))
                {
                    work.RecordUnauthenticatedCandidate(
                        direct,
                        joinsBreadth: false);
                }
            }
            if (reachesNestedTypes)
                ReachNestedGeneratedTypes();

            // A bound raised and caught along the way still bounds the
            // relation: outcomes after it may rest on a swallowed cut.
            reached = work.Reached;
            coverage = reached is null
                ? work.Build()
                : work.BuildBounded(reached);
        }
        catch (MethodDefinitionGeneratedExpansionBoundException bound)
        {
            // A reached bound keeps the retained prefix: the direct methods
            // and every outcome recorded within the limits. Nothing after it,
            // including the unauthenticated direct-body and nested-type
            // passes, is settled, so a unit outside the prefix's outcomes
            // stays Direct and the coverage says the relation is bounded.
            coverage = work.BuildBounded(bound);
            reached = bound;
        }
        work.Settle();

        ImmutableArray<MethodDefinitionHandle> methods =
        [
            .. expanded
                .Distinct()
                .OrderBy(static handle =>
                    MetadataTokens.GetRowNumber(handle)),
        ];
        return new(methods, coverage, reached);

        void AddGenerated(
            MethodDefinitionHandle method,
            MethodDefinitionHandle owner,
            MethodDefinitionGeneratedExpansionOriginKind kind,
            MethodDefinitionGeneratedBodyRoles role =
                MethodDefinitionGeneratedBodyRoles.Execution)
        {
            var origin =
                new MethodDefinitionGeneratedExpansionOrigin(
                    method,
                    owner,
                    kind,
                    role);
            work.RecordGeneratedMethod(origin);
            int token = MetadataTokens.GetToken(method);
            attributed.Add(token);
            if (!selected.Add(token))
                return;
            declaredOwnerByToken.Add(token, owner);
            expanded.Add(method);
            pending.Enqueue(method);
        }

        // Every method of a compiler-generated type reached from a selected
        // type through compiler-generated nested types is attributed above or
        // unauthenticated here. The chain stops at the first user nested
        // type, whose members own their own generated bodies.
        void ReachNestedGeneratedTypes()
        {
            var visited = new HashSet<TypeDefinitionHandle>(reachFromTypes);
            var parents = new Stack<TypeDefinitionHandle>(reachFromTypes);
            while (parents.Count > 0)
            {
                foreach (TypeDefinitionHandle nested
                    in _reader.GetTypeDefinition(parents.Pop())
                        .GetNestedTypes())
                {
                    work.RecordRelationshipNode(nested);
                    TypeDefinition nestedType =
                        _reader.GetTypeDefinition(nested);
                    if (!visited.Add(nested)
                        || !_primaryMetadataResolver
                            .HasCompilerGeneratedAttribute(
                                nestedType.GetCustomAttributes()))
                    {
                        continue;
                    }

                    parents.Push(nested);
                    foreach (MethodDefinitionHandle method
                        in nestedType.GetMethods())
                    {
                        work.RecordCandidateDefinition(method);
                        int token = MetadataTokens.GetToken(method);
                        if (attributed.Contains(token))
                            continue;
                        bool joinsBreadth =
                            includeUnauthenticatedCandidates
                            && selected.Add(token);
                        work.RecordUnauthenticatedCandidate(
                            method,
                            joinsBreadth);
                        if (joinsBreadth)
                            expanded.Add(method);
                    }
                }
            }
        }
    }

    internal bool TryResolveUltimateLiftedOwner(
        AuthenticatedSourceOwner source,
        out AuthenticatedSourceOwner ultimateOwner) =>
        ResolveUltimateLiftedOwner(
            source,
            out ultimateOwner)
            == DeclaredOwnerResolution.Resolved;

    DeclaredOwnerResolution ResolveUltimateLiftedOwner(
        AuthenticatedSourceOwner source,
        out AuthenticatedSourceOwner ultimateOwner,
        bool exactAttribution = false)
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
                exactAttribution
                    ? _liftedSourceOwnerResolver.ResolveExact(
                        (MethodDefinitionHandle)currentHandle,
                        currentDefinition,
                        current.Method,
                        out AuthenticatedSourceOwner sourceOwner)
                    : _liftedSourceOwnerResolver.Resolve(
                        (MethodDefinitionHandle)currentHandle,
                        currentDefinition,
                        current.Method,
                        out sourceOwner,
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

    internal bool IsMalformedGeneratedLiftedOwner(
        AuthenticatedSourceOwner source) =>
        _stateMachineSourceResolver
            .HasMalformedCompilerGeneratedLiftedName(source);

    internal AuthenticatedSourceOwner CreateAuthenticatedSourceOwner(
        MethodIdentity source)
    {
        AuthenticatedSourceOwner owner =
            _stateMachineSourceResolver
                .CreateAuthenticatedSourceOwner(source);
        return owner with
        {
            IsAuthenticatedTopLevelEntryPoint =
                _liftedSourceOwnerResolver
                    .IsAuthenticatedTopLevelEntryPoint(source),
        };
    }
}
