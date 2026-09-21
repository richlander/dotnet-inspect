using System.Diagnostics.CodeAnalysis;
using QuerySpace;
using QuerySpace.Rows;

namespace DotnetInspector.QueryOperations;

public sealed class QueryOperationTermCapability
{
    internal QueryOperationTermCapability(
        QueryOperationTermBinding binding,
        IReadOnlyList<PortableQueryOperator> operators)
    {
        Binding = binding;
        Operators = operators;
    }

    public QueryOperationTermBinding Binding { get; }

    public IReadOnlyList<PortableQueryOperator> Operators { get; }
}

public sealed class QueryOperationOrderCapability
{
    internal QueryOperationOrderCapability(
        QueryOperationOrderBinding binding,
        IReadOnlyList<QueryOperationOrderRole> roles)
    {
        Binding = binding;
        Roles = roles;
        Directions = Array.AsReadOnly(
            new[]
            {
                PortableQueryDirection.Ascending,
                PortableQueryDirection.Descending,
            });
    }

    public QueryOperationOrderBinding Binding { get; }

    public IReadOnlyList<QueryOperationOrderRole> Roles { get; }

    public IReadOnlyList<PortableQueryDirection> Directions { get; }
}

public sealed class QueryOperationRouteCapabilities
{
    internal QueryOperationRouteCapabilities(
        string vocabulary,
        IReadOnlyList<QueryOperationTermCapability> terms,
        IReadOnlyList<QueryOperationOrderCapability> orders,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RowSelectionStageKind> stages)
    {
        Vocabulary = vocabulary;
        Terms = terms;
        Orders = orders;
        Dimensions = dimensions;
        Stages = stages;
    }

    public string Vocabulary { get; }

    public IReadOnlyList<QueryOperationTermCapability> Terms { get; }

    public IReadOnlyList<QueryOperationOrderCapability> Orders { get; }

    public IReadOnlyList<string> Dimensions { get; }

    public IReadOnlyList<RowSelectionStageKind> Stages { get; }
}

public interface IQueryOperationRoute
{
    string Identity { get; }

    string OperationIdentity { get; }

    string SubjectRole { get; }

    string ResultGrain { get; }

    IReadOnlyList<string> RowSets { get; }

    string ProfileIdentity { get; }

    QueryOperationRouteCapabilities Capabilities { get; }
}

public sealed class QueryOperationRoute<TPredicate, TPlan>
    : IQueryOperationRoute
{
    private readonly PortableQueryVocabulary<TPredicate, TPlan>
        _profiledVocabulary;

    private QueryOperationRoute(
        string identity,
        QueryOperationDefinition<TPredicate, TPlan> operation,
        string subjectRole,
        string resultGrain,
        IReadOnlyList<string> rowSets,
        string profileIdentity,
        QueryOperationRouteCapabilities capabilities,
        PortableQueryVocabulary<TPredicate, TPlan> profiledVocabulary)
    {
        Identity = identity;
        Operation = operation;
        SubjectRole = subjectRole;
        ResultGrain = resultGrain;
        RowSets = rowSets;
        ProfileIdentity = profileIdentity;
        Capabilities = capabilities;
        _profiledVocabulary = profiledVocabulary;
    }

    public string Identity { get; }

    public QueryOperationDefinition<TPredicate, TPlan> Operation { get; }

    public string OperationIdentity => Operation.Identity;

    public string SubjectRole { get; }

    public string ResultGrain { get; }

    public IReadOnlyList<string> RowSets { get; }

    public string ProfileIdentity { get; }

    public QueryOperationRouteCapabilities Capabilities { get; }

    public static QueryOperationRoute<TPredicate, TPlan> Create(
        string identity,
        QueryOperationDefinition<TPredicate, TPlan> operation,
        string subjectRole,
        string resultGrain,
        IReadOnlyList<string> rowSets,
        string profileIdentity,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RowSelectionStageKind> stages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultGrain);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileIdentity);

        if (!operation.OwnsSubjectRole(subjectRole))
        {
            throw new ArgumentException(
                $"Operation '{operation.Identity}' declares no subject role "
                + $"'{subjectRole}'.",
                nameof(subjectRole));
        }

        if (!operation.OwnsResultGrain(resultGrain))
        {
            throw new ArgumentException(
                $"Operation '{operation.Identity}' declares no result grain "
                + $"'{resultGrain}'.",
                nameof(resultGrain));
        }

        IReadOnlyList<string> rowSetCopy =
            QueryOperationContract.CopyIdentities(
                rowSets,
                nameof(rowSets),
                requireAny: false);
        foreach (string rowSet in rowSetCopy)
        {
            if (!operation.OwnsRowSet(rowSet))
            {
                throw new ArgumentException(
                    $"Operation '{operation.Identity}' declares no row set "
                    + $"'{rowSet}'.",
                    nameof(rowSets));
            }
        }

        IReadOnlyList<string> dimensionCopy =
            QueryOperationContract.CopyIdentities(
                dimensions,
                nameof(dimensions),
                requireAny: false);
        foreach (string dimension in dimensionCopy)
        {
            if (!operation.Vocabulary.TryGetDimension(
                    dimension,
                    out _))
            {
                throw new ArgumentException(
                    $"Operation '{operation.Identity}' declares no work "
                    + $"dimension '{dimension}'.",
                    nameof(dimensions));
            }
        }

        IReadOnlyList<RowSelectionStageKind> stageCopy =
            CopyStages(stages);
        foreach (RowSelectionStageKind stage in stageCopy)
        {
            if (!operation.Vocabulary.AdmitsStageKind(stage))
            {
                throw new ArgumentException(
                    $"Operation '{operation.Identity}' does not admit stage "
                    + $"'{stage}'.",
                    nameof(stages));
            }
        }

        QueryOperationProfile profile =
            operation.GetProfile(profileIdentity);
        var selectedRowSets =
            rowSetCopy.ToHashSet(StringComparer.Ordinal);

        var terms =
            new QueryOperationTermCapability[
                profile.TermBindings.Count];
        var termKeys =
            new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0;
             index < profile.TermBindings.Count;
             index++)
        {
            QueryOperationTermRegistration<TPredicate> registration =
                operation.GetTermBinding(
                    profile.TermBindings[index]);
            ValidateApplicability(
                registration.Binding.Identity,
                registration.Binding.Applicability,
                subjectRole,
                resultGrain,
                selectedRowSets,
                nameof(profileIdentity));
            if (!termKeys.Add(registration.Binding.Key))
            {
                throw new ArgumentException(
                    $"Query profile '{profileIdentity}' exposes key "
                    + $"'{registration.Binding.Key}' more than once.",
                    nameof(profileIdentity));
            }

            terms[index] =
                new(
                    registration.Binding,
                    registration.Operators);
        }

        var orders =
            new QueryOperationOrderCapability[
                profile.OrderBindings.Count];
        var orderReferences =
            new HashSet<(
                QueryOperationOrderKind Kind,
                string Reference)>();
        for (int index = 0;
             index < profile.OrderBindings.Count;
             index++)
        {
            QueryOperationOrderRegistration registration =
                operation.GetOrderBinding(
                    profile.OrderBindings[index]);
            ValidateApplicability(
                registration.Binding.Identity,
                registration.Binding.Applicability,
                subjectRole,
                resultGrain,
                selectedRowSets,
                nameof(profileIdentity));
            if (!orderReferences.Add(
                    (registration.Binding.Kind,
                        registration.Binding.Reference)))
            {
                throw new ArgumentException(
                    $"Query profile '{profileIdentity}' exposes "
                    + $"{registration.Binding.Kind} order "
                    + $"'{registration.Binding.Reference}' more than once.",
                    nameof(profileIdentity));
            }

            orders[index] =
                new(
                    registration.Binding,
                    registration.Roles);
        }

        if (stageCopy.Contains(RowSelectionStageKind.Top)
            && !orders.Any(capability =>
                capability.Roles.Contains(
                    QueryOperationOrderRole.Ranking)))
        {
            throw new ArgumentException(
                $"Query route '{identity}' admits Top but its profile "
                + $"'{profileIdentity}' exposes no ranking order.",
                nameof(profileIdentity));
        }

        ValidateRequiredFamilies(
            operation,
            profile,
            nameof(profileIdentity));
        QueryOperationContract.ValidateKnown(
            operation.Vocabulary.RequiredDimensions,
            dimensionCopy.ToHashSet(StringComparer.Ordinal),
            "required work dimension",
            nameof(dimensions));
        ValidateEffectDimensions(
            terms.Select(capability =>
                capability.Binding.Effects)
                .Concat(orders.Select(capability =>
                    capability.Binding.Effects))
                .SelectMany(effects => effects),
            dimensionCopy,
            nameof(dimensions));

        IReadOnlyList<QueryOperationTermCapability> termCapabilities =
            Array.AsReadOnly(terms);
        IReadOnlyList<QueryOperationOrderCapability> orderCapabilities =
            Array.AsReadOnly(orders);
        var capabilities =
            new QueryOperationRouteCapabilities(
                operation.Vocabulary.Identity,
                termCapabilities,
                orderCapabilities,
                dimensionCopy,
                stageCopy);
        var profiledVocabulary =
            new ProfiledQueryOperationVocabulary<TPredicate, TPlan>(
                operation.Vocabulary,
                operation,
                profile,
                dimensionCopy,
                stageCopy);

        return new(
            identity,
            operation,
            subjectRole,
            resultGrain,
            rowSetCopy,
            profileIdentity,
            capabilities,
            profiledVocabulary);
    }

    public PortableQueryResolution<TPlan> Resolve(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default) =>
        PortableQueryResolver.Resolve(
            Capabilities.Vocabulary,
            _profiledVocabulary,
            intent,
            cancellationToken);

    private static IReadOnlyList<RowSelectionStageKind> CopyStages(
        IReadOnlyList<RowSelectionStageKind> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var seen = new HashSet<RowSelectionStageKind>();
        var copy = new RowSelectionStageKind[stages.Count];
        for (int index = 0; index < stages.Count; index++)
        {
            RowSelectionStageKind stage = stages[index];
            QueryOperationContract.ValidateDefined(
                stage,
                nameof(stages));
            if (!seen.Add(stage))
            {
                throw new ArgumentException(
                    $"Stage '{stage}' is duplicated.",
                    nameof(stages));
            }

            copy[index] = stage;
        }

        return Array.AsReadOnly(copy);
    }

    private static void ValidateApplicability(
        string bindingIdentity,
        QueryOperationApplicability applicability,
        string subjectRole,
        string resultGrain,
        IReadOnlySet<string> rowSets,
        string parameterName)
    {
        if (!applicability.AppliesTo(
                subjectRole,
                resultGrain,
                rowSets))
        {
            throw new ArgumentException(
                $"Query binding '{bindingIdentity}' does not apply to "
                + $"subject role '{subjectRole}', result grain "
                + $"'{resultGrain}', and the selected row sets.",
                parameterName);
        }
    }

    private static void ValidateRequiredFamilies(
        QueryOperationDefinition<TPredicate, TPlan> operation,
        QueryOperationProfile profile,
        string parameterName)
    {
        var families = new HashSet<string>(
            profile.TermBindings
                .Select(operation.GetTermBinding)
                .Select(registration =>
                    registration.Declaration.Family)
                .OfType<string>(),
            StringComparer.Ordinal);
        foreach (string family in
                 operation.Vocabulary.RequiredTermFamilies)
        {
            if (!families.Contains(family))
            {
                throw new ArgumentException(
                    $"Query profile '{profile.Identity}' cannot satisfy "
                    + $"required term family '{family}'.",
                    parameterName);
            }
        }
    }

    private static void ValidateEffectDimensions(
        IEnumerable<QueryOperationEffect> effects,
        IReadOnlyList<string> dimensions,
        string parameterName)
    {
        var available =
            dimensions.ToHashSet(StringComparer.Ordinal);
        foreach (QueryOperationEffect effect in effects)
        {
            if (effect.Kind
                    is QueryOperationEffectKind.WorkDimension
                && !available.Contains(effect.Identity))
            {
                throw new ArgumentException(
                    $"Query binding requires work dimension "
                    + $"'{effect.Identity}', which the route does not support.",
                    parameterName);
            }
        }
    }
}

internal sealed class ProfiledQueryOperationVocabulary<TPredicate, TPlan>
    : PortableQueryVocabulary<TPredicate, TPlan>
{
    private readonly PortableQueryVocabulary<TPredicate, TPlan> _inner;
    private readonly IReadOnlyDictionary<
        string,
        ProfiledQueryKeyDeclaration<TPredicate>> _keys;
    private readonly IReadOnlySet<string> _dimensions;
    private readonly IReadOnlySet<RowSelectionStageKind> _stages;
    private readonly IReadOnlySet<string> _namedOrders;
    private readonly IReadOnlySet<string> _fieldOrders;

    public ProfiledQueryOperationVocabulary(
        PortableQueryVocabulary<TPredicate, TPlan> inner,
        QueryOperationDefinition<TPredicate, TPlan> operation,
        QueryOperationProfile profile,
        IReadOnlyList<string> dimensions,
        IReadOnlyList<RowSelectionStageKind> stages)
    {
        _inner = inner;
        _dimensions =
            dimensions.ToHashSet(StringComparer.Ordinal);
        _stages = stages.ToHashSet();

        var keys = new Dictionary<
            string,
            (
                PortableQueryKeyDeclaration<TPredicate> Declaration,
                bool AdmitsTerms,
                bool AdmitsOrder)>(
                StringComparer.Ordinal);
        foreach (string bindingIdentity in profile.TermBindings)
        {
            QueryOperationTermRegistration<TPredicate> registration =
                operation.GetTermBinding(bindingIdentity);
            keys[registration.Binding.Key] =
                (registration.Declaration, true, false);
        }

        var namedOrders =
            new HashSet<string>(StringComparer.Ordinal);
        var fieldOrders =
            new HashSet<string>(StringComparer.Ordinal);
        foreach (string bindingIdentity in profile.OrderBindings)
        {
            QueryOperationOrderBinding binding =
                operation.GetOrderBinding(bindingIdentity).Binding;
            if (binding.Kind is QueryOperationOrderKind.Named)
            {
                namedOrders.Add(binding.Reference);
                continue;
            }

            fieldOrders.Add(binding.Reference);
            operation.Vocabulary.TryGetKey(
                binding.Reference,
                out PortableQueryKeyDeclaration<TPredicate>? declaration);
            if (keys.TryGetValue(
                    binding.Reference,
                    out var existing))
            {
                keys[binding.Reference] =
                    (existing.Declaration,
                        existing.AdmitsTerms,
                        true);
            }
            else
            {
                keys[binding.Reference] =
                    (declaration!, false, true);
            }
        }

        _keys = keys.ToDictionary(
            pair => pair.Key,
            pair => new ProfiledQueryKeyDeclaration<TPredicate>(
                pair.Value.Declaration,
                pair.Value.AdmitsTerms),
            StringComparer.Ordinal);
        _namedOrders = namedOrders;
        _fieldOrders = fieldOrders;
    }

    public override string Identity => _inner.Identity;

    public override IReadOnlyList<string> RequiredTermFamilies =>
        _inner.RequiredTermFamilies;

    public override IReadOnlyList<string> RequiredDimensions =>
        _inner.RequiredDimensions;

    public override string? DefaultRanking =>
        _inner.DefaultRanking is { } ranking
        && _namedOrders.Contains(ranking)
            ? ranking
            : null;

    public override bool CollapsesDuplicateBindings =>
        _inner.CollapsesDuplicateBindings;

    public override bool TryGetKey(
        string key,
        [NotNullWhen(true)]
        out PortableQueryKeyDeclaration<TPredicate>? declaration)
    {
        bool found = _keys.TryGetValue(
            key,
            out ProfiledQueryKeyDeclaration<TPredicate>? profiled);
        declaration = profiled;
        return found;
    }

    public override bool TryGetDimension(
        string dimension,
        [NotNullWhen(true)]
        out PortableQueryDimensionDeclaration<TPredicate>? declaration)
    {
        if (!_dimensions.Contains(dimension))
        {
            declaration = null;
            return false;
        }

        return _inner.TryGetDimension(
            dimension,
            out declaration);
    }

    public override bool AdmitsStageKind(
        RowSelectionStageKind kind) =>
        _stages.Contains(kind);

    public override bool TryGetNamedOrder(
        string reference,
        out PortableQueryOrderPurpose purpose)
    {
        if (!_namedOrders.Contains(reference))
        {
            purpose = default;
            return false;
        }

        return _inner.TryGetNamedOrder(
            reference,
            out purpose);
    }

    public override bool IsOrderable(string key) =>
        _fieldOrders.Contains(key)
        && _inner.IsOrderable(key);

    public override bool AreTermsCompatible(
        PortableQueryResolvedTerm<TPredicate> first,
        PortableQueryResolvedTerm<TPredicate> second) =>
        _inner.AreTermsCompatible(first, second);

    public override TPlan CreatePlan(
        PortableQueryResolvedIntent<TPredicate> resolved) =>
        _inner.CreatePlan(resolved);
}

internal sealed class ProfiledQueryKeyDeclaration<TPredicate>(
    PortableQueryKeyDeclaration<TPredicate> inner,
    bool admitsTerms)
    : PortableQueryKeyDeclaration<TPredicate>
{
    public override string Key => inner.Key;

    public override string? Family => inner.Family;

    public override PortableQueryFamilyKind FamilyKind =>
        inner.FamilyKind;

    public override bool AdmitsOperator(
        PortableQueryOperator @operator) =>
        admitsTerms
        && inner.AdmitsOperator(@operator);

    public override PortableQueryBinding<TPredicate> Bind(
        PortableQueryOperator @operator,
        string value) =>
        admitsTerms
            ? inner.Bind(@operator, value)
            : PortableQueryBinding<TPredicate>.Rejected;
}
