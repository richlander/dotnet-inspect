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
        Identity = identity;
        Language = language;
        ContentHash = contentHash;
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
}

public sealed record ResourceEffectCatalogReceipt
{
    readonly ImmutableArray<ResourceEffectModelReceipt> _models;

    internal ResourceEffectCatalogReceipt(
        string semanticHash,
        ImmutableArray<ResourceEffectModelReceipt> models)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticHash);
        if (semanticHash.Length != 64 || semanticHash.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A semantic receipt must be a SHA-256 hexadecimal digest.", nameof(semanticHash));
        SemanticHash = semanticHash.ToLowerInvariant();
        _models = ImmutableArrayValueEquality.RequireInitialized(models, nameof(models));
    }

    public string SemanticHash { get; }
    public ImmutableArray<ResourceEffectModelReceipt> Models => _models;

    public bool Equals(ResourceEffectCatalogReceipt? other)
        => other is not null
            && SemanticHash == other.SemanticHash
            && _models.SequenceEqual(other._models);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SemanticHash);
        ImmutableArrayValueEquality.AddToHash(ref hash, _models);
        return hash.ToHashCode();
    }
}

public sealed class ResourceEffectCatalog
{
    readonly ImmutableArray<ResourceKindDefinition> _resourceKinds;
    readonly ImmutableArray<NormalizedResourceEffectDeclaration> _declarations;

    internal ResourceEffectCatalog(
        ImmutableArray<ResourceKindDefinition> resourceKinds,
        ImmutableArray<NormalizedResourceEffectDeclaration> declarations,
        ResourceEffectCatalogReceipt receipt)
    {
        _resourceKinds =
            ImmutableArrayValueEquality.RequireInitialized(resourceKinds, nameof(resourceKinds));
        _declarations =
            ImmutableArrayValueEquality.RequireInitialized(declarations, nameof(declarations));
        Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
    }

    public ImmutableArray<ResourceKindDefinition> ResourceKinds => _resourceKinds;
    public ImmutableArray<NormalizedResourceEffectDeclaration> Declarations => _declarations;
    public ResourceEffectCatalogReceipt Receipt { get; }
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

public abstract class ResourceEffectCatalogOutcome
{
    private protected ResourceEffectCatalogOutcome()
    {
    }

    public sealed class Constructed : ResourceEffectCatalogOutcome
    {
        internal Constructed(ResourceEffectCatalog catalog)
            => Catalog = catalog;

        public ResourceEffectCatalog Catalog { get; }
    }

    public sealed class Rejected : ResourceEffectCatalogOutcome
    {
        readonly ImmutableArray<ResourceEffectModelDiagnostic> _diagnostics;

        internal Rejected(ImmutableArray<ResourceEffectModelDiagnostic> diagnostics)
        {
            _diagnostics =
                ImmutableArrayValueEquality.RequireInitialized(diagnostics, nameof(diagnostics));
            if (_diagnostics.IsEmpty)
                throw new ArgumentException("A rejected catalog requires diagnostics.", nameof(diagnostics));
        }

        public ImmutableArray<ResourceEffectModelDiagnostic> Diagnostics => _diagnostics;
    }

    public sealed class WorkLimitExceeded : ResourceEffectCatalogOutcome
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

public static class ResourceEffectCatalogBuilder
{
    public static ResourceEffectCatalogOutcome Build(
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

        var parsedModels = new List<ParsedModel>(snapshot.Count);
        int catalogStatementCount = 0;
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
                model.Declarations.Length + model.NormalizedDeclarations.Length;
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
                + model.NormalizedDeclarations.Length;
            if (statementCount > limits.MaxStatementsPerModel)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.ModelStatements,
                    limits.MaxStatementsPerModel,
                    statementCount,
                    model.Identity);
            }
            if (catalogStatementCount > limits.MaxCatalogStatements - statementCount)
            {
                return Limit(
                    ResourceEffectWorkLimitKind.CatalogStatements,
                    limits.MaxCatalogStatements,
                    catalogStatementCount + statementCount,
                    model.Identity);
            }
            catalogStatementCount += statementCount;

            ResourceEffectCatalogOutcome? failure =
                TryParseModel(model, limits, out ParsedModel? parsed);
            if (failure is not null)
                return failure;
            parsedModels.Add(parsed!);
        }

        ResourceEffectCatalogOutcome? compositionFailure =
            TryCompose(parsedModels, out ResourceEffectCatalog? catalog);
        return compositionFailure
            ?? new ResourceEffectCatalogOutcome.Constructed(catalog!);
    }

    static ResourceEffectCatalogOutcome? TryParseModel(
        ResourceEffectModelDefinition model,
        ResourceEffectWorkLimits limits,
        out ParsedModel? parsed)
    {
        parsed = null;
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
            ResourceEffectCatalogOutcome? targetFailure =
                ValidateTarget(targetDeclaration.Target, model.Identity);
            if (targetFailure is not null)
                return targetFailure;

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

                ResourceEffectParseOutcome parse =
                    ResourceEffectStatementParser.Parse(source.Text, limits);
                switch (parse)
                {
                    case ResourceEffectParseOutcome.Rejected rejected:
                        return new ResourceEffectCatalogOutcome.Rejected(
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
                        ResourceEffectCatalogOutcome? validationFailure =
                            ValidateDeclaration(
                                model.Identity,
                                targetDeclaration.Target,
                                source.Provenance,
                                source.Text.Length,
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

        foreach (NormalizedResourceEffectDeclaration declaration
                 in model.NormalizedDeclarations)
        {
            ResourceEffectCatalogOutcome? targetFailure =
                ValidateTarget(declaration.Target, model.Identity);
            if (targetFailure is not null)
                return targetFailure;
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
            ResourceEffectCatalogOutcome? validationFailure =
                ValidateDeclaration(
                    model.Identity,
                    declaration.Target,
                    declaration.Provenances[0],
                    0,
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

        ResourceEffectCatalogOutcome? localFailure =
            ValidateModelLocalReferences(model.Identity, declarations);
        if (localFailure is not null)
            return localFailure;

        ImmutableArray<ParsedDeclaration> orderedDeclarations =
            [.. declarations.OrderBy(
                declaration => ResourceEffectCanonicalizer.Declaration(
                    declaration.Target,
                    declaration.Effect),
                StringComparer.Ordinal)];
        ImmutableArray<ResourceKindDefinition> orderedDefinitions =
            [.. definitions.Values
                .OrderBy(definition => definition.Identity.Value, StringComparer.Ordinal)
                .ThenBy(definition => definition.Arity)];
        ImmutableArray<ResourceDeclarationProvenance> provenances =
            [.. definitions.Values.SelectMany(definition => definition.Provenances)
                .Concat(declarations.Select(declaration => declaration.Provenance))
                .Distinct()
                .OrderBy(ResourceEffectCanonicalizer.Provenance, StringComparer.Ordinal)];
        string content = ResourceEffectCanonicalizer.ModelContent(
            orderedDefinitions,
            orderedDeclarations);
        parsed = new ParsedModel(
            model.Identity,
            model.Language,
            orderedDefinitions,
            orderedDeclarations,
            new ResourceEffectModelReceipt(
                model.Identity,
                model.Language,
                Hash(content),
                provenances));
        return null;
    }

    static ResourceEffectCatalogOutcome ResourceKindLimit(
        ResourceEffectModelDefinition model,
        ResourceEffectWorkLimits limits,
        int required)
        => Limit(
            ResourceEffectWorkLimitKind.ModelResourceKinds,
            limits.MaxResourceKindsPerModel,
            required,
            model.Identity);

    static ResourceEffectCatalogOutcome? ValidateTarget(
        ResourceEffectTargetSelector target,
        ResourceEffectModelIdentity model)
    {
        bool valid = target switch
        {
            ResourceEffectTargetSelector.Type type =>
                ValidateTypeVariables(type.Selector, type.Selector.Segments.Sum(segment => segment.GenericArity), 0),
            ResourceEffectTargetSelector.Member member =>
                ValidateMemberVariables(member.Selector),
            _ => false,
        };
        return valid
            ? null
            : Reject(
                model,
                null,
                ResourceEffectDiagnosticKind.UnboundGenericVariable,
                0,
                0,
                "A structural selector contains an unbound generic variable.");
    }

    static bool ValidateMemberVariables(ResourceEffectMemberSelector member)
    {
        int typeArity = member.DeclaringType.Segments.Sum(segment => segment.GenericArity);
        if (!ValidateTypeVariables(member.DeclaringType, typeArity, member.GenericArity))
            return false;
        foreach (ResourceEffectParameterSelector parameter in member.Parameters)
        {
            if (!ValidateTypeVariables(parameter.Type, typeArity, member.GenericArity))
                return false;
        }
        return ValidateTypeVariables(member.ReturnType, typeArity, member.GenericArity);
    }

    static bool ValidateResolvedFieldVariables(
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

    static ResourceEffectCatalogOutcome? ValidateDeclaration(
        ResourceEffectModelIdentity model,
        ResourceEffectTargetSelector target,
        ResourceDeclarationProvenance provenance,
        int sourceLength,
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
                        || resource.Selector is null)
                    {
                        return Invalid(
                            ResourceEffectDiagnosticKind.InvalidTarget,
                            "A field resource declaration requires value=declared-field and selector.");
                    }
                    break;
                default:
                    return Invalid(
                        ResourceEffectDiagnosticKind.InvalidTarget,
                        "A resource statement may target only a type or field.");
            }
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
        if (!ResolvedFieldsAreValid(
                    target,
                    effect,
                    typeArity,
                    methodArity,
                    boundTypeVariables))
        {
            return Invalid(
                    ResourceEffectDiagnosticKind.UnboundGenericVariable,
                    "A resolved field selector contains a generic variable not bound by the outer operation.");
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

        ResourceEffectCatalogOutcome Invalid(
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

    static bool ResolvedFieldsAreValid(
        ResourceEffectTargetSelector target,
        ResourceEffect effect)
    {
        (int typeArity, int methodArity) = TargetArities(target);
        return ResolvedFieldsAreValid(
            target,
            effect,
            typeArity,
            methodArity,
            BoundTypeVariables(target));
    }

    static bool ResolvedFieldsAreValid(
        ResourceEffectTargetSelector target,
        ResourceEffect effect,
        int typeArity,
        int methodArity,
        HashSet<ResourceEffectGenericVariable> boundTypeVariables)
    {
        if (target is not ResourceEffectTargetSelector.Member)
            return !Locations(effect).Any(
                location => location is ResourceEffectLocation.ResolvedField);
        return Locations(effect)
            .OfType<ResourceEffectLocation.ResolvedField>()
            .All(field => ValidateResolvedFieldVariables(
                field.Selector,
                typeArity,
                methodArity,
                boundTypeVariables));
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

    static ResourceEffectCatalogOutcome? ValidateModelLocalReferences(
        ResourceEffectModelIdentity model,
        List<ParsedDeclaration> declarations)
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

        var outcomes = new Dictionary<ScopedLocal, ResourceEffect.Outcome>();
        var callbacks = new Dictionary<ScopedIndex, ResourceEffect.Callback>();
        var operations = new HashSet<ScopedIndex>();
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
                var canonicalOutcome = new ResourceEffect.Outcome(
                    outcome.Identity,
                    ResolveLocation(outcome.Source, fields),
                    outcome.Test);
                var key = new ScopedLocal(target, outcome.Identity);
                if (outcomes.TryGetValue(key, out ResourceEffect.Outcome? existing)
                    && existing != canonicalOutcome)
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.DuplicateLocalIdentity,
                        "One outcome identity has inconsistent definitions on the same operation.");
                }
                outcomes[key] = canonicalOutcome;
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
            if (declaration.Effect is ResourceEffect.Consume consume)
                operations.Add(new ScopedIndex(target, consume.Target.Index));
        }

        foreach (ParsedDeclaration declaration in declarations)
        {
            string target = ResourceEffectCanonicalizer.Target(declaration.Target);
            foreach (ResourceEffectLocation location in Locations(declaration.Effect))
            {
                if (location is ResourceEffectLocation.Field field
                    && !fields.ContainsKey(field.Selector))
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
                if (location is ResourceEffectLocation.Operation operation
                    && !operations.Contains(new ScopedIndex(target, operation.Index)))
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.UnresolvedOperation,
                        "An operation location references no consume declaration on the operation.");
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
                    && !outcomes.ContainsKey(new ScopedLocal(target, outcome.Identity)))
                {
                    return Failure(
                        declaration,
                        ResourceEffectDiagnosticKind.UnresolvedOutcome,
                        "An outcome completion references no outcome declaration on the operation.");
                }
            }
        }

        for (int index = 0; index < declarations.Count; index++)
        {
            ParsedDeclaration declaration = declarations[index];
            string target = ResourceEffectCanonicalizer.Target(declaration.Target);
            ResourceEffect resolved = ResolveEffect(
                declaration.Effect,
                target,
                fields,
                outcomes);
            if (!ResolvedFieldsAreValid(declaration.Target, resolved))
            {
                return Failure(
                    declaration,
                    ResourceEffectDiagnosticKind.UnboundGenericVariable,
                    "A resolved field selector contains a generic variable not bound by the outer operation.");
            }
            declarations[index] = declaration with
            {
                Effect = resolved,
            };
        }
        return null;

        ResourceEffectCatalogOutcome Failure(
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
            ResourceEffectLocation.ResolvedField field =>
                AllLocalFieldsDefined(field.Root, fields),
            _ => true,
        };

    static ResourceEffect ResolveEffect(
        ResourceEffect effect,
        string target,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields,
        IReadOnlyDictionary<ScopedLocal, ResourceEffect.Outcome> outcomes)
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
                ResolveLocation(authority.Target, fields),
                authority.Key),
            ResourceEffect.Acquire acquire => new ResourceEffect.Acquire(
                acquire.Kind,
                ResolveLocation(acquire.Target, fields),
                ResolveCompletion(acquire.When, target, fields, outcomes),
                ResolveOptionalLocation(acquire.Correspondence, fields),
                ResolveOptionalLocation(acquire.Lender, fields)),
            ResourceEffect.Move move => new ResourceEffect.Move(
                ResolveLocation(move.Source, fields),
                ResolveLocation(move.Target, fields),
                ResolveCompletion(move.When, target, fields, outcomes),
                move.Kind),
            ResourceEffect.Consume consume => new ResourceEffect.Consume(
                ResolveLocation(consume.Source, fields),
                consume.Target,
                consume.Kind),
            ResourceEffect.Release release => new ResourceEffect.Release(
                ResolveLocation(release.Source, fields),
                ResolveCompletion(release.When, target, fields, outcomes),
                release.Kind,
                ResolveOptionalLocation(release.Correspondence, fields),
                ResolveOptionalLocation(release.Observation, fields)),
            ResourceEffect.Borrow borrow => new ResourceEffect.Borrow(
                ResolveLocation(borrow.Source, fields),
                ResolveLocation(borrow.Target, fields),
                borrow.Access,
                borrow.Scope,
                borrow.Kind,
                ResolveOptionalLocation(borrow.Lender, fields),
                borrow.Materialization),
            ResourceEffect.Derive derive => new ResourceEffect.Derive(
                ResolveLocation(derive.Source, fields),
                ResolveLocation(derive.Target, fields),
                derive.Relation,
                ResolveGuard(derive.Guard, fields)),
            ResourceEffect.Pass pass => new ResourceEffect.Pass(
                ResolveLocation(pass.Source, fields),
                ResolveLocation(pass.Target, fields),
                pass.Identity),
            ResourceEffect.Independent independent => new ResourceEffect.Independent(
                ResolveLocation(independent.Source, fields),
                ResolveLocation(independent.Target, fields)),
            ResourceEffect.Callback callback => callback,
            ResourceEffect.Accept accept => new ResourceEffect.Accept(
                ResolveLocation(accept.Source, fields),
                ResolveLocation(accept.Target, fields),
                ResolveCompletion(accept.When, target, fields, outcomes),
                accept.Kind,
                accept.Order),
            ResourceEffect.Operation operation => new ResourceEffect.Operation(
                operation.Boundary,
                operation.Throws,
                ResolveGuard(operation.Guard, fields)),
            ResourceEffect.Outcome outcome => new ResourceEffect.Outcome(
                outcome.Identity,
                ResolveLocation(outcome.Source, fields),
                outcome.Test),
            _ => throw new InvalidOperationException("Unknown resource effect."),
        };

    static ResourceEffectLocation ResolveLocation(
        ResourceEffectLocation location,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields)
        => location switch
        {
            ResourceEffectLocation.Field field => new ResourceEffectLocation.ResolvedField(
                ResolveLocation(field.Root, fields),
                fields[field.Selector]),
            ResourceEffectLocation.ResolvedField field =>
                new ResourceEffectLocation.ResolvedField(
                    ResolveLocation(field.Root, fields),
                    field.Selector),
            _ => location,
        };

    static ResourceEffectLocation? ResolveOptionalLocation(
        ResourceEffectLocation? location,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields)
        => location is null ? null : ResolveLocation(location, fields);

    static ResourceEffectCompletion ResolveCompletion(
        ResourceEffectCompletion completion,
        string target,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields,
        IReadOnlyDictionary<ScopedLocal, ResourceEffect.Outcome> outcomes)
    {
        return completion switch
        {
            ResourceEffectCompletion.Outcome outcome =>
                ResolveLocalOutcome(outcome),
            ResourceEffectCompletion.ResolvedOutcome outcome =>
                new ResourceEffectCompletion.ResolvedOutcome(
                    ResolveLocation(outcome.Source, fields),
                    outcome.Test),
            _ => completion,
        };

        ResourceEffectCompletion ResolveLocalOutcome(
            ResourceEffectCompletion.Outcome outcome)
        {
            ResourceEffect.Outcome definition =
                outcomes[new ScopedLocal(target, outcome.Identity)];
            return new ResourceEffectCompletion.ResolvedOutcome(
                ResolveLocation(definition.Source, fields),
                definition.Test);
        }
    }

    static ResourceEffectGuard? ResolveGuard(
        ResourceEffectGuard? guard,
        IReadOnlyDictionary<ResourceEffectLocalIdentity, ResourceEffectMemberSelector> fields)
        => guard is ResourceEffectGuard.ExactRuntimeType exact
            ? new ResourceEffectGuard.ExactRuntimeType(
                ResolveLocation(exact.Subject, fields),
                exact.Expected)
            : null;

    static ResourceEffectCatalogOutcome? TryCompose(
        List<ParsedModel> models,
        out ResourceEffectCatalog? catalog)
    {
        catalog = null;
        var resourceKinds = new Dictionary<ResourceKindIdentity, ResourceKindDefinition>();
        foreach (ParsedModel model in models)
        {
            foreach (ResourceKindDefinition definition in model.ResourceKinds)
            {
                if (resourceKinds.TryGetValue(
                        definition.Identity,
                        out ResourceKindDefinition? existing)
                    && existing.Arity != definition.Arity)
                {
                    return RejectAll(
                        existing.Provenances.Concat(definition.Provenances),
                        ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                        "Admitted models declare incompatible arities for one catalog-global resource kind.");
                }
                resourceKinds[definition.Identity] =
                    existing is null ? definition : Merge(existing, definition);
            }
        }

        foreach (ParsedModel model in models)
        {
            foreach (ParsedDeclaration declaration in model.Declarations)
            {
                foreach (ResourceKindReference reference in Kinds(declaration.Effect))
                {
                    if (!resourceKinds.TryGetValue(reference.Identity, out ResourceKindDefinition? definition))
                    {
                        return Reject(
                            model.Identity,
                            declaration.Provenance,
                            ResourceEffectDiagnosticKind.UnresolvedResourceKind,
                            0,
                            declaration.SourceLength,
                            "An effect references no admitted catalog-global resource kind.");
                    }
                    if (definition.Arity != reference.Arguments.Length)
                    {
                        return RejectAll(
                            definition.Provenances.Append(declaration.Provenance),
                            ResourceEffectDiagnosticKind.ResourceKindArityMismatch,
                            "A resource-kind reference has incompatible generic arity.");
                    }
                }
            }
        }

        var coalesced =
            new Dictionary<SemanticDeclarationKey, List<ResourceDeclarationProvenance>>();
        foreach (ParsedDeclaration declaration in models.SelectMany(model => model.Declarations))
        {
            var key = new SemanticDeclarationKey(declaration.Target, declaration.Effect);
            if (!coalesced.TryGetValue(key, out List<ResourceDeclarationProvenance>? provenances))
            {
                provenances = [];
                coalesced.Add(key, provenances);
            }
            provenances.Add(declaration.Provenance);
        }

        ImmutableArray<NormalizedResourceEffectDeclaration> normalized =
            [.. coalesced
                .Select(pair => new NormalizedResourceEffectDeclaration(
                    pair.Key.Target,
                    pair.Key.Effect,
                    [.. pair.Value
                        .Distinct()
                        .OrderBy(ResourceEffectCanonicalizer.Provenance, StringComparer.Ordinal)]))
                .OrderBy(
                    declaration => ResourceEffectCanonicalizer.Declaration(
                        declaration.Target,
                        declaration.Effect),
                    StringComparer.Ordinal)];

        ResourceEffectCatalogOutcome? conflict = FindConflict(normalized);
        if (conflict is not null)
            return conflict;

        ImmutableArray<ResourceKindDefinition> orderedKinds =
            [.. resourceKinds.Values
                .OrderBy(definition => definition.Identity.Value, StringComparer.Ordinal)
                .ThenBy(definition => definition.Arity)];
        ImmutableArray<ResourceEffectModelReceipt> receipts =
            [.. models.Select(model => model.Receipt)
                .OrderBy(receipt => receipt.Identity.Value, StringComparer.Ordinal)
                .ThenBy(receipt => receipt.ContentHash, StringComparer.Ordinal)
                .ThenBy(
                    receipt => ResourceEffectCanonicalizer.ProvenanceSet(
                        receipt.Provenances),
                    StringComparer.Ordinal)];
        string semanticContent =
            ResourceEffectCanonicalizer.CatalogContent(
                orderedKinds,
                normalized,
                receipts);
        catalog = new ResourceEffectCatalog(
            orderedKinds,
            normalized,
            new ResourceEffectCatalogReceipt(Hash(semanticContent), receipts));
        return null;
    }

    static ResourceEffectCatalogOutcome? FindConflict(
        ImmutableArray<NormalizedResourceEffectDeclaration> declarations)
    {
        for (int firstIndex = 0; firstIndex < declarations.Length; firstIndex++)
        {
            NormalizedResourceEffectDeclaration first = declarations[firstIndex];
            for (int secondIndex = firstIndex + 1;
                 secondIndex < declarations.Length;
                 secondIndex++)
            {
                NormalizedResourceEffectDeclaration second = declarations[secondIndex];
                if (!TargetsCanOverlap(first.Target, second.Target))
                    continue;
                if (TerminalConflict(first.Effect, second.Effect)
                    || EntryConflict(first.Effect, second.Effect)
                    || OperationConflict(
                        first.Effect,
                        first.Target,
                        second.Effect,
                        second.Target))
                {
                    ImmutableArray<ResourceDeclarationProvenance> provenances =
                        [.. first.Provenances
                            .Concat(second.Provenances)
                            .Distinct()
                            .OrderBy(ResourceEffectCanonicalizer.Provenance, StringComparer.Ordinal)];
                    var diagnostic = new ResourceEffectDiagnostic(
                        ResourceEffectDiagnosticKind.ConflictingDeclaration,
                        0,
                        0,
                        new InertString(
                            TextPolicy.Field,
                            "Satisfiable declarations prescribe contradictory effects for one operation occurrence."));
                    return new ResourceEffectCatalogOutcome.Rejected(
                        [.. provenances.Select(provenance =>
                            new ResourceEffectModelDiagnostic(
                                provenance.Model,
                                provenance,
                                diagnostic))]);
                }
            }
        }
        return null;
    }

    static bool TerminalConflict(
        ResourceEffect first,
        ResourceEffect second)
    {
        TerminalEffect? left = Terminal(first);
        TerminalEffect? right = Terminal(second);
        if (left is null || right is null)
            return false;
        if (left.Source != right.Source || !KindsOverlap(left.Kind, right.Kind))
            return false;
        if (!CompletionsOverlap(left.When, right.When))
            return false;
        return left.Transition != right.Transition;
    }

    static TerminalEffect? Terminal(ResourceEffect effect)
        => effect switch
        {
            ResourceEffect.Move move => new(
                move.Source,
                move.Kind,
                move.When,
                ResourceEffectCanonicalizer.Transition(move)),
            ResourceEffect.Release release => new(
                release.Source,
                release.Kind,
                release.When,
                ResourceEffectCanonicalizer.Transition(release)),
            ResourceEffect.Accept accept => new(
                accept.Source,
                accept.Kind,
                accept.When,
                ResourceEffectCanonicalizer.Transition(accept)),
            _ => null,
        };

    static bool OperationConflict(
        ResourceEffect first,
        ResourceEffectTargetSelector firstTarget,
        ResourceEffect second,
        ResourceEffectTargetSelector secondTarget)
    {
        if (first is not ResourceEffect.Operation left
            || second is not ResourceEffect.Operation right)
        {
            return false;
        }
        if (left.Boundary == right.Boundary && left.Throws == right.Throws)
            return false;
        return GuardsOverlap(
            left.Guard,
            firstTarget,
            right.Guard,
            secondTarget);
    }

    static bool EntryConflict(ResourceEffect first, ResourceEffect second)
    {
        EntryEffect? left = Entry(first);
        EntryEffect? right = Entry(second);
        if (left is null || right is null)
            return false;
        if (left.Source != right.Source || !KindsOverlap(left.Kind, right.Kind))
            return false;
        if (left.IsBorrow || right.IsBorrow)
            return left.IsBorrow != right.IsBorrow;
        return left.Transition != right.Transition;
    }

    static EntryEffect? Entry(ResourceEffect effect)
        => effect switch
        {
            ResourceEffect.Borrow borrow => new(
                borrow.Source,
                borrow.Kind,
                IsBorrow: true,
                Transition: ""),
            ResourceEffect.Consume consume => new(
                consume.Source,
                consume.Kind,
                IsBorrow: false,
                Transition:
                    "consume:"
                    + ResourceEffectCanonicalizer.Location(consume.Target)),
            ResourceEffect.Move
                {
                    When: ResourceEffectCompletion.Entry,
                } move => new(
                    move.Source,
                    move.Kind,
                    IsBorrow: false,
                    ResourceEffectCanonicalizer.Transition(move)),
            ResourceEffect.Release
                {
                    When: ResourceEffectCompletion.Entry,
                } release => new(
                    release.Source,
                    release.Kind,
                    IsBorrow: false,
                    ResourceEffectCanonicalizer.Transition(release)),
            ResourceEffect.Accept
                {
                    When: ResourceEffectCompletion.Entry,
                } accept => new(
                    accept.Source,
                    accept.Kind,
                    IsBorrow: false,
                    ResourceEffectCanonicalizer.Transition(accept)),
            _ => null,
        };

    static bool TargetsCanOverlap(
        ResourceEffectTargetSelector first,
        ResourceEffectTargetSelector second)
        => (first, second) switch
        {
            (ResourceEffectTargetSelector.Type left,
                ResourceEffectTargetSelector.Type right) =>
                TypeExpressionsCanUnify(left.Selector, right.Selector),
            (ResourceEffectTargetSelector.Member left,
                ResourceEffectTargetSelector.Member right) =>
                MembersCanOverlap(left.Selector, right.Selector),
            _ => false,
        };

    static bool MembersCanOverlap(
        ResourceEffectMemberSelector first,
        ResourceEffectMemberSelector second)
    {
        if (first.MetadataName != second.MetadataName
            || first.Kind != second.Kind
            || first.IsStatic != second.IsStatic
            || first.GenericArity != second.GenericArity
            || first.CallingConvention != second.CallingConvention
            || first.HasThis != second.HasThis
            || first.ExplicitThis != second.ExplicitThis
            || first.Parameters.Length != second.Parameters.Length)
        {
            return false;
        }
        for (int index = 0; index < first.Parameters.Length; index++)
        {
            if (first.Parameters[index].RefKind != second.Parameters[index].RefKind)
                return false;
        }
        return TypeExpressionPairsCanUnify(
            [
                (first.DeclaringType, second.DeclaringType),
                .. first.Parameters
                    .Zip(second.Parameters)
                    .Select(pair => (pair.First.Type, pair.Second.Type)),
                (first.ReturnType, second.ReturnType),
            ]);
    }

    static bool KindsOverlap(ResourceKindReference? first, ResourceKindReference? second)
        => first is null
            || second is null
            || (first.Identity == second.Identity
                && first.Arguments.Length == second.Arguments.Length);

    static bool CompletionsOverlap(
        ResourceEffectCompletion first,
        ResourceEffectCompletion second)
    {
        if (first is ResourceEffectCompletion.Entry
            || second is ResourceEffectCompletion.Entry)
            return true;
        if (first is ResourceEffectCompletion.ExceptionalExit
            || second is ResourceEffectCompletion.ExceptionalExit)
        {
            return first is ResourceEffectCompletion.ExceptionalExit
                && second is ResourceEffectCompletion.ExceptionalExit;
        }
        if (first is ResourceEffectCompletion.ResolvedOutcome firstOutcome
            && second is ResourceEffectCompletion.ResolvedOutcome secondOutcome)
        {
            return !OutcomeTestsDisjoint(
                firstOutcome.Source,
                firstOutcome.Test,
                secondOutcome.Source,
                secondOutcome.Test);
        }
        return true;
    }

    static bool OutcomeTestsDisjoint(
        ResourceEffectLocation firstSource,
        ResourceEffectOutcomeTest firstTest,
        ResourceEffectLocation secondSource,
        ResourceEffectOutcomeTest secondTest)
    {
        if (firstSource != secondSource)
            return false;
        return (firstTest, secondTest) switch
        {
            (ResourceEffectOutcomeTest.Boolean left, ResourceEffectOutcomeTest.Boolean right) =>
                left.Value != right.Value,
            (ResourceEffectOutcomeTest.Null, ResourceEffectOutcomeTest.NonNull)
                or (ResourceEffectOutcomeTest.NonNull, ResourceEffectOutcomeTest.Null) => true,
            (ResourceEffectOutcomeTest.Enum left, ResourceEffectOutcomeTest.Enum right) =>
                left.Value != right.Value,
            (ResourceEffectOutcomeTest.ExactType left, ResourceEffectOutcomeTest.ExactType right) =>
                left.Selector != right.Selector,
            _ => false,
        };
    }

    static bool GuardsOverlap(
        ResourceEffectGuard? first,
        ResourceEffectTargetSelector firstTarget,
        ResourceEffectGuard? second,
        ResourceEffectTargetSelector secondTarget)
    {
        if (first is null || second is null)
            return true;
        var left = (ResourceEffectGuard.ExactRuntimeType)first;
        var right = (ResourceEffectGuard.ExactRuntimeType)second;
        if (left.Subject != right.Subject)
            return true;
        ResourceTypeExpression? leftExpected =
            ExpectedType(firstTarget, left.Expected);
        ResourceTypeExpression? rightExpected =
            ExpectedType(secondTarget, right.Expected);
        return leftExpected is null
            || rightExpected is null
            || TypeExpressionsCanUnify(leftExpected, rightExpected);
    }

    static bool TypeExpressionsCanUnify(
        ResourceTypeExpression first,
        ResourceTypeExpression second)
        => TypeExpressionPairsCanUnify([(first, second)]);

    static bool TypeExpressionPairsCanUnify(
        IEnumerable<(ResourceTypeExpression First, ResourceTypeExpression Second)> pairs)
    {
        var substitutions =
            new Dictionary<ResourceEffectGenericVariable, ResourceTypeExpression>();
        foreach ((ResourceTypeExpression first, ResourceTypeExpression second) in pairs)
        {
            if (!Unify(first, second))
                return false;
        }
        return true;

        bool Unify(ResourceTypeExpression left, ResourceTypeExpression right)
        {
            left = Resolve(left);
            right = Resolve(right);
            if (left is ResourceTypeExpression.Variable leftVariable)
                return Bind(leftVariable.Value, right);
            if (right is ResourceTypeExpression.Variable rightVariable)
                return Bind(rightVariable.Value, left);
            return (left, right) switch
            {
                (ResourceTypeExpression.Named leftNamed,
                    ResourceTypeExpression.Named rightNamed) =>
                    AssemblySelectorsCanOverlap(
                        leftNamed.Assembly,
                        rightNamed.Assembly)
                    && leftNamed.Namespace == rightNamed.Namespace
                    && leftNamed.Segments.SequenceEqual(rightNamed.Segments)
                    && leftNamed.Arguments.Length == rightNamed.Arguments.Length
                    && leftNamed.Arguments
                        .Zip(rightNamed.Arguments)
                        .All(pair => Unify(pair.First, pair.Second)),
                (ResourceTypeExpression.SzArray leftArray,
                    ResourceTypeExpression.SzArray rightArray) =>
                    Unify(leftArray.Element, rightArray.Element),
                (ResourceTypeExpression.Array leftArray,
                    ResourceTypeExpression.Array rightArray) =>
                    leftArray.Rank == rightArray.Rank
                    && Unify(leftArray.Element, rightArray.Element),
                (ResourceTypeExpression.ByReference leftReference,
                    ResourceTypeExpression.ByReference rightReference) =>
                    Unify(leftReference.Element, rightReference.Element),
                (ResourceTypeExpression.Pointer leftPointer,
                    ResourceTypeExpression.Pointer rightPointer) =>
                    Unify(leftPointer.Element, rightPointer.Element),
                _ => false,
            };
        }

        ResourceTypeExpression Resolve(ResourceTypeExpression expression)
        {
            var seen = new HashSet<ResourceEffectGenericVariable>();
            while (expression is ResourceTypeExpression.Variable variable
                   && substitutions.TryGetValue(variable.Value, out ResourceTypeExpression? replacement)
                   && seen.Add(variable.Value))
            {
                expression = replacement;
            }
            return expression;
        }

        bool Bind(
            ResourceEffectGenericVariable variable,
            ResourceTypeExpression expression)
        {
            expression = Resolve(expression);
            if (expression is ResourceTypeExpression.Variable other
                && other.Value == variable)
            {
                return true;
            }
            if (Occurs(variable, expression))
                return false;
            substitutions[variable] = expression;
            return true;
        }

        bool Occurs(
            ResourceEffectGenericVariable variable,
            ResourceTypeExpression expression)
        {
            expression = Resolve(expression);
            return expression switch
            {
                ResourceTypeExpression.Variable candidate =>
                    candidate.Value == variable,
                ResourceTypeExpression.Named named =>
                    named.Arguments.Any(argument => Occurs(variable, argument)),
                ResourceTypeExpression.SzArray array =>
                    Occurs(variable, array.Element),
                ResourceTypeExpression.Array array =>
                    Occurs(variable, array.Element),
                ResourceTypeExpression.ByReference reference =>
                    Occurs(variable, reference.Element),
                ResourceTypeExpression.Pointer pointer =>
                    Occurs(variable, pointer.Element),
                _ => false,
            };
        }
    }

    static bool AssemblySelectorsCanOverlap(
        ResourceAssemblySelector first,
        ResourceAssemblySelector second)
    {
        if (first.SimpleName != second.SimpleName
            && !first.AllowCoreLibraryFacade
            && !second.AllowCoreLibraryFacade)
        {
            return false;
        }
        if (first.PublicKeyToken is not null
            && second.PublicKeyToken is not null
            && first.PublicKeyToken != second.PublicKeyToken)
        {
            return false;
        }
        return first.Version.Kind == ResourceAssemblyVersionPolicyKind.Any
            || second.Version.Kind == ResourceAssemblyVersionPolicyKind.Any
            || first.Version.Version == second.Version.Version;
    }

    static ResourceTypeExpression? ExpectedType(
        ResourceEffectTargetSelector target,
        ResourceEffectSignatureLocation location)
    {
        if (target is not ResourceEffectTargetSelector.Member member)
            return null;
        return location switch
        {
            ResourceEffectSignatureLocation.Receiver => member.Selector.DeclaringType,
            ResourceEffectSignatureLocation.Return => member.Selector.ReturnType,
            ResourceEffectSignatureLocation.Parameter parameter
                when parameter.Index < member.Selector.Parameters.Length =>
                    member.Selector.Parameters[parameter.Index].Type,
            _ => null,
        };
    }

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
            ResourceEffectLocation.CallbackParameter callback =>
                callback.CallbackIndex < member.Parameters.Length,
            ResourceEffectLocation.CallbackReturn callback =>
                callback.CallbackIndex < member.Parameters.Length,
            ResourceEffectLocation.Field field =>
                ValidateLocationShape(member, field.Root),
            ResourceEffectLocation.ResolvedField field =>
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
            if (completion is ResourceEffectCompletion.ResolvedOutcome outcome)
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

    static ResourceEffectCatalogOutcome Reject(
        ResourceEffectModelIdentity? model,
        ResourceDeclarationProvenance? provenance,
        ResourceEffectDiagnosticKind kind,
        int offset,
        int length,
        string message)
        => new ResourceEffectCatalogOutcome.Rejected(
            [new ResourceEffectModelDiagnostic(
                model,
                provenance,
                new ResourceEffectDiagnostic(
                    kind,
                    offset,
                    length,
                    new InertString(TextPolicy.Field, message)))]);

    static ResourceEffectCatalogOutcome RejectAll(
        IEnumerable<ResourceDeclarationProvenance> provenances,
        ResourceEffectDiagnosticKind kind,
        string message)
    {
        var diagnostic = new ResourceEffectDiagnostic(
            kind,
            0,
            0,
            new InertString(TextPolicy.Field, message));
        return new ResourceEffectCatalogOutcome.Rejected(
            [.. provenances
                .Distinct()
                .OrderBy(ResourceEffectCanonicalizer.Provenance, StringComparer.Ordinal)
                .Select(provenance =>
                    new ResourceEffectModelDiagnostic(
                        provenance.Model,
                        provenance,
                        diagnostic))]);
    }

    static ResourceEffectCatalogOutcome Limit(
        ResourceEffectWorkLimitKind kind,
        int limit,
        int required,
        ResourceEffectModelIdentity? model = null,
        ResourceDeclarationProvenance? provenance = null,
        int offset = 0)
        => new ResourceEffectCatalogOutcome.WorkLimitExceeded(
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
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    internal sealed record ParsedDeclaration(
        ResourceEffectTargetSelector Target,
        ResourceEffect Effect,
        ResourceDeclarationProvenance Provenance,
        int SourceLength);

    sealed record ParsedModel(
        ResourceEffectModelIdentity Identity,
        ResourceEffectLanguageIdentity Language,
        ImmutableArray<ResourceKindDefinition> ResourceKinds,
        ImmutableArray<ParsedDeclaration> Declarations,
        ResourceEffectModelReceipt Receipt);

    sealed record ScopedLocal(string Target, ResourceEffectLocalIdentity Identity);
    sealed record ScopedIndex(string Target, int Index);
    sealed record EntryEffect(
        ResourceEffectLocation Source,
        ResourceKindReference? Kind,
        bool IsBorrow,
        string Transition);
    sealed record TerminalEffect(
        ResourceEffectLocation Source,
        ResourceKindReference? Kind,
        ResourceEffectCompletion When,
        string Transition);

    sealed class SemanticDeclarationKey : IEquatable<SemanticDeclarationKey>
    {
        public SemanticDeclarationKey(
            ResourceEffectTargetSelector target,
            ResourceEffect effect)
        {
            Target = target;
            Effect = effect;
        }

        public ResourceEffectTargetSelector Target { get; }
        public ResourceEffect Effect { get; }

        public bool Equals(SemanticDeclarationKey? other)
            => other is not null && Target == other.Target && Effect == other.Effect;

        public override bool Equals(object? obj)
            => obj is SemanticDeclarationKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Target, Effect);
    }
}

static class ResourceEffectCanonicalizer
{
    public static string ModelContent(
        ImmutableArray<ResourceKindDefinition> definitions,
        ImmutableArray<ResourceEffectCatalogBuilder.ParsedDeclaration> declarations)
        => Node(
            "model-content",
            List(
                "resource-kinds",
                definitions.Select(ResourceKind)),
            List(
                "declarations",
                declarations
                    .Select(declaration =>
                        Declaration(declaration.Target, declaration.Effect))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)));

    public static string CatalogContent(
        ImmutableArray<ResourceKindDefinition> definitions,
        ImmutableArray<NormalizedResourceEffectDeclaration> declarations,
        ImmutableArray<ResourceEffectModelReceipt> receipts)
        => Node(
            "catalog-content",
            List("resource-kinds", definitions.Select(ResourceKind)),
            List(
                "declarations",
                declarations.Select(declaration =>
                    Declaration(declaration.Target, declaration.Effect))),
            List(
                "models",
                receipts.Select(receipt =>
                    Node(
                        "model",
                        Atom("identity", receipt.Identity.Value),
                        Atom("language", receipt.Language.Value),
                        Atom("content-hash", receipt.ContentHash)))));

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

    public static string Transition(ResourceEffect effect)
        => effect switch
        {
            ResourceEffect.Move move =>
                Node("transition-move", Location(move.Target)),
            ResourceEffect.Release release =>
                Node(
                    "transition-release",
                    OptionalLocation(release.Correspondence),
                    OptionalLocation(release.Observation)),
            ResourceEffect.Accept accept =>
                Node("transition-accept", Location(accept.Target)),
            _ => throw new ArgumentException("The effect is not terminal.", nameof(effect)),
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
            ResourceEffectLocation.ResolvedField field =>
                Node(
                    "location-resolved-field",
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
            ResourceEffectCompletion.ResolvedOutcome outcome =>
                Node(
                    "completion-resolved-outcome",
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
