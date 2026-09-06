using System.Collections;
using System.Collections.Immutable;

using ILInspector.Findings;

namespace ILInspector.ILDiff.Tests;

public class FindingCensusTests
{
    static readonly FindingSubject Subject = new("test", "test");
    static readonly FindingDescriptor Descriptor = new("test.item", "item");

    static Finding<string> Finding(string payload = "same")
        => new(
            Subject,
            Descriptor,
            new FindingKey("same"),
            payload,
            Detail: "same");

    static FindingCensusValidationFailure Failure(
        FindingCensusValidation validation)
        => validation switch
        {
            FindingCensusValidation.Invalid invalid => invalid.Failure,
            FindingCensusValidation.Valid => throw new Xunit.Sdk.XunitException(
                "Expected invalid census validation."),
        };

    static FindingCensusValidationFailure AssertFailure(
        FindingCensusValidation validation,
        FindingCensusValidationFailureKind kind,
        int key = 0,
        int? inputIndex = null)
    {
        FindingCensusValidationFailure failure = Failure(validation);
        Assert.Equal(kind, failure.Kind);
        Assert.Equal(key, failure.Key.Value);
        Assert.Equal(inputIndex, failure.InputIndex);
        return failure;
    }

    [Fact]
    public void Seal_PreservesOrderMultiplicityAndExactInstances()
    {
        var first = Finding();
        var second = Finding();
        Assert.Equal(first, second);
        Assert.NotSame(first, second);

        var census = FindingCensus<string>.Seal([first, second]);
        var independentlySealed = FindingCensus<string>.Seal([first, second]);

        Assert.False(census.Receipt.IsDefault);
        Assert.NotEqual(census.Receipt, independentlySealed.Receipt);
        Assert.Equal(
            census.Entries[0].Key,
            independentlySealed.Entries[0].Key);
        Assert.Equal([first, second], census.Findings);
        Assert.Same(first, census.Findings[0]);
        Assert.Same(second, census.Findings[1]);
        Assert.Equal([1, 2], census.Entries.Select(entry => entry.Key.Value));
        Assert.Same(first, census.Entries[0].Finding);
        Assert.Same(second, census.Entries[1].Finding);
        Assert.Same(census.Findings[0], census.Entries[0].Finding);
        Assert.Same(census.Findings[1], census.Entries[1].Finding);

        FindingCensusValidation validation = census.Validate(
            census.Receipt,
            census.Entries.Reverse());
        Assert.True(validation is FindingCensusValidation.Valid);
    }

    [Fact]
    public void Seal_ReceiptsSuccessfulEmptyCensusesIndependently()
    {
        var first = FindingCensus<string>.Seal([]);
        var second = FindingCensus<string>.Seal([]);

        Assert.False(first.Receipt.IsDefault);
        Assert.NotEqual(first.Receipt, second.Receipt);
        Assert.Empty(first.Findings);
        Assert.Empty(first.Entries);
        Assert.True(
            first.Validate(first.Receipt, [])
                is FindingCensusValidation.Valid);
    }

    [Fact]
    public void Seal_EnumeratesInputExactlyOnce()
    {
        var first = Finding();
        var second = Finding();
        var findings = new OneShotEnumerable<Finding<string>>([first, second]);

        var census = FindingCensus<string>.Seal(findings);

        Assert.Equal(1, findings.EnumerationCount);
        Assert.Same(first, census.Findings[0]);
        Assert.Same(second, census.Findings[1]);
        Assert.Same(first, census.Entries[0].Finding);
        Assert.Same(second, census.Entries[1].Finding);
    }

    [Fact]
    public void Seal_RejectsInvalidCollections()
    {
        Assert.Throws<ArgumentNullException>(
            () => FindingCensus<string>.Seal(null!));
        Assert.Throws<ArgumentException>(
            () => FindingCensus<string>.Seal(
                default(ImmutableArray<Finding<string>>)));
        Assert.Throws<ArgumentException>(
            () => FindingCensus<string>.Seal([null!]));
        Assert.Throws<ArgumentNullException>(
            () => new FindingCensusEntry<string>(default, null!));
    }

    [Fact]
    public void Validate_DistinguishesReceiptAndCollectionFailures()
    {
        var finding = Finding();
        var census = FindingCensus<string>.Seal([finding]);
        var other = FindingCensus<string>.Seal([finding]);

        AssertFailure(
            census.Validate(default, census.Entries),
            FindingCensusValidationFailureKind.DefaultReceipt,
            key: 0);
        AssertFailure(
            census.Validate(other.Receipt, census.Entries),
            FindingCensusValidationFailureKind.WrongReceipt,
            key: 0);
        AssertFailure(
            census.Validate(
                census.Receipt,
                default(ImmutableArray<FindingCensusEntry<string>>)),
            FindingCensusValidationFailureKind.UninitializedEntries,
            key: 0);

        ImmutableArray<FindingCensusEntry<string>> entriesWithNull = [null!];
        AssertFailure(
            census.Validate(census.Receipt, entriesWithNull),
            FindingCensusValidationFailureKind.NullEntry,
            key: 0,
            inputIndex: 0);
        Assert.Throws<ArgumentNullException>(
            () => census.Validate(census.Receipt, null!));
    }

    [Fact]
    public void Validate_DistinguishesKeySetFailures()
    {
        var first = Finding();
        var second = Finding();
        var census = FindingCensus<string>.Seal([first, second]);
        var larger = FindingCensus<string>.Seal([first, second, Finding()]);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [
                    new FindingCensusEntry<string>(default, first),
                    census.Entries[1],
                ]),
            FindingCensusValidationFailureKind.DefaultKey,
            key: 0,
            inputIndex: 0);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [
                    census.Entries[0],
                    new FindingCensusEntry<string>(
                        census.Entries[0].Key,
                        second),
                ]),
            FindingCensusValidationFailureKind.DuplicateKey,
            key: 1);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [
                    census.Entries[0],
                    new FindingCensusEntry<string>(
                        larger.Entries[2].Key,
                        second),
                ]),
            FindingCensusValidationFailureKind.ExtraKey,
            key: 3);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [census.Entries[1]]),
            FindingCensusValidationFailureKind.MissingKey,
            key: 1);
    }

    [Fact]
    public void Validate_RejectsValueEqualFindingSubstitution()
    {
        var first = Finding();
        var second = Finding();
        var substitute = Finding();
        var census = FindingCensus<string>.Seal([first, second]);

        Assert.Equal(first, substitute);
        Assert.NotSame(first, substitute);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [
                    new FindingCensusEntry<string>(
                        census.Entries[0].Key,
                        substitute),
                    census.Entries[1],
                ]),
            FindingCensusValidationFailureKind.SubstitutedFinding,
            key: 1);
    }

    [Fact]
    public void ValidateEntry_AdmitsSubsetsWithoutWeakeningAssociation()
    {
        var first = Finding();
        var second = Finding();
        var substitute = Finding();
        var census = FindingCensus<string>.Seal([first, second]);
        var other = FindingCensus<string>.Seal([first, second]);
        var larger = FindingCensus<string>.Seal([first, second, Finding()]);

        Assert.True(
            census.ValidateEntry(census.Receipt, census.Entries[1])
                is FindingCensusValidation.Valid);
        AssertFailure(
            census.ValidateEntry(
                default,
                census.Entries[1]),
            FindingCensusValidationFailureKind.DefaultReceipt,
            key: 0);
        AssertFailure(
            census.ValidateEntry(
                other.Receipt,
                census.Entries[1]),
            FindingCensusValidationFailureKind.WrongReceipt,
            key: 0);
        AssertFailure(
            census.ValidateEntry(
                census.Receipt,
                null),
            FindingCensusValidationFailureKind.NullEntry,
            key: 0);
        AssertFailure(
            census.ValidateEntry(
                census.Receipt,
                new FindingCensusEntry<string>(
                    default,
                    second)),
            FindingCensusValidationFailureKind.DefaultKey,
            key: 0);
        AssertFailure(
            census.ValidateEntry(
                census.Receipt,
                new FindingCensusEntry<string>(
                    larger.Entries[2].Key,
                    second)),
            FindingCensusValidationFailureKind.ExtraKey,
            key: 3);
        AssertFailure(
            census.ValidateEntry(
                census.Receipt,
                new FindingCensusEntry<string>(
                    census.Entries[1].Key,
                    substitute)),
            FindingCensusValidationFailureKind.SubstitutedFinding,
            key: 2);
    }

    [Fact]
    public void Validate_UsesDeterministicFailurePrecedence()
    {
        var first = Finding();
        var second = Finding();
        var census = FindingCensus<string>.Seal([first, second]);
        var larger = FindingCensus<string>.Seal([first, second, Finding()]);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [
                    census.Entries[1],
                    new FindingCensusEntry<string>(
                        census.Entries[1].Key,
                        first),
                    new FindingCensusEntry<string>(
                        larger.Entries[2].Key,
                        second),
                ]),
            FindingCensusValidationFailureKind.DuplicateKey,
            key: 2);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [
                    census.Entries[1],
                    new FindingCensusEntry<string>(
                        larger.Entries[2].Key,
                        first),
                ]),
            FindingCensusValidationFailureKind.ExtraKey,
            key: 3);

        AssertFailure(
            census.Validate(
                census.Receipt,
                [
                    new FindingCensusEntry<string>(
                        census.Entries[1].Key,
                        first),
                ]),
            FindingCensusValidationFailureKind.MissingKey,
            key: 1);
    }

    [Fact]
    public void Validate_UsesFullFailurePrecedence()
    {
        var first = Finding();
        var second = Finding();
        var census = FindingCensus<string>.Seal([first, second]);
        var other = FindingCensus<string>.Seal([first, second]);

        ImmutableArray<FindingCensusEntry<string>> uninitialized = default;
        AssertFailure(
            census.Validate(default, uninitialized),
            FindingCensusValidationFailureKind.DefaultReceipt);
        AssertFailure(
            census.Validate(other.Receipt, uninitialized),
            FindingCensusValidationFailureKind.WrongReceipt);
        AssertFailure(
            census.Validate(census.Receipt, uninitialized),
            FindingCensusValidationFailureKind.UninitializedEntries);

        FindingCensusEntry<string>[] nullBeforeDefault =
        [
            census.Entries[0],
            null!,
            new(default, second),
            null!,
        ];
        AssertFailure(
            census.Validate(census.Receipt, nullBeforeDefault),
            FindingCensusValidationFailureKind.NullEntry,
            inputIndex: 1);

        FindingCensusEntry<string>[] defaultBeforeDuplicate =
        [
            census.Entries[0],
            census.Entries[0],
            new(default, second),
            new(default, second),
        ];
        AssertFailure(
            census.Validate(census.Receipt, defaultBeforeDuplicate),
            FindingCensusValidationFailureKind.DefaultKey,
            inputIndex: 2);
    }

    [Fact]
    public void Validate_ReportsSmallestKeyIndependentOfCandidateOrder()
    {
        Finding<string>[] findings =
            [Finding("1"), Finding("2"), Finding("3"), Finding("4")];
        var census = FindingCensus<string>.Seal(findings);
        var larger = FindingCensus<string>.Seal(
            [.. findings, Finding("5"), Finding("6")]);

        FindingCensusEntry<string>[] duplicateEntries =
        [
            census.Entries[1],
            census.Entries[0],
            census.Entries[1],
            census.Entries[0],
            census.Entries[2],
            census.Entries[3],
        ];
        AssertFailureKey(
            FindingCensusValidationFailureKind.DuplicateKey,
            1,
            duplicateEntries);
        AssertFailureKey(
            FindingCensusValidationFailureKind.DuplicateKey,
            1,
            duplicateEntries.Reverse());

        FindingCensusEntry<string>[] extraEntries =
        [
            .. census.Entries,
            new FindingCensusEntry<string>(
                larger.Entries[5].Key,
                findings[0]),
            new FindingCensusEntry<string>(
                larger.Entries[4].Key,
                findings[1]),
        ];
        AssertFailureKey(
            FindingCensusValidationFailureKind.ExtraKey,
            5,
            extraEntries);
        AssertFailureKey(
            FindingCensusValidationFailureKind.ExtraKey,
            5,
            extraEntries.Reverse());

        FindingCensusEntry<string>[] missingEntries =
            [census.Entries[3], census.Entries[1]];
        AssertFailureKey(
            FindingCensusValidationFailureKind.MissingKey,
            1,
            missingEntries);
        AssertFailureKey(
            FindingCensusValidationFailureKind.MissingKey,
            1,
            missingEntries.Reverse());

        FindingCensusEntry<string>[] substitutedEntries =
        [
            new(census.Entries[1].Key, Finding("2")),
            new(census.Entries[0].Key, Finding("1")),
            census.Entries[2],
            census.Entries[3],
        ];
        AssertFailureKey(
            FindingCensusValidationFailureKind.SubstitutedFinding,
            1,
            substitutedEntries);
        AssertFailureKey(
            FindingCensusValidationFailureKind.SubstitutedFinding,
            1,
            substitutedEntries.Reverse());

        void AssertFailureKey(
            FindingCensusValidationFailureKind kind,
            int key,
            IEnumerable<FindingCensusEntry<string>> entries)
            => AssertFailure(
                census.Validate(census.Receipt, entries),
                kind,
                key);
    }

    sealed class OneShotEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        int _enumerationCount;

        public int EnumerationCount => _enumerationCount;

        public IEnumerator<T> GetEnumerator()
        {
            if (Interlocked.Increment(ref _enumerationCount) != 1)
                throw new InvalidOperationException("The sequence was enumerated twice.");
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
