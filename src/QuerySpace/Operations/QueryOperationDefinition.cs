using QuerySpace;

namespace QuerySpace.Operations;

public sealed class QueryOperationDefinition<TPredicate, TPlan>
{
    private readonly IReadOnlyDictionary<
        string,
        QueryOperationTermRegistration<TPredicate>> _termBindings;
    private readonly IReadOnlyDictionary<
        string,
        QueryOperationOrderRegistration> _orderBindings;
    private readonly IReadOnlyDictionary<
        string,
        QueryOperationProfile> _profiles;
    private readonly IReadOnlySet<string> _subjectRoleSet;
    private readonly IReadOnlySet<string> _resultGrainSet;
    private readonly IReadOnlySet<string> _rowSetSet;

    private QueryOperationDefinition(
        string identity,
        PortableQueryVocabulary<TPredicate, TPlan> vocabulary,
        IReadOnlyList<string> subjectRoles,
        IReadOnlyList<string> resultGrains,
        IReadOnlyList<string> rowSets,
        IReadOnlyList<QueryOperationTermBinding> termBindings,
        IReadOnlyList<QueryOperationOrderBinding> orderBindings,
        IReadOnlyList<QueryOperationProfile> profiles,
        IReadOnlyDictionary<
            string,
            QueryOperationTermRegistration<TPredicate>> termBindingsByIdentity,
        IReadOnlyDictionary<
            string,
            QueryOperationOrderRegistration> orderBindingsByIdentity,
        IReadOnlyDictionary<
            string,
            QueryOperationProfile> profilesByIdentity)
    {
        Identity = identity;
        Vocabulary = vocabulary;
        SubjectRoles = subjectRoles;
        ResultGrains = resultGrains;
        RowSets = rowSets;
        TermBindings = termBindings;
        OrderBindings = orderBindings;
        Profiles = profiles;
        _termBindings = termBindingsByIdentity;
        _orderBindings = orderBindingsByIdentity;
        _profiles = profilesByIdentity;
        _subjectRoleSet = subjectRoles.ToHashSet(StringComparer.Ordinal);
        _resultGrainSet = resultGrains.ToHashSet(StringComparer.Ordinal);
        _rowSetSet = rowSets.ToHashSet(StringComparer.Ordinal);
    }

    public string Identity { get; }

    public PortableQueryVocabulary<TPredicate, TPlan> Vocabulary { get; }

    public IReadOnlyList<string> SubjectRoles { get; }

    public IReadOnlyList<string> ResultGrains { get; }

    public IReadOnlyList<string> RowSets { get; }

    public IReadOnlyList<QueryOperationTermBinding> TermBindings { get; }

    public IReadOnlyList<QueryOperationOrderBinding> OrderBindings { get; }

    public IReadOnlyList<QueryOperationProfile> Profiles { get; }

    public static QueryOperationDefinition<TPredicate, TPlan> Create(
        string identity,
        PortableQueryVocabulary<TPredicate, TPlan> vocabulary,
        IReadOnlyList<string> subjectRoles,
        IReadOnlyList<string> resultGrains,
        IReadOnlyList<string> rowSets,
        IReadOnlyList<QueryOperationTermBinding> termBindings,
        IReadOnlyList<QueryOperationOrderBinding> orderBindings,
        IReadOnlyList<QueryOperationProfile> profiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(vocabulary);
        ValidateDefaultRanking(vocabulary);

        IReadOnlyList<string> subjectRoleCopy =
            QueryOperationContract.CopyIdentities(
                subjectRoles,
                nameof(subjectRoles),
                requireAny: true);
        IReadOnlyList<string> resultGrainCopy =
            QueryOperationContract.CopyIdentities(
                resultGrains,
                nameof(resultGrains),
                requireAny: true);
        IReadOnlyList<string> rowSetCopy =
            QueryOperationContract.CopyIdentities(
                rowSets,
                nameof(rowSets),
                requireAny: false);
        IReadOnlyList<QueryOperationTermBinding> termBindingCopy =
            QueryOperationContract.Copy(
                termBindings,
                nameof(termBindings));
        IReadOnlyList<QueryOperationOrderBinding> orderBindingCopy =
            QueryOperationContract.Copy(
                orderBindings,
                nameof(orderBindings));
        IReadOnlyList<QueryOperationProfile> profileCopy =
            QueryOperationContract.Copy(
                profiles,
                nameof(profiles));

        var subjectRoleSet =
            subjectRoleCopy.ToHashSet(StringComparer.Ordinal);
        var resultGrainSet =
            resultGrainCopy.ToHashSet(StringComparer.Ordinal);
        var rowSetSet =
            rowSetCopy.ToHashSet(StringComparer.Ordinal);

        var termBindingsByIdentity =
            new Dictionary<
                string,
                QueryOperationTermRegistration<TPredicate>>(
                StringComparer.Ordinal);
        foreach (QueryOperationTermBinding binding in termBindingCopy)
        {
            binding.Applicability.ValidateAgainst(
                subjectRoleSet,
                resultGrainSet,
                rowSetSet,
                nameof(termBindings));
            if (!vocabulary.TryGetKey(
                    binding.Key,
                    out PortableQueryKeyDeclaration<TPredicate>? declaration))
            {
                throw new ArgumentException(
                    $"Query term binding '{binding.Identity}' names unknown key "
                    + $"'{binding.Key}'.",
                    nameof(termBindings));
            }

            PortableQueryOperator[] operators =
            [
                .. Enum.GetValues<PortableQueryOperator>()
                    .Where(declaration.AdmitsOperator),
            ];
            if (operators.Length == 0)
            {
                throw new ArgumentException(
                    $"Query term binding '{binding.Identity}' names key "
                    + $"'{binding.Key}', which admits no operators.",
                    nameof(termBindings));
            }

            ValidateKnownValues(
                binding,
                declaration,
                operators,
                nameof(termBindings));
            ValidateEffects(
                vocabulary,
                binding.Identity,
                binding.Effects,
                nameof(termBindings));
            if (!termBindingsByIdentity.TryAdd(
                    binding.Identity,
                    new(
                        binding,
                        declaration,
                        Array.AsReadOnly(operators))))
            {
                throw new ArgumentException(
                    $"Query term binding identity '{binding.Identity}' is duplicated.",
                    nameof(termBindings));
            }
        }

        var orderBindingsByIdentity =
            new Dictionary<
                string,
                QueryOperationOrderRegistration>(
                StringComparer.Ordinal);
        foreach (QueryOperationOrderBinding binding in orderBindingCopy)
        {
            binding.Applicability.ValidateAgainst(
                subjectRoleSet,
                resultGrainSet,
                rowSetSet,
                nameof(orderBindings));
            IReadOnlyList<QueryOperationOrderRole> roles =
                ResolveOrderRoles(
                    vocabulary,
                    binding,
                    nameof(orderBindings));
            ValidateEffects(
                vocabulary,
                binding.Identity,
                binding.Effects,
                nameof(orderBindings));
            if (!orderBindingsByIdentity.TryAdd(
                    binding.Identity,
                    new(binding, roles)))
            {
                throw new ArgumentException(
                    $"Query order binding identity '{binding.Identity}' is duplicated.",
                    nameof(orderBindings));
            }
        }

        var profilesByIdentity =
            new Dictionary<string, QueryOperationProfile>(
                StringComparer.Ordinal);
        foreach (QueryOperationProfile profile in profileCopy)
        {
            QueryOperationContract.ValidateKnown(
                profile.TermBindings,
                termBindingsByIdentity.Keys.ToHashSet(
                    StringComparer.Ordinal),
                "query term binding",
                nameof(profiles));
            QueryOperationContract.ValidateKnown(
                profile.OrderBindings,
                orderBindingsByIdentity.Keys.ToHashSet(
                    StringComparer.Ordinal),
                "query order binding",
                nameof(profiles));
            if (!profilesByIdentity.TryAdd(
                    profile.Identity,
                    profile))
            {
                throw new ArgumentException(
                    $"Query profile identity '{profile.Identity}' is duplicated.",
                    nameof(profiles));
            }
        }

        return new(
            identity,
            vocabulary,
            subjectRoleCopy,
            resultGrainCopy,
            rowSetCopy,
            termBindingCopy,
            orderBindingCopy,
            profileCopy,
            termBindingsByIdentity,
            orderBindingsByIdentity,
            profilesByIdentity);
    }

    internal bool OwnsSubjectRole(string identity) =>
        _subjectRoleSet.Contains(identity);

    internal bool OwnsResultGrain(string identity) =>
        _resultGrainSet.Contains(identity);

    internal bool OwnsRowSet(string identity) =>
        _rowSetSet.Contains(identity);

    internal QueryOperationProfile GetProfile(string identity) =>
        _profiles.TryGetValue(
            identity,
            out QueryOperationProfile? profile)
            ? profile
            : throw new ArgumentException(
                $"Operation '{Identity}' declares no query profile '{identity}'.",
                nameof(identity));

    internal QueryOperationTermRegistration<TPredicate>
        GetTermBinding(string identity) =>
        _termBindings[identity];

    internal QueryOperationOrderRegistration
        GetOrderBinding(string identity) =>
        _orderBindings[identity];

    private static IReadOnlyList<QueryOperationOrderRole>
        ResolveOrderRoles(
            PortableQueryVocabulary<TPredicate, TPlan> vocabulary,
            QueryOperationOrderBinding binding,
            string parameterName)
    {
        if (binding.Kind is QueryOperationOrderKind.Named)
        {
            if (!vocabulary.TryGetNamedOrder(
                    binding.Reference,
                    out PortableQueryOrderPurpose purpose))
            {
                throw new ArgumentException(
                    $"Query order binding '{binding.Identity}' names unknown "
                    + $"order '{binding.Reference}'.",
                    parameterName);
            }

            return purpose is PortableQueryOrderPurpose.Ranking
                ? Array.AsReadOnly(
                    new[]
                    {
                        QueryOperationOrderRole.Baseline,
                        QueryOperationOrderRole.Ranking,
                    })
                : Array.AsReadOnly(
                    new[]
                    {
                        QueryOperationOrderRole.Baseline,
                    });
        }

        if (!vocabulary.TryGetKey(binding.Reference, out _))
        {
            throw new ArgumentException(
                $"Query order binding '{binding.Identity}' names unknown field "
                + $"'{binding.Reference}'.",
                parameterName);
        }

        if (!vocabulary.IsOrderable(binding.Reference))
        {
            throw new ArgumentException(
                $"Query order binding '{binding.Identity}' names field "
                + $"'{binding.Reference}', which is not orderable.",
                parameterName);
        }

        return Array.AsReadOnly(
            new[]
            {
                QueryOperationOrderRole.Baseline,
                QueryOperationOrderRole.Ranking,
            });
    }

    private static void ValidateKnownValues(
        QueryOperationTermBinding binding,
        PortableQueryKeyDeclaration<TPredicate> declaration,
        IReadOnlyList<PortableQueryOperator> operators,
        string parameterName)
    {
        foreach (string value in binding.Description.Values)
        {
            foreach (PortableQueryOperator @operator in operators)
            {
                if (!declaration.Bind(
                        @operator,
                        value).IsBound)
                {
                    throw new ArgumentException(
                        $"Query term binding '{binding.Identity}' advertises "
                        + $"value '{value}', which its binder rejects for "
                        + $"operator '{@operator}'.",
                        parameterName);
                }
            }
        }
    }

    private static void ValidateDefaultRanking(
        PortableQueryVocabulary<TPredicate, TPlan> vocabulary)
    {
        string? defaultRanking = vocabulary.DefaultRanking;
        if (defaultRanking is null)
        {
            return;
        }

        if (!vocabulary.TryGetNamedOrder(
                defaultRanking,
                out PortableQueryOrderPurpose purpose))
        {
            throw new ArgumentException(
                $"Vocabulary '{vocabulary.Identity}' declares unknown default "
                + $"ranking '{defaultRanking}'.",
                nameof(vocabulary));
        }

        if (purpose is not PortableQueryOrderPurpose.Ranking)
        {
            throw new ArgumentException(
                $"Vocabulary '{vocabulary.Identity}' declares default ranking "
                + $"'{defaultRanking}', which is not a ranking order.",
                nameof(vocabulary));
        }
    }

    private static void ValidateEffects(
        PortableQueryVocabulary<TPredicate, TPlan> vocabulary,
        string bindingIdentity,
        IReadOnlyList<QueryOperationEffect> effects,
        string parameterName)
    {
        foreach (QueryOperationEffect effect in effects)
        {
            if (effect.Kind
                    is QueryOperationEffectKind.WorkDimension
                && !vocabulary.TryGetDimension(
                    effect.Identity,
                    out _))
            {
                throw new ArgumentException(
                    $"Query binding '{bindingIdentity}' names unknown work "
                    + $"dimension effect '{effect.Identity}'.",
                    parameterName);
            }
        }
    }
}

internal sealed record QueryOperationTermRegistration<TPredicate>(
    QueryOperationTermBinding Binding,
    PortableQueryKeyDeclaration<TPredicate> Declaration,
    IReadOnlyList<PortableQueryOperator> Operators);

internal sealed record QueryOperationOrderRegistration(
    QueryOperationOrderBinding Binding,
    IReadOnlyList<QueryOperationOrderRole> Roles);
