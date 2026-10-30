using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class LibraryReferenceInspectionTests
{
    [Fact]
    public void RealSystemTextJsonProducesDirectReferences()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "LibraryOverview",
            "System.Text.Json.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available = Assert.IsType<
            LibraryReferenceInspectionOutcome.Available>(
                LibraryReferenceInspection.Execute(
                    AssemblyReferencesQuery.Execute(session)).Content);

        Assert.Contains(
            available.References,
            static reference =>
                reference.Name == "System.Runtime");
    }

    [Fact]
    public void RealCoreLibraryHasNoDirectReferences()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(object).Assembly.Location);

        var available = Assert.IsType<
            LibraryReferenceInspectionOutcome.Available>(
                LibraryReferenceInspection.Execute(
                    AssemblyReferencesQuery.Execute(session)).Content);

        Assert.Empty(available.References);
    }

    [Fact]
    public void DirectAndContextResultsProduceTheSameCanonicalRows()
    {
        AssemblyReferenceIdentity[] identities =
        [
            new("zeta", new Version(2, 0), "neutral", "02"),
            new("Alpha", new Version(2, 0), null, "03"),
            new("alpha", new Version(1, 0), "en-US", "01"),
            new("Alpha", new Version(1, 0), null, "00"),
        ];
        var direct = new AssemblyReferencesResult.Available(
            [.. identities]);
        var context = new AssemblyContextEntry<
            ImmutableArray<AssemblyReferenceRow>>.Available(
                null!,
                [
                    .. identities.Select(
                        AssemblyReferencesQuery.ToRow),
                ]);

        var directAvailable = Assert.IsType<
            LibraryReferenceInspectionOutcome.Available>(
                LibraryReferenceInspection.Execute(direct).Content);
        var contextAvailable = Assert.IsType<
            LibraryReferenceInspectionOutcome.Available>(
                LibraryReferenceInspection.Execute(context).Content);

        Assert.Equal(directAvailable.References, contextAvailable.References);
        Assert.Equal(
            ["Alpha", "Alpha", "alpha", "zeta"],
            directAvailable.References.Select(
                static reference => reference.Name));
        Assert.Equal(
            ["1.0", "2.0", "1.0", "2.0"],
            directAvailable.References.Select(
                static reference => reference.Version));
    }

    [Fact]
    public void EmptyReferencesAreAvailable()
    {
        InspectionEnvelope<LibraryReferenceInspectionOutcome> envelope =
            LibraryReferenceInspection.Execute(
                new AssemblyReferencesResult.Available([]));

        var available = Assert.IsType<
            LibraryReferenceInspectionOutcome.Available>(
                envelope.Content);
        Assert.Empty(available.References);
        Assert.IsType<InspectionShare.NonProjectable>(envelope.Share);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public void ContextRejectionRemainsTyped()
    {
        var rejected = new AssemblyContextEntry<
            ImmutableArray<AssemblyReferenceRow>>.Rejected(
                null!,
                new(
                    CandidateOpenFailureKind.InvalidImage,
                    "The image is invalid."));

        var outcome = Assert.IsType<
            LibraryReferenceInspectionOutcome.Rejected>(
                LibraryReferenceInspection.Execute(rejected).Content);

        Assert.Equal(
            AssemblyReferenceRowsRejectionKind.InvalidImage,
            outcome.Reason.Kind);
        Assert.Equal(
            "The image is invalid.",
            outcome.Reason.Detail.ToString());
    }

    [Fact]
    public void UnexpectedFailureRemainsPrimaryContent()
    {
        var failed = new AssemblyReferencesResult.Failed(
            new InvalidOperationException("Reference read failed."));

        var outcome = Assert.IsType<
            LibraryReferenceInspectionOutcome.Failed>(
                LibraryReferenceInspection.Execute(failed).Content);

        Assert.Equal(
            LibraryReferenceInspectionFailureKind.Unexpected,
            outcome.Reason.Kind);
        Assert.Equal(
            "Reference read failed.",
            outcome.Reason.Detail.ToString());
    }

    [Fact]
    public void EnvelopeRoundTripsThroughSourceGeneratedJson()
    {
        InspectionEnvelope<LibraryReferenceInspectionOutcome> envelope =
            LibraryReferenceInspection.Execute(
                new AssemblyReferencesResult.Available(
                    [
                        new AssemblyReferenceIdentity(
                            "System.Runtime",
                            new Version(10, 0),
                            null,
                            null),
                    ]));

        string json = JsonSerializer.Serialize(
            envelope,
            LibraryReferenceInspectionJsonContext.Default
                .InspectionEnvelopeLibraryReferenceInspectionOutcome);
        InspectionEnvelope<LibraryReferenceInspectionOutcome>? roundTrip =
            JsonSerializer.Deserialize(
                json,
                LibraryReferenceInspectionJsonContext.Default
                    .InspectionEnvelopeLibraryReferenceInspectionOutcome);

        Assert.NotNull(roundTrip);
        var available = Assert.IsType<
            LibraryReferenceInspectionOutcome.Available>(
                roundTrip.Content);
        Assert.Equal(
            Assert.IsType<
                LibraryReferenceInspectionOutcome.Available>(
                    envelope.Content).References,
            available.References);
        Assert.Equal(envelope.Share, roundTrip.Share);
        Assert.Empty(roundTrip.Diagnostics);
    }

    [Fact]
    public void NonAvailableOutcomesRoundTripThroughSourceGeneratedJson()
    {
        InspectionEnvelope<LibraryReferenceInspectionOutcome>[] envelopes =
        [
            LibraryReferenceInspection.Execute(
                new AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceRow>>.Rejected(
                        null!,
                        new(
                            CandidateOpenFailureKind.InvalidImage,
                            "The image is invalid."))),
            LibraryReferenceInspection.Execute(
                new AssemblyReferencesResult.Failed(
                    new InvalidOperationException(
                        "Reference read failed."))),
        ];

        foreach (
            InspectionEnvelope<LibraryReferenceInspectionOutcome> envelope
            in envelopes)
        {
            string json = JsonSerializer.Serialize(
                envelope,
                LibraryReferenceInspectionJsonContext.Default
                    .InspectionEnvelopeLibraryReferenceInspectionOutcome);
            InspectionEnvelope<LibraryReferenceInspectionOutcome>? roundTrip =
                JsonSerializer.Deserialize(
                    json,
                    LibraryReferenceInspectionJsonContext.Default
                        .InspectionEnvelopeLibraryReferenceInspectionOutcome);

            Assert.NotNull(roundTrip);
            Assert.Equal(
                envelope.Content.GetType(),
                roundTrip.Content.GetType());
            Assert.Equal(envelope.Share, roundTrip.Share);
        }
    }
}
