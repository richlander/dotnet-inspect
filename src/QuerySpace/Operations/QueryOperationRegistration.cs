using QuerySpace;

namespace QuerySpace.Operations;

public enum QueryOperationTermRole
{
    SubjectQualification,
    OperationSelector,
    ResultPredicate
}

public enum QueryOperationOrderKind
{
    Named,
    Field
}

public enum QueryOperationOrderRole
{
    Baseline,
    Ranking
}

public enum QueryOperationEffectKind
{
    Capability,
    AcquisitionTier,
    WorkDimension,
    Completion
}

public sealed record QueryOperationEffect
{
    public QueryOperationEffect(
        QueryOperationEffectKind kind,
        string identity)
    {
        QueryOperationContract.ValidateDefined(kind, nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Kind = kind;
        Identity = identity;
    }

    public QueryOperationEffectKind Kind { get; }

    public string Identity { get; }
}

public sealed class QueryOperationApplicability
{
    public QueryOperationApplicability(
        IReadOnlyList<string> subjectRoles,
        IReadOnlyList<string> resultGrains,
        IReadOnlyList<string> rowSets)
    {
        SubjectRoles = QueryOperationContract.CopyIdentities(
            subjectRoles,
            nameof(subjectRoles),
            requireAny: true);
        ResultGrains = QueryOperationContract.CopyIdentities(
            resultGrains,
            nameof(resultGrains),
            requireAny: true);
        RowSets = QueryOperationContract.CopyIdentities(
            rowSets,
            nameof(rowSets),
            requireAny: false);
    }

    public IReadOnlyList<string> SubjectRoles { get; }

    public IReadOnlyList<string> ResultGrains { get; }

    public IReadOnlyList<string> RowSets { get; }

    internal bool AppliesTo(
        string subjectRole,
        string resultGrain,
        IReadOnlySet<string> rowSets) =>
        SubjectRoles.Contains(subjectRole, StringComparer.Ordinal)
        && ResultGrains.Contains(resultGrain, StringComparer.Ordinal)
        && RowSets.All(rowSets.Contains);

    internal void ValidateAgainst(
        IReadOnlySet<string> subjectRoles,
        IReadOnlySet<string> resultGrains,
        IReadOnlySet<string> rowSets,
        string parameterName)
    {
        QueryOperationContract.ValidateKnown(
            SubjectRoles,
            subjectRoles,
            "subject role",
            parameterName);
        QueryOperationContract.ValidateKnown(
            ResultGrains,
            resultGrains,
            "result grain",
            parameterName);
        QueryOperationContract.ValidateKnown(
            RowSets,
            rowSets,
            "row set",
            parameterName);
    }
}

public sealed class QueryOperationTermDescription
{
    public QueryOperationTermDescription(
        string label,
        string valueKind,
        IReadOnlyList<string> values,
        string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueKind);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        Label = label;
        ValueKind = valueKind;
        Values = QueryOperationContract.CopyValues(
            values,
            nameof(values));
        Summary = summary;
    }

    public string Label { get; }

    public string ValueKind { get; }

    public IReadOnlyList<string> Values { get; }

    public string Summary { get; }
}

public sealed class QueryOperationOrderDescription
{
    public QueryOperationOrderDescription(
        string label,
        string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        Label = label;
        Summary = summary;
    }

    public string Label { get; }

    public string Summary { get; }
}

public sealed class QueryOperationTermBinding
{
    public QueryOperationTermBinding(
        string identity,
        string key,
        QueryOperationTermRole role,
        QueryOperationApplicability applicability,
        QueryOperationTermDescription description,
        IReadOnlyList<QueryOperationEffect> effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        QueryOperationContract.ValidateDefined(role, nameof(role));
        ArgumentNullException.ThrowIfNull(applicability);
        ArgumentNullException.ThrowIfNull(description);

        Identity = identity;
        Key = key;
        Role = role;
        Applicability = applicability;
        Description = description;
        Effects = QueryOperationContract.Copy(
            effects,
            nameof(effects));
    }

    public string Identity { get; }

    public string Key { get; }

    public QueryOperationTermRole Role { get; }

    public QueryOperationApplicability Applicability { get; }

    public QueryOperationTermDescription Description { get; }

    public IReadOnlyList<QueryOperationEffect> Effects { get; }
}

public sealed class QueryOperationOrderBinding
{
    public QueryOperationOrderBinding(
        string identity,
        QueryOperationOrderKind kind,
        string reference,
        QueryOperationApplicability applicability,
        QueryOperationOrderDescription description,
        IReadOnlyList<QueryOperationEffect> effects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        QueryOperationContract.ValidateDefined(kind, nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(applicability);
        ArgumentNullException.ThrowIfNull(description);

        Identity = identity;
        Kind = kind;
        Reference = reference;
        Applicability = applicability;
        Description = description;
        Effects = QueryOperationContract.Copy(
            effects,
            nameof(effects));
    }

    public string Identity { get; }

    public QueryOperationOrderKind Kind { get; }

    public string Reference { get; }

    public QueryOperationApplicability Applicability { get; }

    public QueryOperationOrderDescription Description { get; }

    public IReadOnlyList<QueryOperationEffect> Effects { get; }
}

public sealed class QueryOperationProfile
{
    public QueryOperationProfile(
        string identity,
        IReadOnlyList<string> termBindings,
        IReadOnlyList<string> orderBindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        Identity = identity;
        TermBindings = QueryOperationContract.CopyIdentities(
            termBindings,
            nameof(termBindings),
            requireAny: false);
        OrderBindings = QueryOperationContract.CopyIdentities(
            orderBindings,
            nameof(orderBindings),
            requireAny: false);
    }

    public string Identity { get; }

    public IReadOnlyList<string> TermBindings { get; }

    public IReadOnlyList<string> OrderBindings { get; }
}

internal static class QueryOperationContract
{
    public static IReadOnlyList<T> Copy<T>(
        IReadOnlyList<T> values,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var copy = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            copy[index] = values[index]
                ?? throw new ArgumentNullException(
                    parameterName,
                    $"Value {index + 1} is null.");
        }

        return Array.AsReadOnly(copy);
    }

    public static IReadOnlyList<string> CopyIdentities(
        IReadOnlyList<string> values,
        string parameterName,
        bool requireAny)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (requireAny && values.Count == 0)
        {
            throw new ArgumentException(
                "At least one identity is required.",
                parameterName);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var copy = new string[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            string value = values[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(
                value,
                parameterName);
            if (!seen.Add(value))
            {
                throw new ArgumentException(
                    $"Identity '{value}' is duplicated.",
                    parameterName);
            }

            copy[index] = value;
        }

        return Array.AsReadOnly(copy);
    }

    public static IReadOnlyList<string> CopyValues(
        IReadOnlyList<string> values,
        string parameterName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var copy = new string[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            string value = values[index]
                ?? throw new ArgumentNullException(
                    parameterName,
                    $"Value {index + 1} is null.");
            if (!seen.Add(value))
            {
                throw new ArgumentException(
                    $"Value '{value}' is duplicated.",
                    parameterName);
            }

            copy[index] = value;
        }

        return Array.AsReadOnly(copy);
    }

    public static void ValidateKnown(
        IReadOnlyList<string> selected,
        IReadOnlySet<string> known,
        string kind,
        string parameterName)
    {
        foreach (string identity in selected)
        {
            if (!known.Contains(identity))
            {
                throw new ArgumentException(
                    $"Unknown {kind} '{identity}'.",
                    parameterName);
            }
        }
    }

    public static void ValidateDefined<TEnum>(
        TEnum value,
        string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Unsupported {typeof(TEnum).Name} value.");
        }
    }
}
