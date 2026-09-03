using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ILInspector.Findings;

/// <summary>
/// Opaque producer-issued identity for one sealed Finding census.
/// </summary>
public readonly record struct FindingCensusReceipt
{
    internal FindingCensusReceipt(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Receipt must not be empty.", nameof(value));
        Value = value;
    }

    public Guid Value { get; }
    public bool IsDefault => Value == Guid.Empty;

    public override string ToString()
        => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// A compact Finding instance key whose scope is one <see cref="FindingCensusReceipt"/>.
/// </summary>
public readonly record struct FindingInstanceKey
{
    internal FindingInstanceKey(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public int Value { get; }
    public bool IsDefault => Value == 0;

    public override string ToString()
        => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One candidate or canonical key-to-Finding association within a census receipt.
/// </summary>
public sealed class FindingCensusEntry<T>
    where T : notnull
{
    public FindingCensusEntry(FindingInstanceKey key, Finding<T> finding)
    {
        Key = key;
        Finding = finding ?? throw new ArgumentNullException(nameof(finding));
    }

    public FindingInstanceKey Key { get; }
    public Finding<T> Finding { get; }
}

/// <summary>
/// Why candidate entries do not reproduce one sealed Finding census.
/// </summary>
public enum FindingCensusValidationFailureKind
{
    DefaultReceipt,
    WrongReceipt,
    UninitializedEntries,
    NullEntry,
    DefaultKey,
    DuplicateKey,
    ExtraKey,
    MissingKey,
    SubstitutedFinding,
}

/// <summary>
/// Typed evidence that candidate entries do not reproduce one sealed Finding census.
/// </summary>
public sealed record FindingCensusValidationFailure
{
    internal FindingCensusValidationFailure(
        FindingCensusValidationFailureKind kind,
        FindingInstanceKey key = default,
        int? inputIndex = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (inputIndex is < 0)
            throw new ArgumentOutOfRangeException(nameof(inputIndex));

        Kind = kind;
        Key = key;
        InputIndex = inputIndex;
    }

    public FindingCensusValidationFailureKind Kind { get; }
    public FindingInstanceKey Key { get; }
    public int? InputIndex { get; }
}

/// <summary>
/// The typed outcome of validating a candidate census projection.
/// </summary>
[Union]
public sealed record FindingCensusValidation
{
    public FindingCensusValidation(Valid value) => Value = Guard(value);
    public FindingCensusValidation(Invalid value) => Value = Guard(value);

    static object Guard(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }

    public object? Value { get; }

    public sealed record Valid
    {
        internal static Valid Instance { get; } = new();

        private Valid()
        {
        }
    }

    public sealed record Invalid
    {
        internal Invalid(FindingCensusValidationFailure failure)
            => Failure = failure ?? throw new ArgumentNullException(nameof(failure));

        public FindingCensusValidationFailure Failure { get; }
    }
}

/// <summary>
/// One immutable ordered Finding census with producer-issued instance identity.
/// </summary>
public sealed class FindingCensus<T>
    where T : notnull
{
    FindingCensus(
        FindingCensusReceipt receipt,
        ImmutableArray<Finding<T>> findings,
        ImmutableArray<FindingCensusEntry<T>> entries)
    {
        Receipt = receipt;
        Findings = findings;
        Entries = entries;
    }

    public FindingCensusReceipt Receipt { get; }
    public ImmutableArray<Finding<T>> Findings { get; }
    public ImmutableArray<FindingCensusEntry<T>> Entries { get; }

    public static FindingCensus<T> Seal(IEnumerable<Finding<T>> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings is ImmutableArray<Finding<T>> findingArray
            && findingArray.IsDefault)
        {
            throw new ArgumentException(
                "Findings must be initialized.",
                nameof(findings));
        }

        var sealedFindings = findings.ToImmutableArray();
        for (int i = 0; i < sealedFindings.Length; i++)
        {
            if (sealedFindings[i] is null)
            {
                throw new ArgumentException(
                    $"Finding at index {i} must not be null.",
                    nameof(findings));
            }
        }

        Guid receiptValue;
        do
        {
            receiptValue = Guid.NewGuid();
        }
        while (receiptValue == Guid.Empty);

        var entries = ImmutableArray.CreateBuilder<FindingCensusEntry<T>>(
            sealedFindings.Length);
        for (int i = 0; i < sealedFindings.Length; i++)
        {
            entries.Add(new FindingCensusEntry<T>(
                new FindingInstanceKey(i + 1),
                sealedFindings[i]));
        }

        return new FindingCensus<T>(
            new FindingCensusReceipt(receiptValue),
            sealedFindings,
            entries.MoveToImmutable());
    }

    public FindingCensusValidation Validate(
        FindingCensusReceipt receipt,
        IEnumerable<FindingCensusEntry<T>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (receipt.IsDefault)
            return Invalid(FindingCensusValidationFailureKind.DefaultReceipt);
        if (receipt != Receipt)
            return Invalid(FindingCensusValidationFailureKind.WrongReceipt);
        if (entries is ImmutableArray<FindingCensusEntry<T>> entryArray
            && entryArray.IsDefault)
        {
            return Invalid(
                FindingCensusValidationFailureKind.UninitializedEntries);
        }

        var candidateEntries = entries.ToImmutableArray();
        for (int i = 0; i < candidateEntries.Length; i++)
        {
            if (candidateEntries[i] is null)
            {
                return Invalid(
                    FindingCensusValidationFailureKind.NullEntry,
                    inputIndex: i);
            }
        }

        int defaultKeyIndex = -1;
        for (int i = 0; i < candidateEntries.Length; i++)
        {
            if (candidateEntries[i].Key.IsDefault)
            {
                defaultKeyIndex = i;
                break;
            }
        }
        if (defaultKeyIndex >= 0)
        {
            return Invalid(
                FindingCensusValidationFailureKind.DefaultKey,
                inputIndex: defaultKeyIndex);
        }

        FindingInstanceKey duplicateKey = candidateEntries
            .GroupBy(entry => entry.Key)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key.Value)
            .FirstOrDefault();
        if (!duplicateKey.IsDefault)
        {
            return Invalid(
                FindingCensusValidationFailureKind.DuplicateKey,
                duplicateKey);
        }

        FindingInstanceKey extraKey = candidateEntries
            .Select(entry => entry.Key)
            .Where(key => key.Value > Entries.Length)
            .OrderBy(key => key.Value)
            .FirstOrDefault();
        if (!extraKey.IsDefault)
        {
            return Invalid(
                FindingCensusValidationFailureKind.ExtraKey,
                extraKey);
        }

        var byKey = candidateEntries.ToDictionary(entry => entry.Key);
        for (int keyValue = 1; keyValue <= Entries.Length; keyValue++)
        {
            var key = new FindingInstanceKey(keyValue);
            if (!byKey.ContainsKey(key))
            {
                return Invalid(
                    FindingCensusValidationFailureKind.MissingKey,
                    key);
            }
        }

        for (int i = 0; i < Entries.Length; i++)
        {
            FindingCensusEntry<T> canonical = Entries[i];
            if (!ReferenceEquals(
                canonical.Finding,
                byKey[canonical.Key].Finding))
            {
                return Invalid(
                    FindingCensusValidationFailureKind.SubstitutedFinding,
                    canonical.Key);
            }
        }

        return FindingCensusValidation.Valid.Instance;
    }

    /// <summary>
    /// Validates one retained entry without requiring a projection to contain the whole census.
    /// </summary>
    public FindingCensusValidation ValidateEntry(
        FindingCensusReceipt receipt,
        FindingCensusEntry<T>? entry)
    {
        if (receipt.IsDefault)
            return Invalid(FindingCensusValidationFailureKind.DefaultReceipt);
        if (receipt != Receipt)
            return Invalid(FindingCensusValidationFailureKind.WrongReceipt);
        if (entry is null)
            return Invalid(FindingCensusValidationFailureKind.NullEntry);
        if (entry.Key.IsDefault)
            return Invalid(FindingCensusValidationFailureKind.DefaultKey);
        if (entry.Key.Value > Entries.Length)
        {
            return Invalid(
                FindingCensusValidationFailureKind.ExtraKey,
                entry.Key);
        }

        FindingCensusEntry<T> canonical = Entries[entry.Key.Value - 1];
        if (!ReferenceEquals(canonical.Finding, entry.Finding))
        {
            return Invalid(
                FindingCensusValidationFailureKind.SubstitutedFinding,
                entry.Key);
        }

        return FindingCensusValidation.Valid.Instance;
    }

    static FindingCensusValidation Invalid(
        FindingCensusValidationFailureKind kind,
        FindingInstanceKey key = default,
        int? inputIndex = null)
        => new(new FindingCensusValidation.Invalid(
            new FindingCensusValidationFailure(kind, key, inputIndex)));
}
