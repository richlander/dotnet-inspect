using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

using CSharpText;
using DotnetInspector.Libraries;
using DotnetInspector.SourceHouse.BuildAttestation;
using ILInspector.Metadata;
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

    [Fact]
    public async Task
        PhysicalDeclaration_OutOfBoundsSpanIsRejected()
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
                        Copy(
                            exact,
                            span: new(
                                request.Source.RawUtf16Length,
                                1)),
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
        PhysicalDeclaration_TargetIncompatibleSyntaxKindIsRejected()
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
                        Copy(
                            exact,
                            syntaxKind:
                                SourceHouseDeclarationSyntaxKind
                                    .Create(
                                        "ClassDeclaration")),
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
    public void
        BuildAttestor_ByteIdenticalInputsRetainDistinctOpaqueIdentities()
    {
        byte[] identicalBytes = Encoding.UTF8.GetBytes(
            "// distinct physical compiler inputs");
        SourceHouseBuildAttestation attestation =
            Assert.IsType<CSharpBuildAttestationOutcome.Available>(
                CSharpBuildAttestor.EmitAndAttest(
                    new(
                        "IdenticalPhysicalInputsFixture",
                        [
                            new("/fixture/First.cs", identicalBytes),
                            new("/fixture/Second.cs", identicalBytes),
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

        Assert.Equal(2, physicalInputs.Length);
        Assert.True(
            physicalInputs[0].ContentDigest.Matches(
                physicalInputs[1].ContentDigest));
        Assert.NotSame(
            physicalInputs[0].Identity,
            physicalInputs[1].Identity);
        Assert.NotEqual(
            physicalInputs[0].Path,
            physicalInputs[1].Path);
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
        SourceHousePhysicalDeclarationSpan? span = null,
        SourceHouseAttestationGeneration? generation = null,
        SourceHouseDeclarationSyntaxKind? syntaxKind = null) =>
        new(
            source.Request,
            source.Library,
            source.SelectedAssembly,
            source.OperationPlan,
            source.PolicyGeneration,
            source.Issuer,
            source.Profile,
            generation ?? source.Generation,
            source.ModuleDigest,
            source.Target,
            source.XmlDocumentationIdentity,
            source.SourceResult,
            source.SourceInput,
            source.SourceDigest,
            source.SourceEncoding,
            span ?? source.Span,
            syntaxKind ?? source.SyntaxKind);

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
