using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

using CSharpText;
using DotnetInspector.Libraries;
using DotnetInspector.SourceHouse.BuildAttestation;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse.Tests;

public sealed partial class AuthoredSourceHouseTests
{
    private static readonly Lazy<SourceHouseBuildAttestation>
        s_realBuildAttestation = new(BuildRealAttestation);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        PhysicalDeclaration_RealBuildAttestorProvesExactTypeAndMethod(
            bool typeTarget)
    {
        SourceHouseBuildAttestation attestation =
            s_realBuildAttestation.Value;
        byte[] assembly = attestation.PeImage.ToArray();
        byte[] pdb = attestation.PortablePdbImage.ToArray();
        SourceHouseTarget target = PhysicalTarget(
            assembly,
            typeTarget);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                pdb,
                ReadAssemblyIdentity(assembly));

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        target,
                        [attestation])));

        SourceHousePhysicalDeclarationOutcome.Exact exact =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Exact>(
                    available.PhysicalDeclaration);
        SourceHousePhysicalSourceEvidence source =
            Assert.IsType<SourceHousePhysicalSourceEvidence>(
                available.Source.PhysicalSource);
        Assert.Same(
            available.Source.ResultIdentity,
            source.Result);
        Assert.Same(source, exact.Receipt.Source);
        Assert.Same(
            available.PhysicalTarget,
            exact.Receipt.Target);
        Assert.Same(attestation.Identity, exact.Receipt.Capability);
        Assert.NotNull(exact.Receipt.Identity);
        Assert.NotNull(exact.Receipt.Generation);
        Assert.Equal(1, exact.Receipt.ContributionsObserved);
        Assert.Equal(
            1,
            available.Work.AttestationContributionsObserved);
        Assert.Equal(
            typeTarget
                ? "ClassDeclaration"
                : "MethodDeclaration",
            exact.SyntaxKind.Name);

        CSharpBuildSource physicalInput = Assert.Single(
            RealBuildSources(),
            input => SourceHouseSha256Digest
                .Compute(input.Bytes.AsSpan())
                .Matches(source.ContentDigest));
        string document = ILInspector.SourceLink.SourceLinkService
            .DecodeSourceText(physicalInput.Bytes.ToArray());
        string declaration = document.Substring(
            exact.Span.Start,
            exact.Span.Length);
        if (typeTarget)
        {
            Assert.StartsWith(
                "public static class MemberTextSlicer",
                declaration);
        }
        else
        {
            Assert.StartsWith(
                "public static string? ExtractMemberText(",
                declaration);
            Assert.DoesNotContain(
                "public static class MemberTextSlicer",
                declaration,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task
        PhysicalDeclaration_OrdinarySourceResultDoesNotInvokeAttestation()
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new IdentitylessAttestationCapability(inner);
        byte[] assembly = inner.PeImage.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                inner.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        PhysicalTarget(
                            assembly,
                            typeTarget: false),
                        [capability])));

        Assert.Null(available.Source.PhysicalSource);
        SourceHousePhysicalDeclarationOutcome.Unavailable unavailable =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Unavailable>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "SourceResultHasNoPhysicalInputIdentity",
            unavailable.Observation?.Code);
        Assert.Equal(0, capability.AttestationReads);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_AccessorRetainsAuthoredSourceAsUnavailable()
    {
        const string source =
            """
            namespace Eligibility;

            public sealed class AccessorFixture
            {
                public int Value { get; set; }
            }
            """;
        CSharpBuildAttestationOutcome buildOutcome =
            CSharpBuildAttestor.EmitAndAttest(
                new(
                    "AccessorFixture",
                    [
                        new(
                            "/fixture/AccessorFixture.cs",
                            Encoding.UTF8.GetBytes(source)),
                    ],
                    TrustedPlatformAssemblyPaths(),
                    SourceHouseCapabilityIdentity.Create(
                        "accessor-attestor"),
                    SourceHouseAttestationIssuerIdentity.Create(
                        "accessor-issuer"),
                    SourceHouseAttestationProfileIdentity.Create(
                        "direct-csharp-emit-v1"),
                    SourceHouseAttestationGeneration.Create(
                        "accessor-generation")),
                TestContext.Current.CancellationToken);
        SourceHouseBuildAttestation attestation =
            Assert.IsType<
                CSharpBuildAttestationOutcome.Available>(buildOutcome)
            .Attestation;
        byte[] assembly = attestation.PeImage.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                attestation.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        AccessorTarget(
                            assembly,
                            "Eligibility.AccessorFixture",
                            "get_Value"),
                        [attestation])));

        Assert.NotNull(available.Source.PhysicalSource);
        Assert.NotNull(available.PhysicalTarget);
        Assert.Null(
            available.PhysicalTarget.XmlDocumentationIdentity);
        SourceHousePhysicalDeclarationOutcome.Unavailable unavailable =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Unavailable>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "TargetOutsideSupportedProfile",
            unavailable.Observation?.Code);
        Assert.Same(
            available.PhysicalTarget,
            unavailable.Receipt.Target);
        Assert.Null(unavailable.Receipt.Capability);
        Assert.Equal(0, unavailable.Receipt.ContributionsObserved);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_ChangedDirectOutputIsRejected()
    {
        SourceHouseBuildAttestation attestation =
            s_realBuildAttestation.Value;
        byte[] transformedAssembly =
        [
            .. attestation.PeImage,
            0,
        ];
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                transformedAssembly,
                attestation.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(transformedAssembly));

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        PhysicalTarget(
                            transformedAssembly,
                            typeTarget: false),
                        [attestation])));

        SourceHousePhysicalDeclarationOutcome.Rejected rejected =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Rejected>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "AttestationAssociationMismatch",
            rejected.Observation?.Code);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_ContributionBoundIsIncomplete()
    {
        SourceHouseBuildAttestation attestation =
            s_realBuildAttestation.Value;
        byte[] assembly = attestation.PeImage.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                attestation.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        PhysicalTarget(
                            assembly,
                            typeTarget: false),
                        [attestation],
                        limits: Limits(
                            maximumAttestationContributions: 0))));

        SourceHousePhysicalDeclarationOutcome.Incomplete incomplete =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Incomplete>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "AttestationContributionLimitExceeded",
            incomplete.Observation?.Code);
        Assert.Equal(
            1,
            available.Work.AttestationContributionsObserved);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_StaleGenerationIsRejected()
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (request, maximum, token) =>
            {
                var available = Assert.IsType<
                    SourceHouseAttestationCapabilityOutcome.Available>(
                        await inner.ReadAttestationsAsync(
                            request,
                            maximum,
                            token));
                SourceHousePhysicalDeclarationAttestation exact =
                    Assert.Single(available.Attestations);
                return new SourceHouseAttestationCapabilityOutcome
                    .Available(
                    [
                        exact,
                        Copy(
                            exact,
                            generation:
                                SourceHouseAttestationGeneration
                                    .Create(
                                        "stale-generation")),
                    ]);
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(capability);

        SourceHousePhysicalDeclarationOutcome.Rejected rejected =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Rejected>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "AttestationAssociationMismatch",
            rejected.Observation?.Code);
    }

    [Theory]
    [InlineData(PhysicalAssociationMutation.Request)]
    [InlineData(PhysicalAssociationMutation.Library)]
    [InlineData(PhysicalAssociationMutation.SelectedAssembly)]
    [InlineData(PhysicalAssociationMutation.OperationPlan)]
    [InlineData(PhysicalAssociationMutation.PolicyGeneration)]
    [InlineData(PhysicalAssociationMutation.Issuer)]
    [InlineData(PhysicalAssociationMutation.Profile)]
    [InlineData(PhysicalAssociationMutation.ModuleDigest)]
    [InlineData(PhysicalAssociationMutation.ModuleVersionId)]
    [InlineData(PhysicalAssociationMutation.MetadataTarget)]
    [InlineData(PhysicalAssociationMutation.XmlDocumentationIdentity)]
    [InlineData(PhysicalAssociationMutation.SourceResult)]
    [InlineData(PhysicalAssociationMutation.SourceInput)]
    [InlineData(PhysicalAssociationMutation.SourceDigest)]
    [InlineData(PhysicalAssociationMutation.SourceEncoding)]
    public async Task
        PhysicalDeclaration_AnyMismatchedAssociationRejectsAllContributions(
            PhysicalAssociationMutation mutation)
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        byte[] assembly = inner.PeImage.ToArray();
        await using LibraryFixture otherLibrary =
            await LibraryFixture.CreateAsync(
                assembly,
                inner.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));
        SourceHouseResultIdentity? otherResult =
            mutation == PhysicalAssociationMutation.SourceResult
                ? (await ExecutePhysicalAsync(inner))
                    .Source.ResultIdentity
                : null;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (request, maximum, token) =>
            {
                var available = Assert.IsType<
                    SourceHouseAttestationCapabilityOutcome.Available>(
                        await inner.ReadAttestationsAsync(
                            request,
                            maximum,
                            token));
                SourceHousePhysicalDeclarationAttestation exact =
                    Assert.Single(available.Attestations);
                SourceHousePhysicalDeclarationAttestation mismatched =
                    mutation switch
                    {
                        PhysicalAssociationMutation.Request =>
                            Copy(
                                exact,
                                request:
                                    SourceHouseRequestIdentity.Create(
                                        "mismatched-request")),
                        PhysicalAssociationMutation.Library =>
                            Copy(
                                exact,
                                library: otherLibrary.Reference),
                        PhysicalAssociationMutation.SelectedAssembly =>
                            Copy(
                                exact,
                                selectedAssembly:
                                    otherLibrary.Reference.ApiAssembly),
                        PhysicalAssociationMutation.OperationPlan =>
                            Copy(
                                exact,
                                operationPlan:
                                    SourceHouseOperationPlanIdentity
                                        .Create(
                                            "mismatched-plan")),
                        PhysicalAssociationMutation.PolicyGeneration =>
                            Copy(
                                exact,
                                policyGeneration:
                                    SourceHousePolicyGeneration.Create(
                                        "mismatched-policy")),
                        PhysicalAssociationMutation.Issuer =>
                            Copy(
                                exact,
                                issuer:
                                    SourceHouseAttestationIssuerIdentity
                                        .Create(
                                            "mismatched-issuer")),
                        PhysicalAssociationMutation.Profile =>
                            Copy(
                                exact,
                                profile:
                                    SourceHouseAttestationProfileIdentity
                                        .Create(
                                            "mismatched-profile")),
                        PhysicalAssociationMutation.ModuleDigest =>
                            Copy(
                                exact,
                                moduleDigest:
                                    SourceHouseSha256Digest.Compute(
                                        [0x01])),
                        PhysicalAssociationMutation.ModuleVersionId =>
                            Copy(
                                exact,
                                target:
                                    DifferentMethodTarget(
                                        exact.Target,
                                        changeModuleVersionId: true)),
                        PhysicalAssociationMutation.MetadataTarget =>
                            Copy(
                                exact,
                                target:
                                    DifferentMethodTarget(
                                        exact.Target,
                                        changeModuleVersionId: false)),
                        PhysicalAssociationMutation
                            .XmlDocumentationIdentity =>
                            Copy(
                                exact,
                                xmlDocumentationIdentity:
                                    new("M:Mismatch.Subject")),
                        PhysicalAssociationMutation.SourceResult =>
                            Copy(
                                exact,
                                sourceResult: otherResult!),
                        PhysicalAssociationMutation.SourceInput =>
                            Copy(
                                exact,
                                sourceInput:
                                    SourceHousePhysicalSourceInputIdentity
                                        .Create()),
                        PhysicalAssociationMutation.SourceDigest =>
                            Copy(
                                exact,
                                sourceDigest:
                                    SourceHouseSha256Digest.Compute(
                                        [0x02])),
                        PhysicalAssociationMutation.SourceEncoding =>
                            Copy(
                                exact,
                                sourceEncoding:
                                    exact.SourceEncoding
                                        == SourceHousePhysicalSourceEncoding
                                            .Utf8
                                            ? SourceHousePhysicalSourceEncoding
                                                .Utf16LittleEndian
                                            : SourceHousePhysicalSourceEncoding
                                                .Utf8),
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(mutation)),
                    };
                return new SourceHouseAttestationCapabilityOutcome
                    .Available(
                    [
                        exact,
                        mismatched,
                    ]);
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(capability);

        SourceHousePhysicalDeclarationOutcome.Rejected rejected =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Rejected>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "AttestationAssociationMismatch",
            rejected.Observation?.Code);
        Assert.Equal(2, rejected.Receipt.ContributionsObserved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        PhysicalDeclaration_OutOfBoundsSpanIsRejected(
            bool includeValidContribution)
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (request, maximum, token) =>
            {
                var available = Assert.IsType<
                    SourceHouseAttestationCapabilityOutcome.Available>(
                        await inner.ReadAttestationsAsync(
                            request,
                            maximum,
                            token));
                SourceHousePhysicalDeclarationAttestation exact =
                    Assert.Single(available.Attestations);
                SourceHousePhysicalDeclarationAttestation invalid =
                    Copy(
                        exact,
                        span: new(
                            request.Source.RawUtf16Length,
                            1));
                return new SourceHouseAttestationCapabilityOutcome
                    .Available(
                        includeValidContribution
                            ? [exact, invalid]
                            : [invalid]);
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(capability);

        SourceHousePhysicalDeclarationOutcome.Rejected rejected =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Rejected>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "AttestationAssociationMismatch",
            rejected.Observation?.Code);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_DisagreeingSpansConflict()
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (request, maximum, token) =>
            {
                var available = Assert.IsType<
                    SourceHouseAttestationCapabilityOutcome.Available>(
                        await inner.ReadAttestationsAsync(
                            request,
                            maximum,
                            token));
                SourceHousePhysicalDeclarationAttestation exact =
                    Assert.Single(available.Attestations);
                return new SourceHouseAttestationCapabilityOutcome
                    .Available(
                    [
                        exact,
                        Copy(
                            exact,
                            span: new(
                                exact.Span.Start + 1,
                                exact.Span.Length - 1)),
                    ]);
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(capability);

        SourceHousePhysicalDeclarationOutcome.Conflict conflict =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Conflict>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "PhysicalDeclarationClaimsDisagree",
            conflict.Observation?.Code);
        Assert.Equal(2, conflict.Receipt.ContributionsObserved);
        Assert.Equal(2, conflict.Claims.Count);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_DisagreeingSyntaxKindsConflict()
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (request, maximum, token) =>
            {
                var available = Assert.IsType<
                    SourceHouseAttestationCapabilityOutcome.Available>(
                        await inner.ReadAttestationsAsync(
                            request,
                            maximum,
                            token));
                SourceHousePhysicalDeclarationAttestation exact =
                    Assert.Single(available.Attestations);
                return new SourceHouseAttestationCapabilityOutcome
                    .Available(
                    [
                        exact,
                        Copy(
                            exact,
                            syntaxKind:
                                SourceHouseDeclarationSyntaxKind
                                    .Create(
                                        "ConstructorDeclaration")),
                    ]);
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(capability);

        SourceHousePhysicalDeclarationOutcome.Conflict conflict =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Conflict>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "PhysicalDeclarationClaimsDisagree",
            conflict.Observation?.Code);
        Assert.Equal(2, conflict.Claims.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        PhysicalDeclaration_TargetIncompatibleSyntaxKindIsRejected(
            bool includeValidContribution)
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (request, maximum, token) =>
            {
                var available = Assert.IsType<
                    SourceHouseAttestationCapabilityOutcome.Available>(
                        await inner.ReadAttestationsAsync(
                            request,
                            maximum,
                            token));
                SourceHousePhysicalDeclarationAttestation exact =
                    Assert.Single(available.Attestations);
                SourceHousePhysicalDeclarationAttestation invalid =
                    Copy(
                        exact,
                        syntaxKind:
                            SourceHouseDeclarationSyntaxKind
                                .Create(
                                    "ClassDeclaration"));
                return new SourceHouseAttestationCapabilityOutcome
                    .Available(
                        includeValidContribution
                            ? [exact, invalid]
                            : [invalid]);
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(capability);

        SourceHousePhysicalDeclarationOutcome.Rejected rejected =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Rejected>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "AttestationAssociationMismatch",
            rejected.Observation?.Code);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_CharacterBoundIsIncomplete()
    {
        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(
                s_realBuildAttestation.Value,
                limits: Limits(
                    maximumPhysicalDeclarationCharacters: 1));

        SourceHousePhysicalDeclarationOutcome.Incomplete incomplete =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Incomplete>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "PhysicalDeclarationCharacterLimitExceeded",
            incomplete.Observation?.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        PhysicalDeclaration_OutcomePrecedenceIsIndependentOfContributionOrder(
            bool incompleteFirst)
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (request, maximum, token) =>
            {
                var available = Assert.IsType<
                    SourceHouseAttestationCapabilityOutcome.Available>(
                        await inner.ReadAttestationsAsync(
                            request,
                            maximum,
                            token));
                SourceHousePhysicalDeclarationAttestation exact =
                    Assert.Single(available.Attestations);
                SourceHousePhysicalDeclarationAttestation rejected =
                    Copy(
                        exact,
                        sourceInput:
                            SourceHousePhysicalSourceInputIdentity
                                .Create());
                SourceHousePhysicalDeclarationAttestation incomplete =
                    Copy(exact);
                return new SourceHouseAttestationCapabilityOutcome
                    .Available(
                        incompleteFirst
                            ? [incomplete, rejected]
                            : [rejected, incomplete]);
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(
                capability,
                limits: Limits(
                    maximumPhysicalDeclarationCharacters: 1));

        SourceHousePhysicalDeclarationOutcome.Incomplete incomplete =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Incomplete>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "PhysicalDeclarationCharacterLimitExceeded",
            incomplete.Observation?.Code);
        Assert.Equal(2, incomplete.Receipt.ContributionsObserved);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_DeadlineCancelsAttestationOnly()
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var capability = new DelegatingAttestationCapability(
            inner,
            async (_, _, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            });

        SourceHouseOutcome.Available available =
            await ExecutePhysicalAsync(
                capability,
                deadline:
                    DateTimeOffset.UtcNow.AddMilliseconds(200));

        SourceHousePhysicalDeclarationOutcome.Incomplete incomplete =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Incomplete>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "DeadlineExpiredDuringAttestation",
            incomplete.Observation?.Code);
        Assert.Contains(
            "public static string? ExtractMemberText(",
            available.Source.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_CallerCancellationRemainsCancellationAndSettlesLease()
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var capability = new DelegatingAttestationCapability(
            inner,
            async (_, _, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            });
        byte[] assembly = inner.PeImage.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                inner.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));
        using var cancellation = new CancellationTokenSource();
        LibraryOperationLease operation = library.IssueOperation();
        ValueTask<SourceHouseOutcome> execution =
            SourceHouse.ExecuteAuthoredAsync(
                Request(
                    library,
                    PhysicalTarget(
                        assembly,
                        typeTarget: false),
                    [capability]),
                operation,
                cancellation.Token);
        await started.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await execution);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        PhysicalDeclaration_ByteIdenticalLineDestinationIsUnavailable()
    {
        const string physicalPath = "/fixture/physical.cs";
        const string mappedPath = "/fixture/mapped.cs";
        byte[] physicalBytes = Encoding.UTF8.GetBytes(
            $$"""
            #line 1 "{{mappedPath}}"
            namespace Mapped;
            public class Subject
            {
                public void Target() { }
            }
            """);
        string checksum = Convert.ToHexString(
            SHA256.HashData(physicalBytes));
        byte[] checksumBytes = Encoding.UTF8.GetBytes(
            $$"""
            #pragma checksum "{{mappedPath}}" "{8829d00f-11b8-4213-878b-770e8597ac16}" "{{checksum}}"
            """);
        SourceHouseBuildAttestation attestation =
            Assert.IsType<CSharpBuildAttestationOutcome.Available>(
                CSharpBuildAttestor.EmitAndAttest(
                    new(
                        "MappedPhysicalInputFixture",
                        [
                            new(physicalPath, physicalBytes),
                            new(
                                "/fixture/Checksums.g.cs",
                                checksumBytes,
                                CSharpBuildSourceKind.Generated),
                        ],
                        TrustedPlatformAssemblyPaths(),
                        SourceHouseCapabilityIdentity.Create(
                            "mapped-build-attestor"),
                        SourceHouseAttestationIssuerIdentity.Create(
                            "mapped-build"),
                        SourceHouseAttestationProfileIdentity.Create(
                            "direct-csharp-emit-v1"),
                        SourceHouseAttestationGeneration.Create(
                            "mapped-generation")),
                    TestContext.Current.CancellationToken))
            .Attestation;
        byte[] assembly = attestation.PeImage.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                attestation.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));
        SourceHouseTarget.TypeTarget target = TypeTarget(
            assembly,
            "Mapped.Subject");

        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        target,
                        [
                            attestation,
                            Capability(
                                "mapped-destination",
                                SourceHouseCapabilityCategory
                                    .Repository,
                                (candidate, _, _) =>
                                {
                                    Assert.Equal(
                                        mappedPath,
                                        candidate.Document
                                            .OriginalPath);
                                    return ValueTask.FromResult<
                                        SourceHouseCapabilityOutcome>(
                                            new SourceHouseCapabilityOutcome
                                                .Available(
                                                    physicalBytes));
                                }),
                        ])));

        Assert.Equal(
            SourceChecksumVerification.Exact,
            available.Source.Selected.ChecksumVerification);
        Assert.Null(available.Source.PhysicalSource);
        SourceHousePhysicalDeclarationOutcome.Unavailable unavailable =
            Assert.IsType<
                SourceHousePhysicalDeclarationOutcome.Unavailable>(
                    available.PhysicalDeclaration);
        Assert.Equal(
            "SourceResultHasNoPhysicalInputIdentity",
            unavailable.Observation?.Code);
    }

    [Fact]
    public void
        BuildAttestor_ExcludesUnsupportedDeclarationShapes()
    {
        const string authored =
            """
            namespace Eligibility;

            public partial class PartialType
            {
                public partial void PartialMethod();
                public partial void PartialMethod() { }
            }

            public interface Contract
            {
                void Bodyless();
                int Value { get; }
            }

            public class Supported
            {
                public int Value { get; set; }
                public void Included() { }
            }
            """;
        const string generated =
            """
            namespace Eligibility;
            public class GeneratedType
            {
                public void GeneratedMethod() { }
            }
            """;
        CSharpBuildAttestationOutcome outcome =
            CSharpBuildAttestor.EmitAndAttest(
                new(
                    "DeclarationEligibilityFixture",
                    [
                        new(
                            "/fixture/Authored.cs",
                            Encoding.UTF8.GetBytes(authored)),
                        new(
                            "/fixture/Generated.g.cs",
                            Encoding.UTF8.GetBytes(generated),
                            CSharpBuildSourceKind.Generated),
                    ],
                    TrustedPlatformAssemblyPaths(),
                    SourceHouseCapabilityIdentity.Create(
                        "eligibility-attestor"),
                    SourceHouseAttestationIssuerIdentity.Create(
                        "eligibility-issuer"),
                    SourceHouseAttestationProfileIdentity.Create(
                        "direct-csharp-emit-v1"),
                    SourceHouseAttestationGeneration.Create(
                        "eligibility-generation")),
                TestContext.Current.CancellationToken);
        SourceHouseBuildAttestation attestation =
            Assert.IsType<
                CSharpBuildAttestationOutcome.Available>(outcome)
            .Attestation;
        string[] identities =
        [
            .. attestation.AttestedXmlDocumentationIdentities
                .Select(static identity => identity.Value),
        ];

        Assert.Contains("T:Eligibility.Supported", identities);
        Assert.Contains(
            "M:Eligibility.Supported.Included",
            identities);
        Assert.DoesNotContain(
            "T:Eligibility.PartialType",
            identities);
        Assert.DoesNotContain(
            "M:Eligibility.PartialType.PartialMethod",
            identities);
        Assert.DoesNotContain(
            "M:Eligibility.Contract.Bodyless",
            identities);
        Assert.DoesNotContain(
            "M:Eligibility.Supported.get_Value",
            identities);
        Assert.DoesNotContain(
            "M:Eligibility.Supported.set_Value(System.Int32)",
            identities);
        Assert.DoesNotContain(
            "T:Eligibility.GeneratedType",
            identities);
    }

    [Fact]
    public async Task
        BuildAttestor_ByteIdenticalInputsRetainDistinctOpaqueIdentities()
    {
        byte[] identicalBytes = Encoding.UTF8.GetBytes(
            "// distinct physical compiler inputs");
        byte[] declarationBytes = Encoding.UTF8.GetBytes(
            """
            namespace Identity;
            public class Eligible
            {
                public void Method() { }
            }
            """);
        SourceHouseBuildAttestation attestation =
            Assert.IsType<CSharpBuildAttestationOutcome.Available>(
                CSharpBuildAttestor.EmitAndAttest(
                    new(
                        "IdenticalPhysicalInputsFixture",
                        [
                            new("/fixture/Same.cs", identicalBytes),
                            new("/fixture/Same.cs", identicalBytes),
                            new(
                                "/fixture/Eligible.cs",
                                declarationBytes),
                        ],
                        TrustedPlatformAssemblyPaths(),
                        SourceHouseCapabilityIdentity.Create(
                            "identical-input-attestor"),
                        SourceHouseAttestationIssuerIdentity.Create(
                            "identical-input-issuer"),
                        SourceHouseAttestationProfileIdentity.Create(
                            "direct-csharp-emit-v1"),
                        SourceHouseAttestationGeneration.Create(
                            "identical-input-generation")),
                    TestContext.Current.CancellationToken))
            .Attestation;
        SourceHouseBuildSourceEvidence[] physicalInputs =
        [
            .. attestation.Sources.Where(static source =>
                source.Kind == CSharpBuildSourceKind.Authored),
        ];

        Assert.Equal(3, physicalInputs.Length);
        Assert.True(
            physicalInputs[0].ContentDigest.Matches(
                physicalInputs[1].ContentDigest));
        Assert.NotSame(
            physicalInputs[0].Identity,
            physicalInputs[1].Identity);
        Assert.Equal(
            physicalInputs[0].Path,
            physicalInputs[1].Path);
        Assert.Contains(
            attestation.AttestedXmlDocumentationIdentities,
            identity =>
                identity.Value
                    == "M:Identity.Eligible.Method");

        byte[] assembly = attestation.PeImage.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                attestation.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));
        SourceHouseOutcome.Available available =
            Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(
                    library,
                    Request(
                        library,
                        MethodTarget(
                            assembly,
                            "Identity.Eligible",
                            "Method"),
                        [attestation])));
        Assert.IsType<
            SourceHousePhysicalDeclarationOutcome.Exact>(
                available.PhysicalDeclaration);
    }

    [Fact]
    public void
        BuildAttestor_ReorderedNestedAliasAndConditionalDeclarationsRemainExact()
    {
        SourceHouseBuildAttestation first =
            BuildShapeAttestation(reverseOverloads: false);
        SourceHouseBuildAttestation second =
            BuildShapeAttestation(reverseOverloads: true);
        const string integerId =
            "M:Shapes.Outer.Nested.Overload(System.Int32)";
        const string stringId =
            "M:Shapes.Outer.Nested.Overload(System.String)";
        const string activeId =
            "M:Shapes.Outer.Nested.Active(System.Int32)";

        CSharpBuildSource firstSource = Assert.Single(
            ShapeSources(reverseOverloads: false));
        CSharpBuildSource secondSource = Assert.Single(
            ShapeSources(reverseOverloads: true));
        AssertDeclaration(
            first,
            firstSource,
            integerId,
            "Overload(Number value)");
        AssertDeclaration(
            first,
            firstSource,
            stringId,
            "Overload(string value)");
        AssertDeclaration(
            first,
            firstSource,
            activeId,
            "Active(Number value)");
        AssertDeclaration(
            second,
            secondSource,
            integerId,
            "Overload(Number value)");
        AssertDeclaration(
            second,
            secondSource,
            stringId,
            "Overload(string value)");
        AssertDeclaration(
            second,
            secondSource,
            activeId,
            "Active(Number value)");
        Assert.Contains(
            first.AttestedXmlDocumentationIdentities,
            identity =>
                identity.Value == "T:Shapes.Outer.Nested");

        static void AssertDeclaration(
            SourceHouseBuildAttestation attestation,
            CSharpBuildSource input,
            string xmlIdentity,
            string expectedText)
        {
            SourceHouseBuildDeclarationEvidence declaration =
                Assert.Single(
                    attestation.Declarations,
                    candidate =>
                        candidate.XmlIdentity.Value
                            == xmlIdentity);
            Assert.Contains(
                attestation.Sources,
                candidate =>
                    candidate.Path == input.Path
                    && ReferenceEquals(
                        candidate.Identity,
                        declaration.SourceInput));
            string text = SourceLinkService.DecodeSourceText(
                input.Bytes.ToArray());
            Assert.Contains(
                expectedText,
                text.Substring(
                    declaration.Span.Start,
                    declaration.Span.Length),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void
        BuildAttestor_CompilerIdentityCollisionRemainsUnavailable()
    {
        const string source =
            """
            namespace Collision;

            public unsafe class Subject
            {
                public void M(delegate* unmanaged[Cdecl]<void> value) { }
                public void M(delegate* unmanaged[Stdcall]<void> value) { }
            }
            """;
        SourceHouseBuildAttestation attestation =
            Assert.IsType<CSharpBuildAttestationOutcome.Available>(
                CSharpBuildAttestor.EmitAndAttest(
                    new(
                        "CompilerIdentityCollisionFixture",
                        [
                            new(
                                "/fixture/Collision.cs",
                                Encoding.UTF8.GetBytes(source)),
                        ],
                        TrustedPlatformAssemblyPaths(),
                        SourceHouseCapabilityIdentity.Create(
                            "collision-attestor"),
                        SourceHouseAttestationIssuerIdentity.Create(
                            "collision-issuer"),
                        SourceHouseAttestationProfileIdentity.Create(
                            "direct-csharp-emit-v1"),
                        SourceHouseAttestationGeneration.Create(
                            "collision-generation")),
                    TestContext.Current.CancellationToken))
            .Attestation;

        Assert.Equal(1, attestation.CompilerIdentityCollisionCount);
        Assert.DoesNotContain(
            attestation.Declarations,
            declaration =>
                declaration.XmlIdentity.Value.StartsWith(
                    "M:Collision.Subject.M(",
                    StringComparison.Ordinal));
        using var peReader = new PEReader(
            new MemoryStream(
                attestation.PeImage.ToArray(),
                writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        Assert.Equal(
            2,
            reader.MethodDefinitions.Count(handle =>
                reader.GetString(
                    reader.GetMethodDefinition(handle).Name)
                    == "M"));
    }

    [Fact]
    public void
        BuildAttestor_InvalidSourceEncodingFailsVisibly()
    {
        byte[] invalidUtf8 =
        [
            0xEF,
            0xBB,
            0xBF,
            (byte)'/',
            (byte)'/',
            0xC3,
            0x28,
        ];

        CSharpBuildAttestationOutcome.Failed failed =
            Assert.IsType<CSharpBuildAttestationOutcome.Failed>(
                CSharpBuildAttestor.EmitAndAttest(
                    new(
                        "InvalidEncodingFixture",
                        [
                            new(
                                "/fixture/Invalid.cs",
                                invalidUtf8),
                        ],
                        TrustedPlatformAssemblyPaths(),
                        SourceHouseCapabilityIdentity.Create(
                            "invalid-encoding-attestor"),
                        SourceHouseAttestationIssuerIdentity.Create(
                            "invalid-encoding-issuer"),
                        SourceHouseAttestationProfileIdentity.Create(
                            "direct-csharp-emit-v1"),
                        SourceHouseAttestationGeneration.Create(
                            "invalid-encoding-generation")),
                    TestContext.Current.CancellationToken));

        Assert.NotEmpty(failed.Diagnostics);
    }

    private static SourceHouseBuildAttestation BuildShapeAttestation(
        bool reverseOverloads) =>
        Assert.IsType<CSharpBuildAttestationOutcome.Available>(
            CSharpBuildAttestor.EmitAndAttest(
                new(
                    reverseOverloads
                        ? "ReorderedShapeFixture"
                        : "ShapeFixture",
                    ShapeSources(reverseOverloads),
                    TrustedPlatformAssemblyPaths(),
                    SourceHouseCapabilityIdentity.Create(
                        reverseOverloads
                            ? "reordered-shape-attestor"
                            : "shape-attestor"),
                    SourceHouseAttestationIssuerIdentity.Create(
                        "shape-issuer"),
                    SourceHouseAttestationProfileIdentity.Create(
                        "direct-csharp-emit-v1"),
                    SourceHouseAttestationGeneration.Create(
                        reverseOverloads
                            ? "reordered-shape-generation"
                            : "shape-generation")),
                TestContext.Current.CancellationToken))
        .Attestation;

    private static CSharpBuildSource[] ShapeSources(
        bool reverseOverloads)
    {
        string overloads = reverseOverloads
            ?
            """
                    public void Overload(string value) { }
                    public void Overload(Number value) { }
            """
            :
            """
                    public void Overload(Number value) { }
                    public void Overload(string value) { }
            """;
        string source =
            $$"""
            using Number = System.Int32;

            namespace Shapes;

            public class Outer
            {
                public class Nested
                {
            #if true
                    public void Active(Number value) { }
            #endif
            {{overloads}}
                }
            }
            """;
        return
        [
            new(
                "/fixture/Shapes.cs",
                Encoding.UTF8.GetBytes(source)),
        ];
    }

    private static async Task<SourceHouseOutcome.Available>
        ExecutePhysicalAsync(
            ISourceHousePhysicalDeclarationCapability capability,
            SourceHouseLimits? limits = null,
            DateTimeOffset? deadline = null)
    {
        SourceHouseBuildAttestation inner =
            s_realBuildAttestation.Value;
        byte[] assembly = inner.PeImage.ToArray();
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                assembly,
                inner.PortablePdbImage.ToArray(),
                ReadAssemblyIdentity(assembly));
        return Assert.IsType<SourceHouseOutcome.Available>(
            await ExecuteAsync(
                library,
                Request(
                    library,
                    PhysicalTarget(
                        assembly,
                        typeTarget: false),
                    [capability],
                    limits,
                    deadline)));
    }

    private static SourceHousePhysicalDeclarationAttestation Copy(
        SourceHousePhysicalDeclarationAttestation source,
        SourceHouseRequestIdentity? request = null,
        LibraryReference? library = null,
        LibraryContentReference? selectedAssembly = null,
        SourceHouseOperationPlanIdentity? operationPlan = null,
        SourceHousePolicyGeneration? policyGeneration = null,
        SourceHouseAttestationIssuerIdentity? issuer = null,
        SourceHouseAttestationProfileIdentity? profile = null,
        SourceHouseSha256Digest? moduleDigest = null,
        SourceHousePhysicalTargetAddress? target = null,
        XmlDocMemberIdentity? xmlDocumentationIdentity = null,
        SourceHouseResultIdentity? sourceResult = null,
        SourceHousePhysicalSourceInputIdentity? sourceInput = null,
        SourceHouseSha256Digest? sourceDigest = null,
        SourceHousePhysicalSourceEncoding? sourceEncoding = null,
        SourceHousePhysicalDeclarationSpan? span = null,
        SourceHouseAttestationGeneration? generation = null,
        SourceHouseDeclarationSyntaxKind? syntaxKind = null) =>
        new(
            request ?? source.Request,
            library ?? source.Library,
            selectedAssembly ?? source.SelectedAssembly,
            operationPlan ?? source.OperationPlan,
            policyGeneration ?? source.PolicyGeneration,
            issuer ?? source.Issuer,
            profile ?? source.Profile,
            generation ?? source.Generation,
            moduleDigest ?? source.ModuleDigest,
            target ?? source.Target,
            xmlDocumentationIdentity
                ?? source.XmlDocumentationIdentity,
            sourceResult ?? source.SourceResult,
            sourceInput ?? source.SourceInput,
            sourceDigest ?? source.SourceDigest,
            sourceEncoding ?? source.SourceEncoding,
            span ?? source.Span,
            syntaxKind ?? source.SyntaxKind);

    private static SourceHousePhysicalTargetAddress
        DifferentMethodTarget(
            SourceHousePhysicalTargetAddress target,
            bool changeModuleVersionId)
    {
        MetadataMethodAddress address =
            Assert.IsType<
                SourceHousePhysicalTargetAddress.Method>(target)
            .Address;
        return new SourceHousePhysicalTargetAddress.Method(
            new MetadataMethodAddress(
                changeModuleVersionId
                    ? Guid.NewGuid()
                    : address.ModuleVersionId,
                changeModuleVersionId
                    ? address.Handle
                    : MetadataTokens.MethodDefinitionHandle(
                        MetadataTokens.GetRowNumber(
                            address.Handle) + 1)));
    }

    private static SourceHouseBuildAttestation BuildRealAttestation()
    {
        CSharpBuildAttestationOutcome outcome =
            CSharpBuildAttestor.EmitAndAttest(
                new(
                    "SourceHousePhysicalDeclarationFixture",
                    RealBuildSources(),
                    TrustedPlatformAssemblyPaths(),
                    SourceHouseCapabilityIdentity.Create(
                        "real-build-attestor"),
                    SourceHouseAttestationIssuerIdentity.Create(
                        "dotnet-inspect-build"),
                    SourceHouseAttestationProfileIdentity.Create(
                        "direct-csharp-emit-v1"),
                    SourceHouseAttestationGeneration.Create(
                        "real-source-build")));
        if (outcome is CSharpBuildAttestationOutcome.Failed failed)
        {
            Assert.Fail(
                string.Join(Environment.NewLine, failed.Diagnostics));
        }

        return Assert.IsType<
                CSharpBuildAttestationOutcome.Available>(outcome)
            .Attestation;
    }

    private static CSharpBuildSource[] RealBuildSources()
    {
        string root = RepositoryRoot();
        return new[]
            {
                Path.Combine(
                    root,
                    "src",
                    "CSharpText.MemberSlicing"),
            }
            .SelectMany(directory =>
                Directory.EnumerateFiles(
                    directory,
                    "*.cs",
                    SearchOption.TopDirectoryOnly))
            .Order(StringComparer.Ordinal)
            .Select(path =>
                new CSharpBuildSource(
                    path,
                    File.ReadAllBytes(path)))
            .ToArray();
    }

    private static string[] TrustedPlatformAssemblyPaths()
    {
        string runtimeDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException(
                "The runtime assembly has no directory.");
        return
        [
            .. Directory.EnumerateFiles(
                runtimeDirectory,
                "*.dll",
                SearchOption.TopDirectoryOnly),
            typeof(CSharpText.CSharpSourceText).Assembly.Location,
        ];
    }

    private static SourceHouseTarget PhysicalTarget(
        byte[] assembly,
        bool typeTarget)
    {
        using var peReader = new PEReader(
            new MemoryStream(assembly, writable: false));
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeof(
                        CSharpText.MemberSlicing.MemberTextSlicer)
                        .FullName);
        if (typeTarget)
            return new SourceHouseTarget.TypeTarget(
                type.DefinitionName!);

        ApiMember member = Assert.Single(
            type.Members,
            candidate =>
                candidate.Name == nameof(
                    CSharpText.MemberSlicing.MemberTextSlicer
                        .ExtractMemberText)
                && candidate.MetadataToken is not null);
        return new SourceHouseTarget.MemberTarget(
            type.DefinitionName!,
            ApiMemberIdentity.GetMemberAnchor(type, member),
            member.MetadataToken!.Value);
    }

    private static SourceHouseTarget.TypeTarget TypeTarget(
        byte[] assembly,
        string typeName)
    {
        using var peReader = new PEReader(
            new MemoryStream(assembly, writable: false));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(
                    peReader,
                    includeAll: true)
                .Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeName);
        return new(type.DefinitionName!);
    }

    private static SourceHouseTarget.MemberTarget MethodTarget(
        byte[] assembly,
        string typeName,
        string methodName)
    {
        using var peReader = new PEReader(
            new MemoryStream(assembly, writable: false));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(
                    peReader,
                    includeAll: true)
                .Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeName);
        ApiMember method = Assert.Single(
            type.Members,
            candidate =>
                candidate.Name == methodName
                && candidate.MetadataToken is not null);
        return new(
            type.DefinitionName!,
            ApiMemberIdentity.GetMemberAnchor(type, method),
            method.MetadataToken!.Value);
    }

    private static SourceHouseTarget.MemberTarget AccessorTarget(
        byte[] assembly,
        string typeName,
        string accessorName)
    {
        using var peReader = new PEReader(
            new MemoryStream(assembly, writable: false));
        ApiType type = Assert.Single(
            ApiSurfaceExtractor.Extract(
                    peReader,
                    includeAll: true)
                .Types,
            candidate =>
                candidate.DefinitionName?.ToMetadataFullName()
                    == typeName);
        ApiMember accessor = Assert.Single(
            type.Members.SelectMany(
                owner => ApiMemberAccessors.Create(owner, type)),
            candidate =>
                candidate.Name == accessorName
                && candidate.MetadataToken is not null);
        return new(
            type.DefinitionName!,
            ApiMemberIdentity.GetMemberAnchor(type, accessor),
            accessor.MetadataToken!.Value);
    }

    public enum PhysicalAssociationMutation
    {
        Request,
        Library,
        SelectedAssembly,
        OperationPlan,
        PolicyGeneration,
        Issuer,
        Profile,
        ModuleDigest,
        ModuleVersionId,
        MetadataTarget,
        XmlDocumentationIdentity,
        SourceResult,
        SourceInput,
        SourceDigest,
        SourceEncoding,
    }

    private sealed class IdentitylessAttestationCapability(
        SourceHouseBuildAttestation inner)
        : ISourceHousePhysicalDeclarationCapability
    {
        public int AttestationReads { get; private set; }
        public SourceHouseCapabilityIdentity Identity =>
            inner.Identity;
        public SourceHouseCapabilityCategory Category =>
            inner.Category;
        public SourceHouseAttestationIssuerIdentity Issuer =>
            inner.Issuer;
        public SourceHouseAttestationProfileIdentity Profile =>
            inner.Profile;
        public SourceHouseAttestationGeneration Generation =>
            inner.Generation;

        public async ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            SourceHouseCapabilityOutcome outcome =
                await inner.ReadAsync(
                    candidate,
                    maximumBytes,
                    cancellationToken);
            SourceHouseCapabilityOutcome.Available available =
                Assert.IsType<
                    SourceHouseCapabilityOutcome.Available>(
                        outcome);
            return new SourceHouseCapabilityOutcome.Available(
                available.Bytes.AsSpan(),
                observation: available.Observation);
        }

        public ValueTask<SourceHouseAttestationCapabilityOutcome>
            ReadAttestationsAsync(
                SourceHousePhysicalDeclarationRequest request,
                int maximumContributions,
                CancellationToken cancellationToken)
        {
            AttestationReads++;
            return inner.ReadAttestationsAsync(
                request,
                maximumContributions,
                cancellationToken);
        }
    }

    private sealed class DelegatingAttestationCapability
        : ISourceHousePhysicalDeclarationCapability
    {
        private readonly SourceHouseBuildAttestation _inner;
        private readonly Func<
            SourceHousePhysicalDeclarationRequest,
            int,
            CancellationToken,
            ValueTask<SourceHouseAttestationCapabilityOutcome>>
            _readAttestations;

        internal DelegatingAttestationCapability(
            SourceHouseBuildAttestation inner,
            Func<
                SourceHousePhysicalDeclarationRequest,
                int,
                CancellationToken,
                ValueTask<SourceHouseAttestationCapabilityOutcome>>?
                readAttestations = null,
            SourceHouseAttestationGeneration? generation = null)
        {
            _inner = inner;
            _readAttestations =
                readAttestations
                ?? inner.ReadAttestationsAsync;
            Generation = generation ?? inner.Generation;
        }

        public SourceHouseCapabilityIdentity Identity =>
            _inner.Identity;
        public SourceHouseCapabilityCategory Category =>
            _inner.Category;
        public SourceHouseAttestationIssuerIdentity Issuer =>
            _inner.Issuer;
        public SourceHouseAttestationProfileIdentity Profile =>
            _inner.Profile;
        public SourceHouseAttestationGeneration Generation { get; }

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken) =>
            _inner.ReadAsync(
                candidate,
                maximumBytes,
                cancellationToken);

        public ValueTask<SourceHouseAttestationCapabilityOutcome>
            ReadAttestationsAsync(
                SourceHousePhysicalDeclarationRequest request,
                int maximumContributions,
                CancellationToken cancellationToken) =>
            _readAttestations(
                request,
                maximumContributions,
                cancellationToken);
    }
}
