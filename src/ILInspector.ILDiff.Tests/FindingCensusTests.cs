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
        Assert.Equal([1, 2], census.Entries.Select(entry => entry.Key.Value));
        Assert.Same(first, census.Entries[0].Finding);
        Assert.Same(second, census.Entries[1].Finding);

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

        Assert.Equal(
            FindingCensusValidationFailureKind.DefaultReceipt,
            Failure(census.Validate(default, census.Entries)).Kind);
        Assert.Equal(
            FindingCensusValidationFailureKind.WrongReceipt,
            Failure(census.Validate(other.Receipt, census.Entries)).Kind);
        Assert.Equal(
            FindingCensusValidationFailureKind.UninitializedEntries,
            Failure(census.Validate(
                census.Receipt,
                default(ImmutableArray<FindingCensusEntry<string>>))).Kind);

        ImmutableArray<FindingCensusEntry<string>> entriesWithNull = [null!];
        FindingCensusValidationFailure nullEntry = Failure(census.Validate(
            census.Receipt,
            entriesWithNull));
        Assert.Equal(
            FindingCensusValidationFailureKind.NullEntry,
            nullEntry.Kind);
        Assert.Equal(0, nullEntry.InputIndex);
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

        FindingCensusValidationFailure defaultKey = Failure(census.Validate(
            census.Receipt,
            [
                new FindingCensusEntry<string>(default, first),
                census.Entries[1],
            ]));
        Assert.Equal(
            FindingCensusValidationFailureKind.DefaultKey,
            defaultKey.Kind);
        Assert.Equal(0, defaultKey.InputIndex);

        FindingCensusValidationFailure duplicate = Failure(census.Validate(
            census.Receipt,
            [
                census.Entries[0],
                new FindingCensusEntry<string>(
                    census.Entries[0].Key,
                    second),
            ]));
        Assert.Equal(
            FindingCensusValidationFailureKind.DuplicateKey,
            duplicate.Kind);
        Assert.Equal(1, duplicate.Key.Value);

        FindingCensusValidationFailure extra = Failure(census.Validate(
            census.Receipt,
            [
                census.Entries[0],
                new FindingCensusEntry<string>(
                    larger.Entries[2].Key,
                    second),
            ]));
        Assert.Equal(
            FindingCensusValidationFailureKind.ExtraKey,
            extra.Kind);
        Assert.Equal(3, extra.Key.Value);

        FindingCensusValidationFailure missing = Failure(census.Validate(
            census.Receipt,
            [census.Entries[1]]));
        Assert.Equal(
            FindingCensusValidationFailureKind.MissingKey,
            missing.Kind);
        Assert.Equal(1, missing.Key.Value);
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

        FindingCensusValidationFailure failure = Failure(census.Validate(
            census.Receipt,
            [
                new FindingCensusEntry<string>(
                    census.Entries[0].Key,
                    substitute),
                census.Entries[1],
            ]));

        Assert.Equal(
            FindingCensusValidationFailureKind.SubstitutedFinding,
            failure.Kind);
        Assert.Equal(1, failure.Key.Value);
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
        Assert.Equal(
            FindingCensusValidationFailureKind.DefaultReceipt,
            Failure(census.ValidateEntry(
                default,
                census.Entries[1])).Kind);
        Assert.Equal(
            FindingCensusValidationFailureKind.WrongReceipt,
            Failure(census.ValidateEntry(
                other.Receipt,
                census.Entries[1])).Kind);
        Assert.Equal(
            FindingCensusValidationFailureKind.NullEntry,
            Failure(census.ValidateEntry(
                census.Receipt,
                null)).Kind);
        Assert.Equal(
            FindingCensusValidationFailureKind.DefaultKey,
            Failure(census.ValidateEntry(
                census.Receipt,
                new FindingCensusEntry<string>(
                    default,
                    second))).Kind);
        Assert.Equal(
            FindingCensusValidationFailureKind.ExtraKey,
            Failure(census.ValidateEntry(
                census.Receipt,
                new FindingCensusEntry<string>(
                    larger.Entries[2].Key,
                    second))).Kind);
        Assert.Equal(
            FindingCensusValidationFailureKind.SubstitutedFinding,
            Failure(census.ValidateEntry(
                census.Receipt,
                new FindingCensusEntry<string>(
                    census.Entries[1].Key,
                    substitute))).Kind);
    }

    [Fact]
    public void Validate_UsesDeterministicFailurePrecedence()
    {
        var first = Finding();
        var second = Finding();
        var census = FindingCensus<string>.Seal([first, second]);
        var larger = FindingCensus<string>.Seal([first, second, Finding()]);

        FindingCensusValidationFailure duplicateBeforeExtra = Failure(
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
                ]));
        Assert.Equal(
            FindingCensusValidationFailureKind.DuplicateKey,
            duplicateBeforeExtra.Kind);
        Assert.Equal(2, duplicateBeforeExtra.Key.Value);

        FindingCensusValidationFailure extraBeforeMissing = Failure(
            census.Validate(
                census.Receipt,
                [
                    census.Entries[1],
                    new FindingCensusEntry<string>(
                        larger.Entries[2].Key,
                        first),
                ]));
        Assert.Equal(
            FindingCensusValidationFailureKind.ExtraKey,
            extraBeforeMissing.Kind);
        Assert.Equal(3, extraBeforeMissing.Key.Value);

        FindingCensusValidationFailure missingBeforeSubstitution = Failure(
            census.Validate(
                census.Receipt,
                [
                    new FindingCensusEntry<string>(
                        census.Entries[1].Key,
                        first),
                ]));
        Assert.Equal(
            FindingCensusValidationFailureKind.MissingKey,
            missingBeforeSubstitution.Kind);
        Assert.Equal(1, missingBeforeSubstitution.Key.Value);
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
        {
            FindingCensusValidationFailure failure = Failure(
                census.Validate(census.Receipt, entries));
            Assert.Equal(kind, failure.Kind);
            Assert.Equal(key, failure.Key.Value);
        }
    }
}
