using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InertText;

namespace ILInspector.Analysis;

public sealed record ResourceEffectModelReceipt
{
    readonly ImmutableArray<ResourceDeclarationProvenance> _provenances;

    internal ResourceEffectModelReceipt(
        ResourceEffectModelIdentity identity,
        ResourceEffectLanguageIdentity language,
        string contentHash,
        ImmutableArray<ResourceDeclarationProvenance> provenances)
    {
        RequireContentHash(contentHash, nameof(contentHash));
        Identity = identity;
        Language = language;
        ContentHash = contentHash.ToLowerInvariant();
        _provenances =
            ImmutableArrayValueEquality.RequireInitialized(provenances, nameof(provenances));
    }

    public ResourceEffectModelIdentity Identity { get; }
    public ResourceEffectLanguageIdentity Language { get; }
    public string ContentHash { get; }
    public ImmutableArray<ResourceDeclarationProvenance> Provenances => _provenances;

    public bool Equals(ResourceEffectModelReceipt? other)
        => other is not null
            && Identity == other.Identity
            && Language == other.Language
            && ContentHash == other.ContentHash
            && _provenances.SequenceEqual(other._provenances);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identity);
        hash.Add(Language);
        hash.Add(ContentHash);
        ImmutableArrayValueEquality.AddToHash(ref hash, _provenances);
        return hash.ToHashCode();
    }

    internal static void RequireContentHash(string contentHash, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash, parameterName);
        if (contentHash.Length != 64
            || contentHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A content hash must be a SHA-256 hexadecimal digest.",
                parameterName);
        }
    }
}

public sealed record ResourceEffectAdmissionReceipt
{
    readonly ImmutableArray<ResourceEffectModelReceipt> _models;

    internal ResourceEffectAdmissionReceipt(
        string contentHash,
        ImmutableArray<ResourceEffectModelReceipt> models)
    {
        ResourceEffectModelReceipt.RequireContentHash(contentHash, nameof(contentHash));
        ContentHash = contentHash.ToLowerInvariant();
        _models = ImmutableArrayValueEquality.RequireInitialized(models, nameof(models));
    }

    public string ContentHash { get; }
    public ImmutableArray<ResourceEffectModelReceipt> Models => _models;

    public bool Equals(ResourceEffectAdmissionReceipt? other)
        => other is not null
            && ContentHash == other.ContentHash
            && _models.SequenceEqual(other._models);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContentHash);
        ImmutableArrayValueEquality.AddToHash(ref hash, _models);
        return hash.ToHashCode();
    }
}

public sealed class AdmittedResourceEffectModel
{
    readonly ImmutableArray<ResourceKindDefinition> _resourceKinds;
    readonly ImmutableArray<AdmittedResourceEffectDeclaration> _declarations;

    internal AdmittedResourceEffectModel(
        ResourceEffectModelIdentity identity,
        ResourceEffectLanguageIdentity language,
        ImmutableArray<ResourceKindDefinition> resourceKinds,
        ImmutableArray<AdmittedResourceEffectDeclaration> declarations,
        ResourceEffectModelReceipt receipt)
    {
        Identity = identity;
        Language = language;
        _resourceKinds =
            ImmutableArrayValueEquality.RequireInitialized(resourceKinds, nameof(resourceKinds));
        _declarations =
            ImmutableArrayValueEquality.RequireInitialized(declarations, nameof(declarations));
        Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
    }

    public ResourceEffectModelIdentity Identity { get; }
    public ResourceEffectLanguageIdentity Language { get; }
    public ImmutableArray<ResourceKindDefinition> ResourceKinds => _resourceKinds;
    public ImmutableArray<AdmittedResourceEffectDeclaration> Declarations => _declarations;
    public ResourceEffectModelReceipt Receipt { get; }
}

public sealed class ResourceEffectAdmission
{
    readonly ImmutableArray<AdmittedResourceEffectModel> _models;

    internal ResourceEffectAdmission(
        ImmutableArray<AdmittedResourceEffectModel> models,
        ResourceEffectAdmissionReceipt receipt)
    {
        _models = ImmutableArrayValueEquality.RequireInitialized(models, nameof(models));
        Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
    }

    public ImmutableArray<AdmittedResourceEffectModel> Models => _models;
    public ResourceEffectAdmissionReceipt Receipt { get; }
}

public sealed record ResourceEffectModelDiagnostic
{
    public ResourceEffectModelDiagnostic(
        ResourceEffectModelIdentity? model,
        ResourceDeclarationProvenance? provenance,
        ResourceEffectDiagnostic diagnostic)
    {
        Model = model;
        Provenance = provenance;
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public ResourceEffectModelIdentity? Model { get; }
    public ResourceDeclarationProvenance? Provenance { get; }
    public ResourceEffectDiagnostic Diagnostic { get; }
}

public abstract class ResourceEffectAdmissionOutcome
{
    private protected ResourceEffectAdmissionOutcome()
    {
    }

    public sealed class Admitted : ResourceEffectAdmissionOutcome
    {
        internal Admitted(ResourceEffectAdmission admission)
            => Admission = admission;

        public ResourceEffectAdmission Admission { get; }
    }

    public sealed class Rejected : ResourceEffectAdmissionOutcome
    {
        readonly ImmutableArray<ResourceEffectModelDiagnostic> _diagnostics;

        internal Rejected(ImmutableArray<ResourceEffectModelDiagnostic> diagnostics)
        {
            _diagnostics =
                ImmutableArrayValueEquality.RequireInitialized(diagnostics, nameof(diagnostics));
            if (_diagnostics.IsEmpty)
                throw new ArgumentException(
                    "A rejected admission requires diagnostics.",
                    nameof(diagnostics));
        }

        public ImmutableArray<ResourceEffectModelDiagnostic> Diagnostics => _diagnostics;
    }

    public sealed class WorkLimitExceeded : ResourceEffectAdmissionOutcome
    {
        internal WorkLimitExceeded(
            ResourceEffectWorkLimitKind limitKind,
            int limit,
            int required,
            ResourceEffectModelIdentity? model,
            ResourceDeclarationProvenance? provenance,
            int offset)
        {
            LimitKind = limitKind;
            Limit = limit;
            Required = required;
            Model = model;
            Provenance = provenance;
            Offset = offset;
        }

        public ResourceEffectWorkLimitKind LimitKind { get; }
        public int Limit { get; }
        public int Required { get; }
        public ResourceEffectModelIdentity? Model { get; }
        public ResourceDeclarationProvenance? Provenance { get; }
        public int Offset { get; }
    }
}

public static class ResourceEffectAdmissionBuilder
{
    public static ResourceEffectAdmissionOutcome Admit(
        IEnumerable<ResourceEffectModelDefinition> models,
        ResourceEffectWorkLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        limits ??= new ResourceEffectWorkLimits();
        var snapshot = new List<ResourceEffectModelDefinition>(
            Math.Min(limits.MaxModels, 16));
        foreach (ResourceEffectModelDefinition model in models)
        {
            if (model is null)
                throw new ArgumentException("Models must not contain null.", nameof(models));
            if (snapshot.Count == limits.MaxModels)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.Models,
                    limits.MaxModels,
                    snapshot.Count + 1);
            }
            snapshot.Add(model);
        }

        var admittedModels = new List<AdmittedResourceEffectModel>(snapshot.Count);
        int admissionStatementCount = 0;
        foreach (ResourceEffectModelDefinition model in snapshot)
        {
            if (model.Language != ResourceEffectLanguageIdentity.Version1)
            {
                return Reject(
                    model.Identity,
                    null,
                    ResourceEffectDiagnosticKind.UnknownLanguage,
                    0,
                    0,
                    "The model language is not the exact resource-effects/1 version.");
            }
            int declarationCount =
                model.Declarations.Length + model.TypedDeclarations.Length;
            if (declarationCount > limits.MaxDeclarationsPerModel)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.ModelDeclarations,
                    limits.MaxDeclarationsPerModel,
                    declarationCount,
                    model.Identity);
            }
            int statementCount =
                model.Declarations.Sum(declaration => declaration.Statements.Length)
                + model.TypedDeclarations.Length;
            if (statementCount > limits.MaxStatementsPerModel)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.ModelStatements,
                    limits.MaxStatementsPerModel,
                    statementCount,
                    model.Identity);
            }
            if (admissionStatementCount > limits.MaxAdmissionStatements - statementCount)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.AdmissionStatements,
                    limits.MaxAdmissionStatements,
                    admissionStatementCount + statementCount,
                    model.Identity);
            }
            admissionStatementCount += statementCount;

            ResourceEffectAdmissionOutcome? failure =
                TryAdmitModel(model, limits, out AdmittedResourceEffectModel? admittedModel);
            if (failure is not null)
                return failure;
            admittedModels.Add(admittedModel!);
        }

        ImmutableArray<AdmittedResourceEffectModel> admitted = [.. admittedModels];
        ImmutableArray<ResourceEffectModelReceipt> receipts =
            [.. admitted.Select(model => model.Receipt)];
        var receipt = new ResourceEffectAdmissionReceipt(
            Hash(ResourceEffectCanonicalizer.AdmissionContent(receipts)),
            receipts);
        return new ResourceEffectAdmissionOutcome.Admitted(
            new ResourceEffectAdmission(admitted, receipt));
    }

    static ResourceEffectAdmissionOutcome? TryAdmitModel(
        ResourceEffectModelDefinition model,
        ResourceEffectWorkLimits limits,
        out AdmittedResourceEffectModel? admitted)
    {
        admitted = null;
        int provenanceCount = 0;
        foreach (ResourceKindDefinition definition in model.ResourceKinds)
        {
            ResourceEffectAdmissionOutcome? provenanceFailure =
                ChargeProvenances(definition.Provenances);
            if (provenanceFailure is not null)
                return provenanceFailure;
        }
        foreach (ResourceEffectTargetDeclaration declaration in model.Declarations)
        {
            ResourceEffectAdmissionOutcome? provenanceFailure =
                ChargeProvenances(
                    declaration.Statements.Select(statement => statement.Provenance));
            if (provenanceFailure is not null)
                return provenanceFailure;
        }
        foreach (ResourceEffectTypedDeclaration declaration in model.TypedDeclarations)
        {
            ResourceEffectAdmissionOutcome? provenanceFailure =
                ChargeProvenances(declaration.Provenances);
            if (provenanceFailure is not null)
                return provenanceFailure;
        }

        var definitions = new Dictionary<ResourceKindIdentity, ResourceKindDefinition>();
        foreach (ResourceKindDefinition definition in model.ResourceKinds)
        {
            foreach (ResourceDeclarationProvenance provenance in definition.Provenances)
            {
                if (provenance.Model != model.Identity)
                {
                    return Reject(
                        model.Identity,
                        provenance,
                        ResourceEffectDiagnosticKind.InvalidTarget,
                        0,
                        0,
                        "Resource-kind provenance must identify its containing atomic model.");
                }
            }
            if (definitions.TryGetValue(definition.Identity, out ResourceKindDefinition? existing))
            {
                if (existing.Arity != definition.Arity)
                {
                    return RejectAll(
                        existing.Provenances.Concat(definition.Provenances),
                        ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                        "One model declares incompatible arities for the same resource kind.");
                }
                definitions[definition.Identity] = Merge(existing, definition);
                if (definitions.Count > limits.MaxResourceKindsPerModel)
                    return ResourceKindLimit(model, limits, definitions.Count);
                continue;
            }
            definitions.Add(definition.Identity, definition);
            if (definitions.Count > limits.MaxResourceKindsPerModel)
                return ResourceKindLimit(model, limits, definitions.Count);
        }

        var declarations = new List<ParsedDeclaration>();
        foreach (ResourceEffectTargetDeclaration targetDeclaration in model.Declarations)
        {
            foreach (ResourceEffectSourceStatement source in targetDeclaration.Statements)
            {
                if (source.Provenance.Model != model.Identity)
                {
                    return Reject(
                        model.Identity,
                        source.Provenance,
                        ResourceEffectDiagnosticKind.InvalidTarget,
                        0,
                        source.Text.Length,
                        "Declaration provenance must identify its containing atomic model.");
                }
            }

            ResourceDeclarationProvenance? targetProvenance =
                targetDeclaration.Statements.IsEmpty
                    ? null
                    : targetDeclaration.Statements[0].Provenance;
            ResourceEffectAdmissionOutcome? targetBudgetFailure =
                ValidateStructuralBudget(
                    model.Identity,
                    targetProvenance,
                    targetDeclaration.Target,
                    null,
                    limits);
            if (targetBudgetFailure is not null)
                return targetBudgetFailure;
            ResourceEffectAdmissionOutcome? targetFailure =
                ValidateTarget(
                    targetDeclaration.Target,
                    model.Identity,
                    targetDeclaration.Statements.Select(source => source.Provenance));
            if (targetFailure is not null)
                return targetFailure;

            foreach (ResourceEffectSourceStatement source in targetDeclaration.Statements)
            {
                ResourceEffectParseOutcome parse =
                    ResourceEffectStatementParser.Parse(source.Text, limits);
                switch (parse)
                {
                    case ResourceEffectParseOutcome.Rejected rejected:
                        return new ResourceEffectAdmissionOutcome.Rejected(
                            [new ResourceEffectModelDiagnostic(
                                model.Identity,
                                source.Provenance,
                                rejected.Diagnostic)]);
                    case ResourceEffectParseOutcome.WorkLimitExceeded exceeded:
                        return Limit(
                            exceeded.LimitKind,
                            exceeded.Limit,
                            exceeded.Required,
                            model.Identity,
                            source.Provenance,
                            exceeded.Offset);
                    case ResourceEffectParseOutcome.Parsed completed:
                    {
                        ResourceEffect effect = completed.Effect;
                        ResourceEffectAdmissionOutcome? budgetFailure =
                            ValidateStructuralBudget(
                                model.Identity,
                                source.Provenance,
                                targetDeclaration.Target,
                                effect,
                                limits);
                        if (budgetFailure is not null)
                            return budgetFailure;
                        ResourceEffectAdmissionOutcome? validationFailure =
                            ValidateDeclaration(
                                model.Identity,
                                targetDeclaration.Target,
                                source.Provenance,
                                source.Text.Length,
                                isTyped: false,
                                ref effect);
                        if (validationFailure is not null)
                            return validationFailure;
                        if (effect is ResourceEffect.Resource resource)
                        {
                            int arity = resource.Kind.Arguments.Length;
                            if (definitions.TryGetValue(
                                    resource.Kind.Identity,
                                    out ResourceKindDefinition? existing)
                                && existing.Arity != arity)
                            {
                                return RejectAll(
                                    existing.Provenances.Append(source.Provenance),
                                    ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                                    "The resource statement disagrees with the declared resource-kind arity.");
                            }
                            var declared = new ResourceKindDefinition(
                                resource.Kind.Identity,
                                arity,
                                [source.Provenance]);
                            definitions[resource.Kind.Identity] =
                                existing is null
                                    ? declared
                                    : Merge(existing, declared);
                            if (definitions.Count > limits.MaxResourceKindsPerModel)
                                return ResourceKindLimit(model, limits, definitions.Count);
                        }
                        declarations.Add(
                            new ParsedDeclaration(
                                targetDeclaration.Target,
                                effect,
                                source.Provenance,
                                source.Text.Length));
                        break;
                    }
                }
            }
        }

        foreach (ResourceEffectTypedDeclaration declaration
                 in model.TypedDeclarations)
        {
            foreach (ResourceDeclarationProvenance provenance in declaration.Provenances)
            {
                if (provenance.Model != model.Identity)
                {
                    return Reject(
                        model.Identity,
                        provenance,
                        ResourceEffectDiagnosticKind.InvalidTarget,
                        0,
                        0,
                        "Typed declaration provenance must identify its containing atomic model.");
                }
            }

            ResourceEffect effect = declaration.Effect;
            ResourceEffectAdmissionOutcome? budgetFailure =
                ValidateStructuralBudget(
                    model.Identity,
                    declaration.Provenances[0],
                    declaration.Target,
                    effect,
                    limits);
            if (budgetFailure is not null)
                return budgetFailure;
            ResourceEffectAdmissionOutcome? targetFailure =
                ValidateTarget(
                    declaration.Target,
                    model.Identity,
                    declaration.Provenances);
            if (targetFailure is not null)
                return targetFailure;
            ResourceEffectAdmissionOutcome? validationFailure =
                ValidateDeclaration(
                    model.Identity,
                    declaration.Target,
                    declaration.Provenances[0],
                    0,
                    isTyped: true,
                    ref effect);
            if (validationFailure is not null)
                return validationFailure;
            if (effect is ResourceEffect.Resource resource)
            {
                int arity = resource.Kind.Arguments.Length;
                if (definitions.TryGetValue(
                        resource.Kind.Identity,
                        out ResourceKindDefinition? existing)
                    && existing.Arity != arity)
                {
                    return RejectAll(
                        existing.Provenances.Concat(declaration.Provenances),
                        ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                        "The typed resource declaration disagrees with the declared resource-kind arity.");
                }
                var declared = new ResourceKindDefinition(
                    resource.Kind.Identity,
                    arity,
                    declaration.Provenances);
                definitions[resource.Kind.Identity] =
                    existing is null ? declared : Merge(existing, declared);
                if (definitions.Count > limits.MaxResourceKindsPerModel)
                    return ResourceKindLimit(model, limits, definitions.Count);
            }
            foreach (ResourceDeclarationProvenance provenance in declaration.Provenances)
            {
                declarations.Add(
                    new ParsedDeclaration(
                        declaration.Target,
                        effect,
                        provenance,
                        0));
            }
        }

        ResourceEffectAdmissionOutcome? localFailure =
            ValidateModelLocalReferences(model.Identity, declarations, limits);
        if (localFailure is not null)
            return localFailure;

        ResourceEffectAdmissionOutcome? resourceKindFailure =
            ValidateResourceKindArities(model, limits, definitions, declarations);
        if (resourceKindFailure is not null)
            return resourceKindFailure;

        ImmutableArray<AdmittedResourceEffectDeclaration> admittedDeclarations =
            [.. declarations
                .GroupBy(
                    declaration => ResourceEffectCanonicalizer.Declaration(
                        declaration.Target,
                        declaration.Effect),
                    StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    ParsedDeclaration declaration = group.First();
                    return new AdmittedResourceEffectDeclaration(
                        declaration.Target,
                        declaration.Effect,
                        [.. group
                            .Select(item => item.Provenance)
                            .Distinct()
                            .OrderBy(
                                ResourceEffectCanonicalizer.Provenance,
                                StringComparer.Ordinal)]);
                })];
        ImmutableArray<ResourceKindDefinition> orderedDefinitions =
            [.. definitions.Values
                .OrderBy(definition => definition.Identity.Value, StringComparer.Ordinal)
                .ThenBy(definition => definition.Arity)];
        ImmutableArray<ResourceDeclarationProvenance> provenances =
            [.. definitions.Values.SelectMany(definition => definition.Provenances)
                .Concat(admittedDeclarations.SelectMany(declaration => declaration.Provenances))
                .Distinct()
                .OrderBy(ResourceEffectCanonicalizer.Provenance, StringComparer.Ordinal)];
        string content = ResourceEffectCanonicalizer.ModelContent(
            orderedDefinitions,
            admittedDeclarations);
        var receipt = new ResourceEffectModelReceipt(
            model.Identity,
            model.Language,
            Hash(content),
            provenances);
        admitted = new AdmittedResourceEffectModel(
            model.Identity,
            model.Language,
            orderedDefinitions,
            admittedDeclarations,
            receipt);
        return null;

        ResourceEffectAdmissionOutcome? ChargeProvenances(
            IEnumerable<ResourceDeclarationProvenance> provenances)
        {
            foreach (ResourceDeclarationProvenance provenance in provenances)
            {
                provenanceCount++;
                if (provenanceCount > limits.MaxProvenancesPerModel)
                {
                    return Limit(
                        ResourceEffectWorkLimitKind.ModelProvenances,
                        limits.MaxProvenancesPerModel,
                        provenanceCount,
                        model.Identity,
                        provenance);
                }
            }
            return null;
        }
    }

    static ResourceEffectAdmissionOutcome ResourceKindLimit(
        ResourceEffectModelDefinition model,
        ResourceEffectWorkLimits limits,
        int required)
        => Limit(
            ResourceEffectWorkLimitKind.ModelResourceKinds,
            limits.MaxResourceKindsPerModel,
            required,
            model.Identity);

    static ResourceEffectAdmissionOutcome? ValidateResourceKindArities(
        ResourceEffectModelDefinition model,
        ResourceEffectWorkLimits limits,
        IReadOnlyDictionary<ResourceKindIdentity, ResourceKindDefinition> definitions,
        IEnumerable<ParsedDeclaration> declarations)
    {
        var arities = new Dictionary<ResourceKindIdentity, int>();
        var provenances =
            new Dictionary<ResourceKindIdentity, List<ResourceDeclarationProvenance>>();
        foreach (ResourceKindDefinition definition in definitions.Values)
        {
            arities.Add(definition.Identity, definition.Arity);
            provenances.Add(definition.Identity, [.. definition.Provenances]);
        }

        foreach (ParsedDeclaration declaration in declarations)
        {
            foreach (ResourceKindReference kind in Kinds(declaration.Effect))
            {
                int arity = kind.Arguments.Length;
                if (arities.TryGetValue(kind.Identity, out int existingArity))
                {
                    if (existingArity != arity)
                    {
                        return RejectAll(
                            provenances[kind.Identity].Append(declaration.Provenance),
                            ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                            "One model uses incompatible arities for the same resource kind.");
                    }
                    provenances[kind.Identity].Add(declaration.Provenance);
                    continue;
                }

                arities.Add(kind.Identity, arity);
                provenances.Add(kind.Identity, [declaration.Provenance]);
                if (arities.Count > limits.MaxResourceKindsPerModel)
                    return ResourceKindLimit(model, limits, arities.Count);
            }
        }

        return null;
    }

    static ResourceEffectAdmissionOutcome? ValidateStructuralBudget(
        ResourceEffectModelIdentity model,
        ResourceDeclarationProvenance? provenance,
        ResourceEffectTargetSelector? target,
        ResourceEffect? effect,
        ResourceEffectWorkLimits limits)
    {
        var pending = new Stack<(object Node, int Depth)>();
        int nodes = 0;
        ResourceEffectAdmissionOutcome? failure = Push(target, 1) ?? Push(effect, 1);
        if (failure is not null)
            return failure;
        while (pending.TryPop(out (object Node, int Depth) item))
        {
            foreach (object child in StructuralChildren(item.Node))
            {
                failure = Push(child, item.Depth + 1);
                if (failure is not null)
                    return failure;
            }
        }
        return null;

        ResourceEffectAdmissionOutcome? Push(object? node, int depth)
        {
            if (node is null)
                return null;
            if (depth > limits.MaxNestingDepth)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.NestingDepth,
                    limits.MaxNestingDepth,
                    depth,
                    model,
                    provenance);
            }
            nodes++;
            if (nodes > limits.MaxStructuralNodesPerDeclaration)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.StructuralNodes,
                    limits.MaxStructuralNodesPerDeclaration,
                    nodes,
                    model,
                    provenance);
            }
            pending.Push((node, depth));
            return null;
        }
    }

    static IEnumerable<object> StructuralChildren(object node)
    {
        switch (node)
        {
            case ResourceEffectTargetSelector.Type type:
                yield return type.Selector;
                break;
            case ResourceEffectTargetSelector.Member member:
                yield return member.Selector;
                break;
            case ResourceEffectMemberSelector member:
                yield return member.DeclaringType;
                yield return member.ReturnType;
                foreach (ResourceEffectParameterSelector parameter in member.Parameters)
                    yield return parameter;
                break;
            case ResourceEffectParameterSelector parameter:
                yield return parameter.Type;
                break;
            case ResourceTypeExpression.Named named:
                foreach (ResourceTypeNameSegment segment in named.Segments)
                    yield return segment;
                foreach (ResourceTypeExpression argument in named.Arguments)
                    yield return argument;
                break;
            case ResourceTypeExpression.SzArray array:
                yield return array.Element;
                break;
            case ResourceTypeExpression.Array array:
                yield return array.Element;
                break;
            case ResourceTypeExpression.ByReference reference:
                yield return reference.Element;
                break;
            case ResourceTypeExpression.Pointer pointer:
                yield return pointer.Element;
                break;
            case ResourceEffect.Resource resource:
                yield return resource.Kind;
                break;
            case ResourceEffect.Authority authority:
                yield return authority.Kind;
                yield return authority.Target;
                yield return authority.Key;
                break;
            case ResourceEffect.Acquire acquire:
                yield return acquire.Kind;
                yield return acquire.Target;
                yield return acquire.When;
                if (acquire.Correspondence is not null)
                    yield return acquire.Correspondence;
                if (acquire.Lender is not null)
                    yield return acquire.Lender;
                break;
            case ResourceEffect.Move move:
                yield return move.Source;
                yield return move.Target;
                yield return move.When;
                if (move.Kind is not null)
                    yield return move.Kind;
                break;
            case ResourceEffect.Consume consume:
                yield return consume.Source;
                yield return consume.Target;
                if (consume.Kind is not null)
                    yield return consume.Kind;
                break;
            case ResourceEffect.Release release:
                yield return release.Source;
                yield return release.When;
                if (release.Kind is not null)
                    yield return release.Kind;
                if (release.Correspondence is not null)
                    yield return release.Correspondence;
                if (release.Observation is not null)
                    yield return release.Observation;
                break;
            case ResourceEffect.Borrow borrow:
                yield return borrow.Source;
                yield return borrow.Target;
                yield return borrow.Scope;
                if (borrow.Kind is not null)
                    yield return borrow.Kind;
                if (borrow.Lender is not null)
                    yield return borrow.Lender;
                break;
            case ResourceEffect.Derive derive:
                yield return derive.Source;
                yield return derive.Target;
                if (derive.Guard is not null)
                    yield return derive.Guard;
                break;
            case ResourceEffect.Pass pass:
                yield return pass.Source;
                yield return pass.Target;
                break;
            case ResourceEffect.Independent independent:
                yield return independent.Source;
                yield return independent.Target;
                break;
            case ResourceEffect.Callback callback:
                yield return callback.Delegate;
                yield return callback.Scope;
                break;
            case ResourceEffect.Accept accept:
                yield return accept.Source;
                yield return accept.Target;
                yield return accept.When;
                if (accept.Kind is not null)
                    yield return accept.Kind;
                break;
            case ResourceEffect.Operation operation when operation.Guard is not null:
                yield return operation.Guard;
                break;
            case ResourceEffect.Outcome outcome:
                yield return outcome.Source;
                yield return outcome.Test;
                break;
            case ResourceEffectLocation.OperationSlot operation:
                yield return operation.Source;
                if (operation.Kind is not null)
                    yield return operation.Kind;
                break;
            case ResourceEffectLocation.Field field:
                yield return field.Root;
                break;
            case ResourceEffectLocation.StructuralField field:
                yield return field.Root;
                yield return field.Selector;
                break;
            case ResourceEffectCompletion.OutcomeCase outcome:
                yield return outcome.Source;
                yield return outcome.Test;
                break;
            case ResourceEffectGuard.ExactRuntimeType guard:
                yield return guard.Subject;
                yield return guard.Expected;
                break;
            case ResourceKindReference kind:
                foreach (ResourceEffectGenericVariable argument in kind.Arguments)
                    yield return argument;
                break;
            case ResourceAuthorityKey.Singleton singleton:
                foreach (ResourceEffectGenericVariable argument in singleton.Arguments)
                    yield return argument;
                break;
        }
    }

    static ResourceEffectAdmissionOutcome? ValidateTarget(
        ResourceEffectTargetSelector target,
        ResourceEffectModelIdentity model,
        IEnumerable<ResourceDeclarationProvenance> provenances)
    {
        bool valid = target switch
        {
            ResourceEffectTargetSelector.Type type =>
                ValidateTypeVariables(type.Selector, type.Selector.Segments.Sum(segment => segment.GenericArity), 0),
            ResourceEffectTargetSelector.Member member =>
                ValidateMemberVariables(member.Selector),
            _ => false,
        };
        if (valid)
            return null;
        ImmutableArray<ResourceDeclarationProvenance> provenanceSnapshot =
            [.. provenances.Distinct()];
        return provenanceSnapshot.IsEmpty
            ? Reject(
                model,
                null,
                ResourceEffectDiagnosticKind.UnboundGenericVariable,
                0,
                0,
                "A structural selector contains an unbound generic variable.")
            : RejectAll(
                provenanceSnapshot,
                ResourceEffectDiagnosticKind.UnboundGenericVariable,
                "A structural selector contains an unbound generic variable.");
    }

    static bool ValidateMemberVariables(ResourceEffectMemberSelector member)
    {
        int typeArity = member.DeclaringType.Segments.Sum(segment => segment.GenericArity);
        if (!ValidateTypeVariables(member.DeclaringType, typeArity, 0))
            return false;
        HashSet<ResourceEffectGenericVariable> boundTypeVariables =
            [.. TypeVariables(member.DeclaringType).Where(variable =>
                variable.Kind == ResourceEffectGenericVariableKind.Type)];
        foreach (ResourceEffectParameterSelector parameter in member.Parameters)
        {
            if (!ValidateTypeVariables(parameter.Type, typeArity, member.GenericArity)
                || !TypeVariables(parameter.Type)
                    .Where(variable =>
                        variable.Kind == ResourceEffectGenericVariableKind.Type)
                    .All(boundTypeVariables.Contains))
            {
                return false;
            }
        }
        return ValidateTypeVariables(member.ReturnType, typeArity, member.GenericArity)
            && TypeVariables(member.ReturnType)
                .Where(variable =>
                    variable.Kind == ResourceEffectGenericVariableKind.Type)
                .All(boundTypeVariables.Contains);
    }

    static bool ValidateStructuralFieldVariables(
        ResourceEffectMemberSelector field,
        int typeArity,
        int methodArity,
        HashSet<ResourceEffectGenericVariable> boundTypeVariables)
    {
        if (field.Kind != ResourceEffectMemberKind.Field
            || !ValidateTypeVariables(field.DeclaringType, typeArity, methodArity)
            || !ValidateTypeVariables(field.ReturnType, typeArity, methodArity))
        {
            return false;
        }
        return TypeVariables(field.DeclaringType)
            .Concat(TypeVariables(field.ReturnType))
            .Where(variable =>
                variable.Kind == ResourceEffectGenericVariableKind.Type)
            .All(boundTypeVariables.Contains);
    }

    static IEnumerable<ResourceEffectGenericVariable> TypeVariables(
        ResourceTypeExpression expression)
    {
        switch (expression)
        {
            case ResourceTypeExpression.Variable variable:
                yield return variable.Value;
                break;
            case ResourceTypeExpression.Named named:
                foreach (ResourceTypeExpression argument in named.Arguments)
                {
                    foreach (ResourceEffectGenericVariable variable
                             in TypeVariables(argument))
                    {
                        yield return variable;
                    }
                }
                break;
            case ResourceTypeExpression.SzArray array:
                foreach (ResourceEffectGenericVariable variable in TypeVariables(array.Element))
                    yield return variable;
                break;
            case ResourceTypeExpression.Array array:
                foreach (ResourceEffectGenericVariable variable in TypeVariables(array.Element))
                    yield return variable;
                break;
            case ResourceTypeExpression.ByReference reference:
                foreach (ResourceEffectGenericVariable variable in TypeVariables(reference.Element))
                    yield return variable;
                break;
            case ResourceTypeExpression.Pointer pointer:
                foreach (ResourceEffectGenericVariable variable in TypeVariables(pointer.Element))
                    yield return variable;
                break;
        }
    }

    static bool ValidateTypeVariables(
        ResourceTypeExpression expression,
        int typeArity,
        int methodArity)
        => expression switch
        {
            ResourceTypeExpression.Variable variable =>
                variable.Value.Kind == ResourceEffectGenericVariableKind.Type
                    ? variable.Value.Index < typeArity
                    : variable.Value.Index < methodArity,
            ResourceTypeExpression.Named named =>
                named.Arguments.All(argument =>
                    ValidateTypeVariables(argument, typeArity, methodArity)),
            ResourceTypeExpression.SzArray array =>
                ValidateTypeVariables(array.Element, typeArity, methodArity),
            ResourceTypeExpression.Array array =>
                ValidateTypeVariables(array.Element, typeArity, methodArity),
            ResourceTypeExpression.ByReference reference =>
                ValidateTypeVariables(reference.Element, typeArity, methodArity),
            ResourceTypeExpression.Pointer pointer =>
                ValidateTypeVariables(pointer.Element, typeArity, methodArity),
            _ => false,
        };

    static ResourceEffectAdmissionOutcome? ValidateDeclaration(
        ResourceEffectModelIdentity model,
        ResourceEffectTargetSelector target,
        ResourceDeclarationProvenance provenance,
        int sourceLength,
        bool isTyped,
        ref ResourceEffect effect)
    {
        if (!HasValidFiniteShape(effect))
        {
            return Invalid(
                ResourceEffectDiagnosticKind.InvalidTerm,
                "A typed effect contains a value or relationship outside resource-effects/1.");
        }
        if (effect is ResourceEffect.Resource resource)
        {
            switch (target)
            {
                case ResourceEffectTargetSelector.Type:
                    if (resource.Value == ResourceDeclaredValueKind.DeclaredField
                        || resource.Selector is not null)
                    {
                        return Invalid(
                            ResourceEffectDiagnosticKind.InvalidTarget,
                            "A type resource declaration cannot bind a field selector.");
                    }
                    break;
                case ResourceEffectTargetSelector.Member
                    {
                        Selector.Kind: ResourceEffectMemberKind.Field,
                    }:
                    if (resource.Value != ResourceDeclaredValueKind.DeclaredField
                        || (!isTyped && resource.Selector is null))
                    {
                        return Invalid(
                            ResourceEffectDiagnosticKind.InvalidTarget,
                            "A field resource declaration requires value=declared-field; parsed statements also require a model-local selector.");
                    }
                    break;
                default:
                    return Invalid(
                        ResourceEffectDiagnosticKind.InvalidTarget,
                        "A resource statement may target only a type or field.");
            }
        }
        else if (effect is ResourceEffect.Consume consume
                 && (isTyped
                     ? consume.Target is not ResourceEffectLocation.Operation
                         and not ResourceEffectLocation.OperationSlot
                     : consume.Target is not ResourceEffectLocation.Operation))
        {
            return Invalid(
                ResourceEffectDiagnosticKind.InvalidTarget,
                isTyped
                    ? "A typed consume target must be operation[N] or a canonical operation slot."
                    : "A parsed consume target must be operation[N].");
        }
        else if (isTyped
                 && effect is ResourceEffect.Consume
                 {
                     Target: ResourceEffectLocation.OperationSlot operation,
                 } typedConsume
                 && (operation.Source != typedConsume.Source
                     || operation.Kind != typedConsume.Kind))
        {
            return Invalid(
                ResourceEffectDiagnosticKind.InvalidTarget,
                "A canonical consume target must identify its exact source and resource kind.");
        }
        else if (target is not ResourceEffectTargetSelector.Member
                 {
                     Selector.Kind: not ResourceEffectMemberKind.Field,
                 })
        {
            return Invalid(
                ResourceEffectDiagnosticKind.InvalidTarget,
                "An operation effect must target a constructor, method, or accessor.");
        }

        (int typeArity, int methodArity) = TargetArities(target);
        HashSet<ResourceEffectGenericVariable> boundTypeVariables =
            BoundTypeVariables(target);
        foreach (ResourceEffectGenericVariable variable in Variables(effect))
        {
            int arity = variable.Kind == ResourceEffectGenericVariableKind.Type
                ? typeArity
                : methodArity;
            if (variable.Index >= arity)
            {
                return Invalid(
                    ResourceEffectDiagnosticKind.UnboundGenericVariable,
                    "An effect references a generic variable not bound by its target selector.");
            }
            if (variable.Kind == ResourceEffectGenericVariableKind.Type
                && !boundTypeVariables.Contains(variable))
            {
                return Invalid(
                    ResourceEffectDiagnosticKind.InconsistentGenericVariable,
                    "A declaring-type variable is not preserved by the target's structural type binding.");
            }
        }
        if (!StructuralFieldsAreValid(
                    target,
                    effect,
                    typeArity,
                    methodArity,
                    boundTypeVariables))
        {
            return Invalid(
                    ResourceEffectDiagnosticKind.UnboundGenericVariable,
                    "A structural field selector contains a generic variable not bound by the outer operation.");
        }

        if (target is ResourceEffectTargetSelector.Member member)
        {
            foreach (ResourceEffectLocation location in Locations(effect))
            {
                if (!ValidateLocationShape(member.Selector, location))
                {
                    return Invalid(
                        ResourceEffectDiagnosticKind.InvalidLocation,
                        "An effect location is incompatible with its structural member selector.");
                }
            }
            foreach (ResourceEffectSignatureLocation signatureLocation in SignatureLocations(effect))
            {
                if (!ValidateSignatureLocation(member.Selector, signatureLocation))
                {
                    return Invalid(
                        ResourceEffectDiagnosticKind.InvalidGuard,
                        "A guard signature location is incompatible with its structural member selector.");
                }
            }
        }

        if (effect is ResourceEffect.Callback callback
            && callback.Delegate.Index != callback.Scope.Index)
        {
            return Invalid(
                ResourceEffectDiagnosticKind.InconsistentGenericVariable,
                "Version 1 callback scope identity must equal its delegate parameter position.");
        }
        if (effect is ResourceEffect.Borrow borrow)
        {
            int? targetCallback = CallbackIndex(borrow.Target);
            if (borrow.Scope is ResourceBorrowScope.Callback callbackScope
                && targetCallback != callbackScope.Index)
            {
                return Invalid(
                    ResourceEffectDiagnosticKind.UnresolvedCallback,
                    "A callback borrow target must belong to its declared callback scope.");
            }
            if (borrow.Scope is ResourceBorrowScope.Call
                && targetCallback is not null)
            {
                return Invalid(
                    ResourceEffectDiagnosticKind.InvalidLocation,
                    "A call-scoped borrow cannot target a callback location.");
            }
        }

        return null;

        ResourceEffectAdmissionOutcome Invalid(
            ResourceEffectDiagnosticKind kind,
            string message)
            => Reject(
                model,
                provenance,
                kind,
                0,
                sourceLength,
                message);
    }

    static bool StructuralFieldsAreValid(
        ResourceEffectTargetSelector target,
        ResourceEffect effect)
    {
        (int typeArity, int methodArity) = TargetArities(target);
        return StructuralFieldsAreValid(
            target,
            effect,
            typeArity,
            methodArity,
            BoundTypeVariables(target));
    }

    static bool StructuralFieldsAreValid(
        ResourceEffectTargetSelector target,
        ResourceEffect effect,
        int typeArity,
        int methodArity,
        HashSet<ResourceEffectGenericVariable> boundTypeVariables)
    {
        if (target is not ResourceEffectTargetSelector.Member)
            return !Locations(effect)
                .SelectMany(StructuralFields)
                .Any();
        return Locations(effect)
            .SelectMany(StructuralFields)
            .All(field => ValidateStructuralFieldVariables(
                field.Selector,
                typeArity,
                methodArity,
                boundTypeVariables));
    }

    static IEnumerable<ResourceEffectLocation.StructuralField> StructuralFields(
        ResourceEffectLocation location)
    {
        switch (location)
        {
            case ResourceEffectLocation.Field field:
                foreach (ResourceEffectLocation.StructuralField nested
                         in StructuralFields(field.Root))
                {
                    yield return nested;
                }
                break;
            case ResourceEffectLocation.StructuralField field:
                yield return field;
                foreach (ResourceEffectLocation.StructuralField nested
                         in StructuralFields(field.Root))
                {
                    yield return nested;
                }
                break;
            case ResourceEffectLocation.OperationSlot operation:
                foreach (ResourceEffectLocation.StructuralField nested
                         in StructuralFields(operation.Source))
                {
                    yield return nested;
                }
                break;
        }
    }

    static bool HasValidFiniteShape(ResourceEffect effect)
        => effect switch
        {
            ResourceEffect.Resource resource =>
                (resource.Value is null || Enum.IsDefined(resource.Value.Value))
                && (resource.Selector is null || resource.Selector.Value != default),
            ResourceEffect.Release release =>
                release.When is ResourceEffectCompletion.SuccessfulAwait
                    ? release.Observation is ResourceEffectLocation.Return
                    : release.Observation is null,
            ResourceEffect.Borrow borrow =>
                Enum.IsDefined(borrow.Access)
                && (borrow.Materialization is null
                    || Enum.IsDefined(borrow.Materialization.Value)),
            ResourceEffect.Derive derive =>
                Enum.IsDefined(derive.Relation),
            ResourceEffect.Pass pass =>
                pass.Identity is null || Enum.IsDefined(pass.Identity.Value),
            ResourceEffect.Callback callback =>
                Enum.IsDefined(callback.Execution)
                && Enum.IsDefined(callback.Cardinality),
            ResourceEffect.Operation operation =>
                Enum.IsDefined(operation.Boundary)
                && Enum.IsDefined(operation.Throws),
            ResourceEffect.Accept accept =>
                accept.Order is null || accept.Order.Value != default,
            ResourceEffect.Outcome outcome =>
                outcome.Identity != default,
            _ => true,
        };

    static ResourceEffectAdmissionOutcome? ValidateModelLocalReferences(
        ResourceEffectModelIdentity model,
        List<ParsedDeclaration> declarations,
        ResourceEffectWorkLimits limits)
    {
        var fields = new Dictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector>();
        foreach (ParsedDeclaration declaration in declarations)
        {
            if (declaration.Effect is not ResourceEffect.Resource
                {
                    Value: ResourceDeclaredValueKind.DeclaredField,
                    Selector: { } selector,
                })
            {
                continue;
            }
            var member =
                ((ResourceEffectTargetSelector.Member)declaration.Target).Selector;
            if (fields.TryGetValue(selector, out ResourceEffectMemberSelector? existing)
                && existing != member)
            {
                return Failure(
                    declaration,
                    ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
                    "One model-local field selector identifies different fields.");
            }
            fields[selector] = member;
        }

        var outcomeDeclarations =
            new Dictionary<
                ScopedLocal,
                List<(ParsedDeclaration Declaration, ResourceEffect.Outcome Outcome)>>();
        var callbacks = new Dictionary<ScopedIndex, ResourceEffect.Callback>();
        var operationDeclarations =
            new Dictionary<ScopedIndex, List<ParsedDeclaration>>();
        foreach (ParsedDeclaration declaration in declarations)
        {
            string target = ResourceEffectCanonicalizer.Target(declaration.Target);
            if (declaration.Effect is ResourceEffect.Outcome outcome)
            {
                if (!AllLocalFieldsDefined(outcome.Source, fields))
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.UnresolvedField,
                        "An outcome subject references no field declaration in the atomic model.");
                }
                var fieldResolvedOutcome = new ResourceEffect.Outcome(
                    outcome.Identity,
                    ResolveLocation(outcome.Source, fields),
                    outcome.Test);
                var key = new ScopedLocal(target, outcome.Identity);
                if (!outcomeDeclarations.TryGetValue(
                        key,
                        out List<(ParsedDeclaration, ResourceEffect.Outcome)>? definitions))
                {
                    definitions = [];
                    outcomeDeclarations.Add(key, definitions);
                }
                definitions.Add((declaration, fieldResolvedOutcome));
            }
            if (declaration.Effect is ResourceEffect.Callback callback)
            {
                var key = new ScopedIndex(target, callback.Scope.Index);
                if (callbacks.TryGetValue(key, out ResourceEffect.Callback? existing)
                    && existing != callback)
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
                        "One callback identity has inconsistent definitions on the same operation.");
                }
                callbacks[key] = callback;
            }
            if (declaration.Effect is ResourceEffect.Consume
                {
                    Target: ResourceEffectLocation.Operation operation,
                })
            {
                var key = new ScopedIndex(target, operation.Index);
                if (!operationDeclarations.TryGetValue(
                        key,
                        out List<ParsedDeclaration>? definitions))
                {
                    definitions = [];
                    operationDeclarations.Add(key, definitions);
                }
                definitions.Add(declaration);
            }
        }

        foreach (ParsedDeclaration declaration in declarations)
        {
            string target = ResourceEffectCanonicalizer.Target(declaration.Target);
            foreach (ResourceEffectLocation location in Locations(declaration.Effect))
            {
                if (!AllLocalFieldsDefined(location, fields))
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.UnresolvedField,
                        "A rooted field location references no field declaration in the atomic model.");
                }
                int? callbackIndex = CallbackIndex(location);
                if (callbackIndex is int callback
                    && !callbacks.ContainsKey(new ScopedIndex(target, callback)))
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.UnresolvedCallback,
                        "A callback location references no callback declaration on the operation.");
                }
                foreach (ResourceEffectLocation.Operation operation
                         in LocalOperationLocations(location))
                {
                    if (!operationDeclarations.ContainsKey(
                            new ScopedIndex(target, operation.Index)))
                    {
                        return Failure(
                            declaration,
                            ResourceEffectDiagnosticKind.UnresolvedOperation,
                            "An operation location references no consume declaration on the operation.");
                    }
                }
            }
            foreach (ResourceBorrowScope scope in Scopes(declaration.Effect))
            {
                if (scope is ResourceBorrowScope.Callback callback
                    && !callbacks.ContainsKey(new ScopedIndex(target, callback.Index)))
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.UnresolvedCallback,
                        "A callback scope references no callback declaration on the operation.");
                }
            }
            foreach (ResourceEffectCompletion completion in Completions(declaration.Effect))
            {
                if (completion is ResourceEffectCompletion.Outcome outcome
                    && !outcomeDeclarations.ContainsKey(
                        new ScopedLocal(target, outcome.Identity)))
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.UnresolvedOutcome,
                        "An outcome completion references no outcome declaration on the operation.");
                }
            }
        }

        var operations =
            new Dictionary<ScopedIndex, ResourceEffectLocation.OperationSlot>();
        var resolvingOperations = new HashSet<ScopedIndex>();
        ResourceEffectAdmissionOutcome? operationFailure = null;
        foreach ((ScopedIndex key, List<ParsedDeclaration> definitions)
                 in operationDeclarations)
        {
            ResolveOperation(key, definitions[0]);
            if (operationFailure is not null)
                return operationFailure;
        }
        var outcomes = new Dictionary<ScopedLocal, ResourceEffect.Outcome>();
        foreach ((
                     ScopedLocal key,
                     List<(ParsedDeclaration Declaration, ResourceEffect.Outcome Outcome)> definitions)
                 in outcomeDeclarations)
        {
            ResourceEffect.Outcome? resolved = null;
            foreach ((
                         ParsedDeclaration declaration,
                         ResourceEffect.Outcome outcome)
                     in definitions)
            {
                var candidate = new ResourceEffect.Outcome(
                    outcome.Identity,
                    ResolveLocation(
                        outcome.Source,
                        key.Target,
                        fields,
                        operations),
                    outcome.Test);
                if (resolved is not null && resolved != candidate)
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
                        "One outcome identity has inconsistent definitions on the same operation.");
                }
                resolved = candidate;
            }
            outcomes.Add(key, resolved!);
        }
        for (int index = 0; index < declarations.Count; index++)
        {
            ParsedDeclaration declaration = declarations[index];
            string target = ResourceEffectCanonicalizer.Target(declaration.Target);
            ResourceEffect resolved = ResolveEffect(
                declaration.Effect,
                target,
                fields,
                outcomes,
                operations);
            ResourceEffectAdmissionOutcome? budgetFailure =
                ValidateStructuralBudget(
                    model,
                    declaration.Provenance,
                    declaration.Target,
                    resolved,
                    limits);
            if (budgetFailure is not null)
                return budgetFailure;
            if (!StructuralFieldsAreValid(declaration.Target, resolved))
            {
                return Failure(
                    declaration,
                    ResourceEffectDiagnosticKind.UnboundGenericVariable,
                    "A structural field selector contains a generic variable not bound by the outer operation.");
            }
            declarations[index] = declaration with
            {
                Effect = resolved,
            };
        }

        var resolvedOperationDeclarations =
            declarations
                .Where(declaration =>
                    declaration.Effect is ResourceEffect.Consume
                    {
                        Target: ResourceEffectLocation.OperationSlot,
                    })
                .Select(declaration =>
                {
                    var consume = (ResourceEffect.Consume)declaration.Effect;
                    return (
                        Target: ResourceEffectCanonicalizer.Target(declaration.Target),
                        Slot: (ResourceEffectLocation.OperationSlot)consume.Target);
                })
                .ToHashSet();
        foreach (ParsedDeclaration declaration in declarations)
        {
            string target = ResourceEffectCanonicalizer.Target(declaration.Target);
            foreach (ResourceEffectLocation location in Locations(declaration.Effect))
            {
                foreach (ResourceEffectLocation.OperationSlot operation
                         in OperationSlotLocations(location))
                {
                    if (!resolvedOperationDeclarations.Contains((target, operation)))
                    {
                        return Failure(
                            declaration,
                            ResourceEffectDiagnosticKind.UnresolvedOperation,
                            "A canonical operation slot references no defining consume declaration in the atomic model.");
                    }
                }
            }
        }
        return null;

        ResourceEffectLocation.OperationSlot? ResolveOperation(
            ScopedIndex key,
            ParsedDeclaration reference)
        {
            if (operations.TryGetValue(
                    key,
                    out ResourceEffectLocation.OperationSlot? existing))
            {
                return existing;
            }
            if (!resolvingOperations.Add(key))
            {
                operationFailure = Failure(
                    reference,
                    ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
                    "Model-local operation slots form a cyclic consume definition.");
                return null;
            }
            if (resolvingOperations.Count > limits.MaxNestingDepth)
            {
                operationFailure = Limit(
                    ResourceEffectWorkLimitKind.NestingDepth,
                    limits.MaxNestingDepth,
                    resolvingOperations.Count,
                    model,
                    reference.Provenance);
                return null;
            }

            ResourceEffectLocation.OperationSlot? resolved = null;
            foreach (ParsedDeclaration definition in operationDeclarations[key])
            {
                var consume = (ResourceEffect.Consume)definition.Effect;
                ResourceEffectLocation? source =
                    ResolveOperationLocation(consume.Source, key.Target, definition);
                if (source is null)
                    return null;
                var candidate =
                    new ResourceEffectLocation.OperationSlot(source, consume.Kind);
                if (resolved is not null && resolved != candidate)
                {
                    operationFailure = Failure(
                        definition,
                        ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
                        "One model-local operation slot has inconsistent consume definitions.");
                    return null;
                }
                resolved = candidate;
            }

            resolvingOperations.Remove(key);
            operations.Add(key, resolved!);
            return resolved;
        }

        ResourceEffectLocation? ResolveOperationLocation(
            ResourceEffectLocation location,
            string target,
            ParsedDeclaration reference)
        {
            switch (location)
            {
                case ResourceEffectLocation.Field field:
                    return new ResourceEffectLocation.StructuralField(
                        ResolveOperationLocation(field.Root, target, reference)!,
                        fields[field.Selector]);
                case ResourceEffectLocation.StructuralField field:
                    return new ResourceEffectLocation.StructuralField(
                        ResolveOperationLocation(field.Root, target, reference)!,
                        field.Selector);
                case ResourceEffectLocation.Operation operation:
                    return ResolveOperation(
                        new ScopedIndex(target, operation.Index),
                        reference);
                case ResourceEffectLocation.OperationSlot operation:
                    ResourceEffectLocation? source =
                        ResolveOperationLocation(operation.Source, target, reference);
                    return source is null
                        ? null
                        : new ResourceEffectLocation.OperationSlot(
                            source,
                            operation.Kind);
                default:
                    return location;
            }
        }

        ResourceEffectAdmissionOutcome Failure(
            ParsedDeclaration declaration,
            ResourceEffectDiagnosticKind kind,
            string message)
            => Reject(
                model,
                declaration.Provenance,
                kind,
                0,
                declaration.SourceLength,
                message);
    }

    static bool AllLocalFieldsDefined(
        ResourceEffectLocation location,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields)
        => location switch
        {
            ResourceEffectLocation.Field field =>
                fields.ContainsKey(field.Selector)
                && AllLocalFieldsDefined(field.Root, fields),
            ResourceEffectLocation.StructuralField field =>
                AllLocalFieldsDefined(field.Root, fields),
            ResourceEffectLocation.OperationSlot operation =>
                AllLocalFieldsDefined(operation.Source, fields),
            _ => true,
        };

    static IEnumerable<ResourceEffectLocation.Operation> LocalOperationLocations(
        ResourceEffectLocation location)
    {
        switch (location)
        {
            case ResourceEffectLocation.Operation operation:
                yield return operation;
                break;
            case ResourceEffectLocation.Field field:
                foreach (ResourceEffectLocation.Operation nested
                         in LocalOperationLocations(field.Root))
                {
                    yield return nested;
                }
                break;
            case ResourceEffectLocation.StructuralField field:
                foreach (ResourceEffectLocation.Operation nested
                         in LocalOperationLocations(field.Root))
                {
                    yield return nested;
                }
                break;
            case ResourceEffectLocation.OperationSlot operation:
                foreach (ResourceEffectLocation.Operation nested
                         in LocalOperationLocations(operation.Source))
                {
                    yield return nested;
                }
                break;
        }
    }

    static IEnumerable<ResourceEffectLocation.OperationSlot> OperationSlotLocations(
        ResourceEffectLocation location)
    {
        switch (location)
        {
            case ResourceEffectLocation.OperationSlot operation:
                yield return operation;
                foreach (ResourceEffectLocation.OperationSlot nested
                         in OperationSlotLocations(operation.Source))
                {
                    yield return nested;
                }
                break;
            case ResourceEffectLocation.Field field:
                foreach (ResourceEffectLocation.OperationSlot nested
                         in OperationSlotLocations(field.Root))
                {
                    yield return nested;
                }
                break;
            case ResourceEffectLocation.StructuralField field:
                foreach (ResourceEffectLocation.OperationSlot nested
                         in OperationSlotLocations(field.Root))
                {
                    yield return nested;
                }
                break;
        }
    }

    static ResourceEffect ResolveEffect(
        ResourceEffect effect,
        string target,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields,
        IReadOnlyDictionary<ScopedLocal, ResourceEffect.Outcome> outcomes,
        IReadOnlyDictionary<ScopedIndex, ResourceEffectLocation.OperationSlot> operations)
        => effect switch
        {
            ResourceEffect.Resource resource => new ResourceEffect.Resource(
                resource.Kind,
                resource.Value,
                resource.Value == ResourceDeclaredValueKind.DeclaredField
                    ? null
                    : resource.Selector),
            ResourceEffect.Authority authority => new ResourceEffect.Authority(
                authority.Kind,
                ResolveLocation(authority.Target, target, fields, operations),
                authority.Key),
            ResourceEffect.Acquire acquire => new ResourceEffect.Acquire(
                acquire.Kind,
                ResolveLocation(acquire.Target, target, fields, operations),
                ResolveCompletion(
                    acquire.When,
                    target,
                    fields,
                    outcomes,
                    operations),
                ResolveOptionalLocation(
                    acquire.Correspondence,
                    target,
                    fields,
                    operations),
                ResolveOptionalLocation(acquire.Lender, target, fields, operations)),
            ResourceEffect.Move move => new ResourceEffect.Move(
                ResolveLocation(move.Source, target, fields, operations),
                ResolveLocation(move.Target, target, fields, operations),
                ResolveCompletion(move.When, target, fields, outcomes, operations),
                move.Kind),
            ResourceEffect.Consume consume => new ResourceEffect.Consume(
                ResolveLocation(consume.Source, target, fields, operations),
                ResolveLocation(consume.Target, target, fields, operations),
                consume.Kind),
            ResourceEffect.Release release => new ResourceEffect.Release(
                ResolveLocation(release.Source, target, fields, operations),
                ResolveCompletion(
                    release.When,
                    target,
                    fields,
                    outcomes,
                    operations),
                release.Kind,
                ResolveOptionalLocation(
                    release.Correspondence,
                    target,
                    fields,
                    operations),
                ResolveOptionalLocation(
                    release.Observation,
                    target,
                    fields,
                    operations)),
            ResourceEffect.Borrow borrow => new ResourceEffect.Borrow(
                ResolveLocation(borrow.Source, target, fields, operations),
                ResolveLocation(borrow.Target, target, fields, operations),
                borrow.Access,
                borrow.Scope,
                borrow.Kind,
                ResolveOptionalLocation(borrow.Lender, target, fields, operations),
                borrow.Materialization),
            ResourceEffect.Derive derive => new ResourceEffect.Derive(
                ResolveLocation(derive.Source, target, fields, operations),
                ResolveLocation(derive.Target, target, fields, operations),
                derive.Relation,
                ResolveGuard(derive.Guard, target, fields, operations)),
            ResourceEffect.Pass pass => new ResourceEffect.Pass(
                ResolveLocation(pass.Source, target, fields, operations),
                ResolveLocation(pass.Target, target, fields, operations),
                pass.Identity),
            ResourceEffect.Independent independent => new ResourceEffect.Independent(
                ResolveLocation(independent.Source, target, fields, operations),
                ResolveLocation(independent.Target, target, fields, operations)),
            ResourceEffect.Callback callback => callback,
            ResourceEffect.Accept accept => new ResourceEffect.Accept(
                ResolveLocation(accept.Source, target, fields, operations),
                ResolveLocation(accept.Target, target, fields, operations),
                ResolveCompletion(accept.When, target, fields, outcomes, operations),
                accept.Kind,
                accept.Order),
            ResourceEffect.Operation operation => new ResourceEffect.Operation(
                operation.Boundary,
                operation.Throws,
                ResolveGuard(operation.Guard, target, fields, operations)),
            ResourceEffect.Outcome outcome => new ResourceEffect.Outcome(
                outcome.Identity,
                ResolveLocation(outcome.Source, target, fields, operations),
                outcome.Test),
            _ => throw new InvalidOperationException("Unknown resource effect."),
        };

    static ResourceEffectLocation ResolveLocation(
        ResourceEffectLocation location,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields)
        => location switch
        {
            ResourceEffectLocation.Field field => new ResourceEffectLocation.StructuralField(
                ResolveLocation(field.Root, fields),
                fields[field.Selector]),
            ResourceEffectLocation.StructuralField field =>
                new ResourceEffectLocation.StructuralField(
                    ResolveLocation(field.Root, fields),
                    field.Selector),
            _ => location,
        };

    static ResourceEffectLocation ResolveLocation(
        ResourceEffectLocation location,
        string target,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields,
        IReadOnlyDictionary<ScopedIndex, ResourceEffectLocation.OperationSlot> operations)
        => location switch
        {
            ResourceEffectLocation.Field field => new ResourceEffectLocation.StructuralField(
                ResolveLocation(field.Root, target, fields, operations),
                fields[field.Selector]),
            ResourceEffectLocation.StructuralField field =>
                new ResourceEffectLocation.StructuralField(
                    ResolveLocation(field.Root, target, fields, operations),
                    field.Selector),
            ResourceEffectLocation.Operation operation =>
                operations[new ScopedIndex(target, operation.Index)],
            ResourceEffectLocation.OperationSlot operation =>
                new ResourceEffectLocation.OperationSlot(
                    ResolveLocation(operation.Source, target, fields, operations),
                    operation.Kind),
            _ => location,
        };

    static ResourceEffectLocation? ResolveOptionalLocation(
        ResourceEffectLocation? location,
        string target,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields,
        IReadOnlyDictionary<ScopedIndex, ResourceEffectLocation.OperationSlot> operations)
        => location is null
            ? null
            : ResolveLocation(location, target, fields, operations);

    static ResourceEffectCompletion ResolveCompletion(
        ResourceEffectCompletion completion,
        string target,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields,
        IReadOnlyDictionary<ScopedLocal, ResourceEffect.Outcome> outcomes,
        IReadOnlyDictionary<ScopedIndex, ResourceEffectLocation.OperationSlot> operations)
    {
        return completion switch
        {
            ResourceEffectCompletion.Outcome outcome =>
                ResolveLocalOutcome(outcome),
            ResourceEffectCompletion.OutcomeCase outcome =>
                new ResourceEffectCompletion.OutcomeCase(
                    ResolveLocation(outcome.Source, target, fields, operations),
                    outcome.Test),
            _ => completion,
        };

        ResourceEffectCompletion ResolveLocalOutcome(
            ResourceEffectCompletion.Outcome outcome)
        {
            ResourceEffect.Outcome definition =
                outcomes[new ScopedLocal(target, outcome.Identity)];
            return new ResourceEffectCompletion.OutcomeCase(
                ResolveLocation(definition.Source, target, fields, operations),
                definition.Test);
        }
    }

    static ResourceEffectGuard? ResolveGuard(
        ResourceEffectGuard? guard,
        string target,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields,
        IReadOnlyDictionary<ScopedIndex, ResourceEffectLocation.OperationSlot> operations)
        => guard is ResourceEffectGuard.ExactRuntimeType exact
            ? new ResourceEffectGuard.ExactRuntimeType(
                ResolveLocation(exact.Subject, target, fields, operations),
                exact.Expected)
            : null;

    static bool ValidateLocationShape(
        ResourceEffectMemberSelector member,
        ResourceEffectLocation location)
        => location switch
        {
            ResourceEffectLocation.Receiver => !member.IsStatic,
            ResourceEffectLocation.Return => member.Kind != ResourceEffectMemberKind.Field,
            ResourceEffectLocation.Constructed =>
                member.Kind == ResourceEffectMemberKind.Constructor,
            ResourceEffectLocation.Parameter parameter =>
                parameter.Index < member.Parameters.Length,
            ResourceEffectLocation.Operation => true,
            ResourceEffectLocation.OperationSlot operation =>
                ValidateLocationShape(member, operation.Source),
            ResourceEffectLocation.CallbackParameter callback =>
                callback.CallbackIndex < member.Parameters.Length,
            ResourceEffectLocation.CallbackReturn callback =>
                callback.CallbackIndex < member.Parameters.Length,
            ResourceEffectLocation.Field field =>
                ValidateLocationShape(member, field.Root),
            ResourceEffectLocation.StructuralField field =>
                ValidateLocationShape(member, field.Root),
            _ => false,
        };

    static bool ValidateSignatureLocation(
        ResourceEffectMemberSelector member,
        ResourceEffectSignatureLocation location)
        => location switch
        {
            ResourceEffectSignatureLocation.Receiver => !member.IsStatic,
            ResourceEffectSignatureLocation.Return => true,
            ResourceEffectSignatureLocation.Parameter parameter =>
                parameter.Index < member.Parameters.Length,
            _ => false,
        };

    static (int TypeArity, int MethodArity) TargetArities(
        ResourceEffectTargetSelector target)
        => target switch
        {
            ResourceEffectTargetSelector.Type type =>
                (type.Selector.Segments.Sum(segment => segment.GenericArity), 0),
            ResourceEffectTargetSelector.Member member =>
                (member.Selector.DeclaringType.Segments.Sum(segment => segment.GenericArity),
                    member.Selector.GenericArity),
            _ => (0, 0),
        };

    static HashSet<ResourceEffectGenericVariable> BoundTypeVariables(
        ResourceEffectTargetSelector target)
    {
        ResourceTypeExpression.Named declaringType = target switch
        {
            ResourceEffectTargetSelector.Type type => type.Selector,
            ResourceEffectTargetSelector.Member member => member.Selector.DeclaringType,
            _ => throw new InvalidOperationException("Unknown target selector."),
        };
        var variables = new HashSet<ResourceEffectGenericVariable>();
        foreach (ResourceTypeExpression argument in declaringType.Arguments)
            Collect(argument, variables);
        return variables;

        static void Collect(
            ResourceTypeExpression expression,
            HashSet<ResourceEffectGenericVariable> variables)
        {
            switch (expression)
            {
                case ResourceTypeExpression.Variable
                    {
                        Value.Kind: ResourceEffectGenericVariableKind.Type,
                    } variable:
                    variables.Add(variable.Value);
                    break;
                case ResourceTypeExpression.Named named:
                    foreach (ResourceTypeExpression argument in named.Arguments)
                        Collect(argument, variables);
                    break;
                case ResourceTypeExpression.SzArray array:
                    Collect(array.Element, variables);
                    break;
                case ResourceTypeExpression.Array array:
                    Collect(array.Element, variables);
                    break;
                case ResourceTypeExpression.ByReference reference:
                    Collect(reference.Element, variables);
                    break;
                case ResourceTypeExpression.Pointer pointer:
                    Collect(pointer.Element, variables);
                    break;
            }
        }
    }

    static IEnumerable<ResourceEffectGenericVariable> Variables(ResourceEffect effect)
    {
        foreach (ResourceKindReference kind in Kinds(effect))
        {
            foreach (ResourceEffectGenericVariable variable in kind.Arguments)
                yield return variable;
        }
        if (effect is ResourceEffect.Authority
            {
                Key: ResourceAuthorityKey.Singleton singleton,
            })
        {
            foreach (ResourceEffectGenericVariable variable in singleton.Arguments)
                yield return variable;
        }
    }

    static IEnumerable<ResourceKindReference> Kinds(ResourceEffect effect)
    {
        ResourceKindReference? kind = effect switch
        {
            ResourceEffect.Resource value => value.Kind,
            ResourceEffect.Authority value => value.Kind,
            ResourceEffect.Acquire value => value.Kind,
            ResourceEffect.Move value => value.Kind,
            ResourceEffect.Consume value => value.Kind,
            ResourceEffect.Release value => value.Kind,
            ResourceEffect.Borrow value => value.Kind,
            ResourceEffect.Accept value => value.Kind,
            _ => null,
        };
        if (kind is not null)
            yield return kind;
        foreach (ResourceEffectLocation location in Locations(effect))
        {
            foreach (ResourceKindReference nested in LocationKinds(location))
                yield return nested;
        }
    }

    static IEnumerable<ResourceKindReference> LocationKinds(
        ResourceEffectLocation location)
    {
        switch (location)
        {
            case ResourceEffectLocation.Field field:
                foreach (ResourceKindReference kind in LocationKinds(field.Root))
                    yield return kind;
                break;
            case ResourceEffectLocation.StructuralField field:
                foreach (ResourceKindReference kind in LocationKinds(field.Root))
                    yield return kind;
                break;
            case ResourceEffectLocation.OperationSlot operation:
                if (operation.Kind is not null)
                    yield return operation.Kind;
                foreach (ResourceKindReference kind in LocationKinds(operation.Source))
                    yield return kind;
                break;
        }
    }

    static IEnumerable<ResourceEffectLocation> Locations(ResourceEffect effect)
    {
        switch (effect)
        {
            case ResourceEffect.Authority value:
                yield return value.Target;
                break;
            case ResourceEffect.Acquire value:
                yield return value.Target;
                if (value.Correspondence is not null)
                    yield return value.Correspondence;
                if (value.Lender is not null)
                    yield return value.Lender;
                break;
            case ResourceEffect.Move value:
                yield return value.Source;
                yield return value.Target;
                break;
            case ResourceEffect.Consume value:
                yield return value.Source;
                yield return value.Target;
                break;
            case ResourceEffect.Release value:
                yield return value.Source;
                if (value.Correspondence is not null)
                    yield return value.Correspondence;
                if (value.Observation is not null)
                    yield return value.Observation;
                break;
            case ResourceEffect.Borrow value:
                yield return value.Source;
                yield return value.Target;
                if (value.Lender is not null)
                    yield return value.Lender;
                break;
            case ResourceEffect.Derive value:
                yield return value.Source;
                yield return value.Target;
                if (value.Guard is ResourceEffectGuard.ExactRuntimeType guard)
                    yield return guard.Subject;
                break;
            case ResourceEffect.Pass value:
                yield return value.Source;
                yield return value.Target;
                break;
            case ResourceEffect.Independent value:
                yield return value.Source;
                yield return value.Target;
                break;
            case ResourceEffect.Callback value:
                yield return value.Delegate;
                break;
            case ResourceEffect.Accept value:
                yield return value.Source;
                yield return value.Target;
                break;
            case ResourceEffect.Operation
                {
                    Guard: ResourceEffectGuard.ExactRuntimeType operationGuard,
                }:
                yield return operationGuard.Subject;
                break;
            case ResourceEffect.Outcome value:
                yield return value.Source;
                break;
        }
        foreach (ResourceEffectCompletion completion in Completions(effect))
        {
            if (completion is ResourceEffectCompletion.OutcomeCase outcome)
                yield return outcome.Source;
        }
    }

    static IEnumerable<ResourceEffectSignatureLocation> SignatureLocations(
        ResourceEffect effect)
    {
        ResourceEffectGuard? guard = effect switch
        {
            ResourceEffect.Derive derive => derive.Guard,
            ResourceEffect.Operation operation => operation.Guard,
            _ => null,
        };
        if (guard is ResourceEffectGuard.ExactRuntimeType exact)
            yield return exact.Expected;
    }

    static IEnumerable<ResourceBorrowScope> Scopes(ResourceEffect effect)
    {
        if (effect is ResourceEffect.Borrow borrow)
            yield return borrow.Scope;
        if (effect is ResourceEffect.Callback callback)
            yield return callback.Scope;
    }

    static IEnumerable<ResourceEffectCompletion> Completions(ResourceEffect effect)
    {
        ResourceEffectCompletion? completion = effect switch
        {
            ResourceEffect.Acquire acquire => acquire.When,
            ResourceEffect.Move move => move.When,
            ResourceEffect.Release release => release.When,
            ResourceEffect.Accept accept => accept.When,
            _ => null,
        };
        if (completion is not null)
            yield return completion;
    }

    static int? CallbackIndex(ResourceEffectLocation location)
        => location switch
        {
            ResourceEffectLocation.CallbackParameter callback => callback.CallbackIndex,
            ResourceEffectLocation.CallbackReturn callback => callback.CallbackIndex,
            _ => null,
        };

    static ResourceEffectAdmissionOutcome Reject(
        ResourceEffectModelIdentity? model,
        ResourceDeclarationProvenance? provenance,
        ResourceEffectDiagnosticKind kind,
        int offset,
        int length,
        string message)
        => new ResourceEffectAdmissionOutcome.Rejected(
            [new ResourceEffectModelDiagnostic(
                model,
                provenance,
                new ResourceEffectDiagnostic(
                    kind,
                    offset,
                    length,
                    new InertString(TextPolicy.Field, message)))]);

    static ResourceEffectAdmissionOutcome RejectAll(
        IEnumerable<ResourceDeclarationProvenance> provenances,
        ResourceEffectDiagnosticKind kind,
        string message)
    {
        var diagnostic = new ResourceEffectDiagnostic(
            kind,
            0,
            0,
            new InertString(TextPolicy.Field, message));
        return new ResourceEffectAdmissionOutcome.Rejected(
            [.. provenances
                .Distinct()
                .OrderBy(ResourceEffectCanonicalizer.Provenance, StringComparer.Ordinal)
                .Select(provenance =>
                    new ResourceEffectModelDiagnostic(
                        provenance.Model,
                        provenance,
                        diagnostic))]);
    }

    static ResourceEffectAdmissionOutcome Limit(
        ResourceEffectWorkLimitKind kind,
        int limit,
        int required,
        ResourceEffectModelIdentity? model = null,
        ResourceDeclarationProvenance? provenance = null,
        int offset = 0)
        => new ResourceEffectAdmissionOutcome.WorkLimitExceeded(
            kind,
            limit,
            required,
            model,
            provenance,
            offset);

    static ResourceKindDefinition Merge(
        ResourceKindDefinition first,
        ResourceKindDefinition second)
        => new(
            first.Identity,
            first.Arity,
            [.. first.Provenances
                .Concat(second.Provenances)
                .Distinct()
                .OrderBy(ResourceEffectCanonicalizer.Provenance, StringComparer.Ordinal)]);

    static string Hash(string content)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[512];
        for (int offset = 0; offset < content.Length;)
        {
            int count = Math.Min(bytes.Length / sizeof(char), content.Length - offset);
            for (int index = 0; index < count; index++)
            {
                char value = content[offset + index];
                bytes[index * 2] = (byte)(value >> 8);
                bytes[index * 2 + 1] = (byte)value;
            }
            hash.AppendData(bytes[..(count * sizeof(char))]);
            offset += count;
        }
        Span<byte> digest = stackalloc byte[32];
        if (!hash.TryGetHashAndReset(digest, out int written)
            || written != digest.Length)
        {
            throw new CryptographicException("Could not hash resource-effect content.");
        }
        return Convert.ToHexStringLower(digest);
    }

    internal sealed record ParsedDeclaration(
        ResourceEffectTargetSelector Target,
        ResourceEffect Effect,
        ResourceDeclarationProvenance Provenance,
        int SourceLength);

    sealed record ScopedLocal(string Target, ResourceEffectLocalIdentity Identity);
    sealed record ScopedIndex(string Target, int Index);

}

static class ResourceEffectCanonicalizer
{
    public static string ModelContent(
        ImmutableArray<ResourceKindDefinition> definitions,
        ImmutableArray<AdmittedResourceEffectDeclaration> declarations)
        => Node(
            "model-content",
            List(
                "resource-kinds",
                definitions.Select(ResourceKind)),
            List(
                "declarations",
                declarations.Select(declaration =>
                    Declaration(declaration.Target, declaration.Effect))));

    public static string AdmissionContent(
        ImmutableArray<ResourceEffectModelReceipt> receipts)
        => Node(
            "admission-content",
            List(
                "models",
                receipts.Select(receipt =>
                    Node(
                        "model",
                        Atom("identity", receipt.Identity.Value),
                        Atom("language", receipt.Language.Value),
                        Atom("content-hash", receipt.ContentHash),
                        ProvenanceSet(receipt.Provenances)))));

    public static string Declaration(
        ResourceEffectTargetSelector target,
        ResourceEffect effect)
        => Node("declaration", Target(target), Effect(effect));

    public static string Provenance(ResourceDeclarationProvenance provenance)
        => Node(
            "provenance",
            Atom("model", provenance.Model.Value),
            Atom("authority", ((int)provenance.Authority).ToString(CultureInfo.InvariantCulture)),
            Atom("source", provenance.SourceIdentity.ToString()),
            Atom("source-truncated", provenance.SourceIdentity.IsTruncated ? "1" : "0"),
            Atom(
                "ordinal",
                provenance.DeclarationOrdinal.ToString(CultureInfo.InvariantCulture)));

    public static string ProvenanceSet(
        IEnumerable<ResourceDeclarationProvenance> provenances)
        => List("provenances", provenances.Select(Provenance));

    public static string Target(ResourceEffectTargetSelector target)
        => target switch
        {
            ResourceEffectTargetSelector.Type type =>
                Node("target-type", Type(type.Selector)),
            ResourceEffectTargetSelector.Member member =>
                Node("target-member", Member(member.Selector)),
            _ => throw new InvalidOperationException("Unknown target selector."),
        };

    public static string OptionalLocation(ResourceEffectLocation? location)
        => location is null ? Node("no-location") : Location(location);

    public static string Location(ResourceEffectLocation location)
        => location switch
        {
            ResourceEffectLocation.Receiver => Node("location-receiver"),
            ResourceEffectLocation.Return => Node("location-return"),
            ResourceEffectLocation.Constructed => Node("location-constructed"),
            ResourceEffectLocation.Parameter parameter =>
                Node(
                    "location-parameter",
                    Atom("index", parameter.Index.ToString(CultureInfo.InvariantCulture))),
            ResourceEffectLocation.Operation operation =>
                Node(
                    "location-operation",
                    Atom("index", operation.Index.ToString(CultureInfo.InvariantCulture))),
            ResourceEffectLocation.OperationSlot operation =>
                Node(
                    "location-operation-slot",
                    Location(operation.Source),
                    OptionalKind(operation.Kind)),
            ResourceEffectLocation.CallbackParameter callback =>
                Node(
                    "location-callback-parameter",
                    Atom(
                        "callback",
                        callback.CallbackIndex.ToString(CultureInfo.InvariantCulture)),
                    Atom(
                        "parameter",
                        callback.ParameterIndex.ToString(CultureInfo.InvariantCulture))),
            ResourceEffectLocation.CallbackReturn callback =>
                Node(
                    "location-callback-return",
                    Atom(
                        "callback",
                        callback.CallbackIndex.ToString(CultureInfo.InvariantCulture))),
            ResourceEffectLocation.Field field =>
                Node(
                    "location-local-field",
                    Location(field.Root),
                    Atom("selector", field.Selector.Value)),
            ResourceEffectLocation.StructuralField field =>
                Node(
                    "location-structural-field",
                    Location(field.Root),
                    Member(field.Selector)),
            _ => throw new InvalidOperationException("Unknown resource location."),
        };

    static string ResourceKind(ResourceKindDefinition definition)
        => Node(
            "resource-kind-definition",
            Atom("identity", definition.Identity.Value),
            Atom("arity", definition.Arity.ToString(CultureInfo.InvariantCulture)));

    static string Member(ResourceEffectMemberSelector member)
        => Node(
            "member",
            Type(member.DeclaringType),
            Atom("metadata-name", member.MetadataName),
            Atom("kind", ((int)member.Kind).ToString(CultureInfo.InvariantCulture)),
            Atom("static", member.IsStatic ? "1" : "0"),
            Atom(
                "generic-arity",
                member.GenericArity.ToString(CultureInfo.InvariantCulture)),
            Atom(
                "calling-convention",
                ((int)member.CallingConvention).ToString(CultureInfo.InvariantCulture)),
            Atom("has-this", member.HasThis ? "1" : "0"),
            Atom("explicit-this", member.ExplicitThis ? "1" : "0"),
            List(
                "parameters",
                member.Parameters.Select(parameter =>
                    Node(
                        "parameter",
                        Atom(
                            "ref-kind",
                            ((int)parameter.RefKind).ToString(CultureInfo.InvariantCulture)),
                        Type(parameter.Type)))),
            Type(member.ReturnType));

    static string Type(ResourceTypeExpression type)
        => type switch
        {
            ResourceTypeExpression.Variable variable =>
                Node("type-variable", Variable(variable.Value)),
            ResourceTypeExpression.Named named =>
                Node(
                    "type-named",
                    Node(
                        "assembly",
                        Atom("simple-name", named.Assembly.SimpleName),
                        named.Assembly.PublicKeyToken is null
                            ? Node("no-public-key-token")
                            : Atom("public-key-token", named.Assembly.PublicKeyToken),
                        Node(
                            "version-policy",
                            Atom(
                                "kind",
                                ((int)named.Assembly.Version.Kind)
                                    .ToString(CultureInfo.InvariantCulture)),
                            named.Assembly.Version.Version is null
                                ? Node("no-version")
                                : Atom(
                                    "version",
                                    named.Assembly.Version.Version.ToString())),
                        Atom(
                            "core-library-facade",
                            named.Assembly.AllowCoreLibraryFacade ? "1" : "0")),
                    Atom("namespace", named.Namespace),
                    List(
                        "segments",
                        named.Segments.Select(segment =>
                            Node(
                                "segment",
                                Atom("metadata-name", segment.MetadataName),
                                Atom(
                                    "generic-arity",
                                    segment.GenericArity.ToString(
                                        CultureInfo.InvariantCulture))))),
                    List("arguments", named.Arguments.Select(Type))),
            ResourceTypeExpression.SzArray array =>
                Node("type-szarray", Type(array.Element)),
            ResourceTypeExpression.Array array =>
                Node(
                    "type-array",
                    Atom("rank", array.Rank.ToString(CultureInfo.InvariantCulture)),
                    Type(array.Element)),
            ResourceTypeExpression.ByReference reference =>
                Node("type-by-reference", Type(reference.Element)),
            ResourceTypeExpression.Pointer pointer =>
                Node("type-pointer", Type(pointer.Element)),
            _ => throw new InvalidOperationException("Unknown type expression."),
        };

    static string Effect(ResourceEffect effect)
        => effect switch
        {
            ResourceEffect.Resource resource =>
                Node(
                    "effect-resource",
                    Kind(resource.Kind),
                    OptionalEnum("value", resource.Value),
                    OptionalLocal("selector", resource.Selector)),
            ResourceEffect.Authority authority =>
                Node(
                    "effect-authority",
                    Kind(authority.Kind),
                    Location(authority.Target),
                    Authority(authority.Key)),
            ResourceEffect.Acquire acquire =>
                Node(
                    "effect-acquire",
                    Kind(acquire.Kind),
                    Location(acquire.Target),
                    Completion(acquire.When),
                    OptionalLocation(acquire.Correspondence),
                    OptionalLocation(acquire.Lender)),
            ResourceEffect.Move move =>
                Node(
                    "effect-move",
                    OptionalKind(move.Kind),
                    Location(move.Source),
                    Location(move.Target),
                    Completion(move.When)),
            ResourceEffect.Consume consume =>
                Node(
                    "effect-consume",
                    OptionalKind(consume.Kind),
                    Location(consume.Source),
                    Location(consume.Target)),
            ResourceEffect.Release release =>
                Node(
                    "effect-release",
                    OptionalKind(release.Kind),
                    Location(release.Source),
                    Completion(release.When),
                    OptionalLocation(release.Correspondence),
                    OptionalLocation(release.Observation)),
            ResourceEffect.Borrow borrow =>
                Node(
                    "effect-borrow",
                    OptionalKind(borrow.Kind),
                    Location(borrow.Source),
                    Location(borrow.Target),
                    Atom(
                        "access",
                        ((int)borrow.Access).ToString(CultureInfo.InvariantCulture)),
                    Scope(borrow.Scope),
                    OptionalLocation(borrow.Lender),
                    OptionalEnum("materialization", borrow.Materialization)),
            ResourceEffect.Derive derive =>
                Node(
                    "effect-derive",
                    Location(derive.Source),
                    Location(derive.Target),
                    Atom(
                        "relation",
                        ((int)derive.Relation).ToString(CultureInfo.InvariantCulture)),
                    Guard(derive.Guard)),
            ResourceEffect.Pass pass =>
                Node(
                    "effect-pass",
                    Location(pass.Source),
                    Location(pass.Target),
                    OptionalEnum("identity", pass.Identity)),
            ResourceEffect.Independent independent =>
                Node(
                    "effect-independent",
                    Location(independent.Source),
                    Location(independent.Target)),
            ResourceEffect.Callback callback =>
                Node(
                    "effect-callback",
                    Location(callback.Delegate),
                    Scope(callback.Scope),
                    Atom(
                        "execution",
                        ((int)callback.Execution).ToString(CultureInfo.InvariantCulture)),
                    Atom(
                        "cardinality",
                        ((int)callback.Cardinality).ToString(CultureInfo.InvariantCulture))),
            ResourceEffect.Accept accept =>
                Node(
                    "effect-accept",
                    OptionalKind(accept.Kind),
                    Location(accept.Source),
                    Location(accept.Target),
                    Completion(accept.When),
                    OptionalLocal("order", accept.Order)),
            ResourceEffect.Operation operation =>
                Node(
                    "effect-operation",
                    Atom(
                        "boundary",
                        ((int)operation.Boundary).ToString(CultureInfo.InvariantCulture)),
                    Atom(
                        "throws",
                        ((int)operation.Throws).ToString(CultureInfo.InvariantCulture)),
                    Guard(operation.Guard)),
            ResourceEffect.Outcome outcome =>
                Node(
                    "effect-outcome",
                    Atom("identity", outcome.Identity.Value),
                    Location(outcome.Source),
                    OutcomeTest(outcome.Test)),
            _ => throw new InvalidOperationException("Unknown resource effect."),
        };

    static string Kind(ResourceKindReference kind)
        => Node(
            "resource-kind-reference",
            Atom("identity", kind.Identity.Value),
            List("arguments", kind.Arguments.Select(Variable)));

    static string OptionalKind(ResourceKindReference? kind)
        => kind is null ? Node("no-resource-kind") : Kind(kind);

    static string Variable(ResourceEffectGenericVariable variable)
        => Node(
            "generic-variable",
            Atom("kind", ((int)variable.Kind).ToString(CultureInfo.InvariantCulture)),
            Atom("index", variable.Index.ToString(CultureInfo.InvariantCulture)));

    static string Completion(ResourceEffectCompletion completion)
        => completion switch
        {
            ResourceEffectCompletion.Entry => Node("completion-entry"),
            ResourceEffectCompletion.NormalReturn => Node("completion-normal-return"),
            ResourceEffectCompletion.ExceptionalExit => Node("completion-exceptional-exit"),
            ResourceEffectCompletion.SuccessfulAwait => Node("completion-successful-await"),
            ResourceEffectCompletion.Outcome outcome =>
                Node(
                    "completion-local-outcome",
                    Atom("identity", outcome.Identity.Value)),
            ResourceEffectCompletion.OutcomeCase outcome =>
                Node(
                    "completion-outcome-case",
                    Location(outcome.Source),
                    OutcomeTest(outcome.Test)),
            _ => throw new InvalidOperationException("Unknown completion."),
        };

    static string Authority(ResourceAuthorityKey key)
        => key switch
        {
            ResourceAuthorityKey.Value => Node("authority-value"),
            ResourceAuthorityKey.Singleton singleton =>
                Node(
                    "authority-singleton",
                    List("arguments", singleton.Arguments.Select(Variable))),
            _ => throw new InvalidOperationException("Unknown authority key."),
        };

    static string Scope(ResourceBorrowScope scope)
        => scope switch
        {
            ResourceBorrowScope.Call => Node("scope-call"),
            ResourceBorrowScope.Callback callback =>
                Node(
                    "scope-callback",
                    Atom("index", callback.Index.ToString(CultureInfo.InvariantCulture))),
            _ => throw new InvalidOperationException("Unknown borrow scope."),
        };

    static string Guard(ResourceEffectGuard? guard)
        => guard switch
        {
            null => Node("no-guard"),
            ResourceEffectGuard.ExactRuntimeType exact =>
                Node(
                    "guard-exact-runtime-type",
                    Location(exact.Subject),
                    Signature(exact.Expected)),
            _ => throw new InvalidOperationException("Unknown guard."),
        };

    static string Signature(ResourceEffectSignatureLocation location)
        => location switch
        {
            ResourceEffectSignatureLocation.Receiver =>
                Node("signature-receiver"),
            ResourceEffectSignatureLocation.Return =>
                Node("signature-return"),
            ResourceEffectSignatureLocation.Parameter parameter =>
                Node(
                    "signature-parameter",
                    Atom("index", parameter.Index.ToString(CultureInfo.InvariantCulture))),
            _ => throw new InvalidOperationException("Unknown signature location."),
        };

    static string OutcomeTest(ResourceEffectOutcomeTest test)
        => test switch
        {
            ResourceEffectOutcomeTest.Boolean boolean =>
                Node("outcome-boolean", Atom("value", boolean.Value ? "1" : "0")),
            ResourceEffectOutcomeTest.Enum @enum =>
                Node("outcome-enum", Atom("value", @enum.Value)),
            ResourceEffectOutcomeTest.Null => Node("outcome-null"),
            ResourceEffectOutcomeTest.NonNull => Node("outcome-non-null"),
            ResourceEffectOutcomeTest.ExactType type =>
                Node("outcome-exact-type", Atom("selector", type.Selector)),
            _ => throw new InvalidOperationException("Unknown outcome test."),
        };

    static string OptionalEnum<T>(string tag, T? value)
        where T : struct, Enum
        => value is null
            ? Node("no-" + tag)
            : Atom(tag, Convert.ToInt32(value.Value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture));

    static string OptionalLocal(
        string tag,
        ResourceEffectLocalIdentity? identity)
        => identity is null
            ? Node("no-" + tag)
            : Atom(tag, identity.Value.Value);

    static string Atom(string tag, string value)
        => Node(tag, value);

    static string List(string tag, IEnumerable<string> values)
        => Node(tag, [.. values]);

    static string Node(string tag, params string[] fields)
    {
        var builder = new StringBuilder();
        AppendFrame(builder, tag);
        builder.Append(fields.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        foreach (string field in fields)
            AppendFrame(builder, field);
        return builder.ToString();
    }

    static void AppendFrame(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
    }
}
