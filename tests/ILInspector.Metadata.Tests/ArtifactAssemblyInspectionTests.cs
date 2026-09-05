using System.Collections.Immutable;
using System.Reflection;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;

namespace ILInspector.Metadata.Tests;

public sealed class ArtifactAssemblyInspectionTests
{
    static CancellationToken Token => TestContext.Current.CancellationToken;
    static readonly Guid Mvid = new("90452082-aebc-4d57-b379-54fa790a66da");

    [Fact]
    public void AdmissionProjection_BindsExactArtifactIdentityAssemblyRegistrationIdentityAndMvid()
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();

        Assert.Same(artifact.Contribution.Registration.Artifact, projection.Registration.Artifact);
        Assert.Same(artifact.Owner.Generation, projection.Registration.Generation);
        Assert.Same(projection.Registration.Artifact.Generation, projection.Registration.Generation);
        Assert.Equal(Mvid, projection.Registration.ModuleVersionId);
        Assert.Equal(new AssemblyReferenceIdentity("ArtifactBound", new Version(1, 0, 0, 0), null, null),
            projection.Identity);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("disposed")]
    [InlineData("revoked")]
    [InlineData("ended")]
    public void AdmissionProjection_MapsUnauthorizedAuthorityWithoutInvokingCallback(string state)
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        using var foreign = new RetainedImage(AssemblyBytes());
        if (state == "disposed")
            artifact.AdmissionLease.Dispose();
        if (state == "revoked")
            artifact.Publish();
        if (state == "ended")
            artifact.Owner.EndGeneration();
        int callbacks = 0;
        ArtifactAssemblyProjectionOutcome outcome = ArtifactAssemblyProjectionOutcome.FromAccess(
            artifact.Content.WithAdmissionContent(
                state == "missing" ? null : state == "foreign" ? foreign.AdmissionLease : artifact.AdmissionLease,
                (view, token) =>
                {
                    callbacks++;
                    return ArtifactAssemblyInspection.Project(view, token);
                }, Token));

        Assert.Equal(ArtifactAssemblyProjectionFailureKind.AdmissionUnauthorized,
            Assert.IsType<ArtifactAssemblyProjectionOutcome.Rejected>(outcome).Failure.Kind);
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public void AdmissionProjection_PublicSurfaceCarriesNoProvenanceContentOrLeaseCapability()
    {
        var visited = new HashSet<Type>();
        Visit(typeof(ArtifactAssemblyProjection));

        void Visit(Type type)
        {
            if (!visited.Add(type) || type.IsPrimitive || type.IsEnum
                || type == typeof(string) || type == typeof(Guid) || type == typeof(Version))
                return;
            Assert.False(typeof(Stream).IsAssignableFrom(type));
            Assert.False(typeof(Delegate).IsAssignableFrom(type));
            Assert.False(typeof(IArtifactAccessLease).IsAssignableFrom(type));
            Assert.False(typeof(IArtifactProvenance).IsAssignableFrom(type));
            Assert.NotEqual(typeof(ArtifactAcquisitionRegistration), type);
            Assert.NotEqual(typeof(ArtifactContentReference), type);
            Assert.NotEqual(typeof(RetainedArtifactContent), type);
            Assert.NotEqual(typeof(ArtifactGenerationAuthority), type);
            Assert.False(type.IsArray || type.IsByRefLike);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain("Path", property.Name, StringComparison.OrdinalIgnoreCase);
                Visit(property.PropertyType);
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Visit(field.FieldType);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.ReturnType != typeof(void))
                    Visit(method.ReturnType);
            }
        }
    }

    [Theory]
    [InlineData("WindowsRuntime 1.4")]
    [InlineData("WindowsRuntime 1.4;CLR v4.0.30319")]
    public void AdmissionProjection_RejectsUnsupportedWindowsMetadataBeforeMetadataWork(string version)
    {
        using var artifact = new RetainedImage(WindowsMetadataBytes(version));
        var outcome = ArtifactAssemblyProjectionOutcome.FromAccess(
            artifact.Content.WithAdmissionContent(artifact.AdmissionLease,
                ArtifactAssemblyInspection.Project, Token));

        Assert.Equal(ArtifactAssemblyProjectionFailureKind.UnsupportedWindowsMetadata,
            Assert.IsType<ArtifactAssemblyProjectionOutcome.Rejected>(outcome).Failure.Kind);
    }

    [Theory]
    [InlineData("native", null, ArtifactNonAssemblyKind.NativeImage)]
    [InlineData("module", null, ArtifactNonAssemblyKind.ManagedModule)]
    [InlineData("malformed", ArtifactAssemblyProjectionFailureKind.MalformedMetadata, null)]
    [InlineData("malformed-metadata", ArtifactAssemblyProjectionFailureKind.MalformedMetadata, null)]
    [InlineData("empty", ArtifactAssemblyProjectionFailureKind.MalformedMetadata, null)]
    [InlineData("empty-mvid", ArtifactAssemblyProjectionFailureKind.EmptyModuleVersionId, null)]
    public void AdmissionProjection_ClassifiesNativeModuleMalformedAndEmptyMvid(
        string imageKind,
        ArtifactAssemblyProjectionFailureKind? failure,
        ArtifactNonAssemblyKind? nonAssembly)
    {
        using var artifact = new RetainedImage(ImageBytes(imageKind));
        ArtifactAssemblyProjectionOutcome outcome = ArtifactAssemblyProjectionOutcome.FromAccess(
            artifact.Content.WithAdmissionContent(artifact.AdmissionLease,
                ArtifactAssemblyInspection.Project, Token));
        if (failure is not null)
            Assert.Equal(failure.Value, Assert.IsType<ArtifactAssemblyProjectionOutcome.Rejected>(outcome).Failure.Kind);
        else
            Assert.Equal(nonAssembly!.Value, Assert.IsType<ArtifactAssemblyProjectionOutcome.NotAssembly>(outcome).Kind);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("disposed")]
    [InlineData("revoked")]
    [InlineData("replaced")]
    [InlineData("ended")]
    public void QueryValidation_MapsUnauthorizedAuthorityWithoutInvokingCallback(string state)
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();
        artifact.Publish();
        using var foreign = new RetainedImage(AssemblyBytes());
        foreign.Publish();
        if (state == "disposed")
            artifact.QueryLease!.Dispose();
        if (state == "revoked")
            artifact.Owner.Revoke(artifact.QueryAuthorization!);
        if (state == "replaced")
            artifact.Owner.ReplaceQueryAuthorization(artifact.QueryAuthorization!);
        if (state == "ended")
            artifact.Owner.EndGeneration();
        int callbacks = 0;
        int producers = 0;
        var outcome = ArtifactAssemblyQueryOutcome<int>.FromAccess(
            artifact.Content.WithQueryContent(
                state == "missing" ? null : state == "foreign" ? foreign.QueryLease : artifact.QueryLease,
                (view, token) =>
                {
                    callbacks++;
                    return ArtifactAssemblyInspection.Execute(view, projection,
                        (_, _) => ++producers, token);
                }, Token));
        Assert.Equal(ArtifactAssemblyQueryFailureKind.QueryUnauthorized,
            Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Rejected>(outcome).Failure.Kind);
        Assert.Equal(0, callbacks);
        Assert.Equal(0, producers);
    }

    [Fact]
    public void QueryValidation_ConsumesOwnerAttestedArtifactIdentityAndGeneration()
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();
        artifact.Publish();
        var outcome = ArtifactAssemblyQueryOutcome<string>.FromAccess(
            artifact.Content.WithQueryContent(artifact.QueryLease, (view, token) =>
            {
                Assert.Same(projection.Registration.Generation, view.Generation);
                Assert.Same(projection.Registration.Artifact, view.Artifact);
                return ArtifactAssemblyInspection.Execute(view, projection,
                    (session, _) => session.IdentityNames().Name, token);
            }, Token));
        Assert.Equal("ArtifactBound", Assert.IsType<ArtifactAssemblyQueryOutcome<string>.Validated>(outcome).Value);
    }

    [Fact]
    public void QueryValidation_AcceptsExactRetainedImageInsideCallbackWithoutRebinding()
    {
        byte[] source = AssemblyBytes();
        using var artifact = new RetainedImage(source);
        ArtifactAssemblyProjection projection = artifact.Project();
        AssemblyProjectionRegistration registration = projection.Registration;
        artifact.Publish();
        Array.Clear(source);
        AssemblyInspectionSession? borrowed = null;
        MethodBodySource? bodies = null;
        var outcome = artifact.Execute(projection, (session, _) =>
        {
            borrowed = session;
            bodies = session.MethodBodies;
            GC.Collect();
            return session.IdentityNames().Name;
        });
        Assert.Equal("ArtifactBound", Assert.IsType<ArtifactAssemblyQueryOutcome<string>.Validated>(outcome).Value);
        Assert.Same(registration, projection.Registration);
        Assert.Equal(Mvid, registration.ModuleVersionId);
        Assert.NotNull(borrowed);
        Assert.Throws<ObjectDisposedException>(() => borrowed.HasMetadata);
        Assert.Throws<ObjectDisposedException>(() => borrowed.MethodBodies);
        Assert.NotNull(bodies);
        Assert.Throws<ObjectDisposedException>(() => bodies.EnumerateMethods());
    }

    [Theory]
    [InlineData("WindowsRuntime 1.4")]
    [InlineData("WindowsRuntime 1.4;CLR v4.0.30319")]
    public void QueryValidation_RejectsUnsupportedWindowsMetadataBeforeMetadataWork(string version)
    {
        using var artifact = new RetainedImage(WindowsMetadataBytes(version));
        artifact.Publish();
        ArtifactAssemblyProjection expected = ExpectedProjection(artifact);
        int producers = 0;
        var outcome = artifact.Execute(expected, (_, _) => ++producers);
        Assert.Equal(ArtifactAssemblyQueryFailureKind.UnsupportedWindowsMetadata,
            Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Rejected>(outcome).Failure.Kind);
        Assert.Equal(0, producers);
    }

    [Theory]
    [InlineData("native", null, ArtifactNonAssemblyKind.NativeImage)]
    [InlineData("module", null, ArtifactNonAssemblyKind.ManagedModule)]
    [InlineData("malformed", ArtifactAssemblyQueryFailureKind.MalformedMetadata, null)]
    [InlineData("malformed-metadata", ArtifactAssemblyQueryFailureKind.MalformedMetadata, null)]
    [InlineData("empty", ArtifactAssemblyQueryFailureKind.MalformedMetadata, null)]
    [InlineData("empty-mvid", ArtifactAssemblyQueryFailureKind.EmptyModuleVersionId, null)]
    public void QueryValidation_ClassifiesNativeModuleMalformedAndEmptyMvid(
        string imageKind,
        ArtifactAssemblyQueryFailureKind? failure,
        ArtifactNonAssemblyKind? nonAssembly)
    {
        using var artifact = new RetainedImage(ImageBytes(imageKind));
        artifact.Publish();
        int producers = 0;
        var outcome = artifact.Execute(ExpectedProjection(artifact), (_, _) => ++producers);
        if (failure is not null)
            Assert.Equal(failure.Value, Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Rejected>(outcome).Failure.Kind);
        else
            Assert.Equal(nonAssembly!.Value, Assert.IsType<ArtifactAssemblyQueryOutcome<int>.NotAssembly>(outcome).Kind);
        Assert.Equal(0, producers);
    }

    [Theory]
    [InlineData("generation", ArtifactAssemblyQueryFailureKind.GenerationMismatch)]
    [InlineData("artifact", ArtifactAssemblyQueryFailureKind.ArtifactIdentityMismatch)]
    [InlineData("assembly", ArtifactAssemblyQueryFailureKind.AssemblyIdentityMismatch)]
    [InlineData("mvid", ArtifactAssemblyQueryFailureKind.ModuleVersionIdMismatch)]
    public void QueryValidation_RejectsArtifactGenerationAssemblyIdentityAndMvidMismatch(
        string mismatch,
        ArtifactAssemblyQueryFailureKind failure)
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        using var foreign = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();
        ArtifactIdentity secondIdentity = artifact.Add(AssemblyBytes()).Registration.Artifact;
        artifact.Publish();
        projection = mismatch switch
        {
            "generation" => foreign.Project(),
            "artifact" => projection with
            {
                Registration = projection.Registration with { Artifact = secondIdentity },
            },
            "assembly" => projection with { Identity = projection.Identity with { Name = "Other" } },
            "mvid" => projection with
            {
                Registration = projection.Registration with { ModuleVersionId = Guid.NewGuid() },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch)),
        };
        int producers = 0;
        var outcome = artifact.Execute(projection, (_, _) => ++producers);
        Assert.Equal(failure, Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Rejected>(outcome).Failure.Kind);
        Assert.Equal(0, producers);
    }

    [Fact]
    public void AdmissionProjection_ExactArtifactIdentityIsNonVacuous()
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();
        RetainedArtifactContent second = artifact.Add(AssemblyBytes());
        ArtifactAssemblyProjection other = Assert.IsType<ArtifactAssemblyProjectionOutcome.Projected>(
            ArtifactAssemblyProjectionOutcome.FromAccess(second.WithAdmissionContent(
                artifact.AdmissionLease, ArtifactAssemblyInspection.Project, Token))).Value;
        Assert.Equal(projection.Identity, other.Identity);
        Assert.Equal(projection.Registration.ModuleVersionId, other.Registration.ModuleVersionId);
        Assert.Same(projection.Registration.Generation, other.Registration.Generation);
        Assert.NotSame(projection.Registration.Artifact, other.Registration.Artifact);
        artifact.Publish();
        var outcome = ArtifactAssemblyQueryOutcome<int>.FromAccess(
            second.WithQueryContent(artifact.QueryLease,
                (view, token) => ArtifactAssemblyInspection.Execute<int>(view, projection,
                    (_, _) => throw new InvalidOperationException("Must not execute."), token), Token));
        Assert.Equal(ArtifactAssemblyQueryFailureKind.ArtifactIdentityMismatch,
            Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Rejected>(outcome).Failure.Kind);
    }

    [Fact]
    public async Task ArtifactAdmission_ProjectsAssembliesThroughAuthorizedLease()
    {
        await using var artifacts = new ArtifactSetSession();
        byte[] source = AssemblyBytes();
        int opens = 0;
        ArtifactIdentity? identity = null;
        await artifacts.AddRequiredAcquisitionAsync((scope, _) =>
        {
            ArtifactContribution contribution = scope.Register(new Provenance(), _ =>
            {
                opens++;
                return new MemoryStream(source, writable: false);
            });
            identity = contribution.Descriptor.Identity;
            return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                new ArtifactAcquisitionOutcome.Acquired([contribution], ArtifactAcquisitionLeases.None));
        }, cancellationToken: Token);
        ArtifactAssemblyProjection? projection = null;
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await artifacts.SealWithProjectionAsync((view, token) =>
            {
                projection = Assert.IsType<ArtifactAssemblyProjectionOutcome.Projected>(
                    ArtifactAssemblyInspection.Project(view, token)).Value;
                Assert.Throws<InvalidOperationException>(() => artifacts.CreateQueryAuthorization());
                return null;
            }, Token));
        Assert.NotNull(projection);
        Assert.Same(identity, projection.Registration.Artifact);
        using ArtifactQueryLease lease = artifacts.IssueLease(artifacts.CreateQueryAuthorization());
        var outcome = ArtifactAssemblyQueryOutcome<string>.FromAccess(
            artifacts.WithQueryContent(identity!, lease, (view, token) =>
                ArtifactAssemblyInspection.Execute(view, projection,
                    (session, _) => session.IdentityNames().Name, token), Token));
        Assert.Equal("ArtifactBound", Assert.IsType<ArtifactAssemblyQueryOutcome<string>.Validated>(outcome).Value);
        Assert.Equal(1, opens);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unsupported")]
    [InlineData("unauthorized")]
    [InlineData("overflow")]
    [InlineData("cancelled")]
    public void QueryValidation_PreservesProducerExceptionsAndDisposesSession(string kind)
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();
        artifact.Publish();
        using var cancellation = new CancellationTokenSource();
        Exception primary = kind switch
        {
            "malformed" => new BadImageFormatException("producer"),
            "unsupported" => new UnsupportedMetadataFormatException(),
            "unauthorized" => new UnauthorizedAccessException("producer"),
            "overflow" => new OverflowException("producer"),
            _ => new OperationCanceledException(cancellation.Token),
        };
        AssemblyInspectionSession? borrowed = null;
        Exception? actual = Record.Exception(() => artifact.Execute<int>(projection, (session, _) =>
        {
            borrowed = session;
            throw primary;
        }));
        Assert.Same(primary, actual);
        Assert.NotNull(borrowed);
        Assert.Throws<ObjectDisposedException>(() => borrowed.HasMetadata);
        Assert.True(artifact.Owner.EndGenerationAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public void QueryValidation_CancellationAfterProducerDoesNotPublishAResult()
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();
        artifact.Publish();
        using var cancellation = new CancellationTokenSource();
        AssemblyInspectionSession? borrowed = null;
        var failure = Assert.Throws<OperationCanceledException>(() =>
            ArtifactAssemblyQueryOutcome<int>.FromAccess(
                artifact.Content.WithQueryContent(artifact.QueryLease, (view, token) =>
                    ArtifactAssemblyInspection.Execute(view, projection, (session, _) =>
                    {
                        borrowed = session;
                        cancellation.Cancel();
                        return 1;
                    }, token), cancellation.Token)));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.NotNull(borrowed);
        Assert.Throws<ObjectDisposedException>(() => borrowed.HasMetadata);
        Assert.True(artifact.Owner.EndGenerationAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public void QueryValidation_UsesEquivalentAssemblyIdentity()
    {
        using var artifact = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = artifact.Project();
        artifact.Publish();
        projection = projection with
        {
            Identity = projection.Identity with
            {
                Name = "ARTIFACTBOUND",
                Culture = "neutral",
                PublicKeyToken = "",
            },
        };
        Assert.Equal(1, Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Validated>(
            artifact.Execute(projection, (_, _) => 1)).Value);
    }

    [Theory]
    [InlineData("generation", ArtifactAssemblyQueryFailureKind.GenerationMismatch)]
    [InlineData("artifact", ArtifactAssemblyQueryFailureKind.ArtifactIdentityMismatch)]
    [InlineData("assembly", ArtifactAssemblyQueryFailureKind.AssemblyIdentityMismatch)]
    public void QueryValidation_PreservesMismatchPrecedence(
        string mismatch,
        ArtifactAssemblyQueryFailureKind failure)
    {
        using var artifact = new RetainedImage(
            mismatch == "assembly" ? ImageBytes("empty-mvid") : ImageBytes("malformed"));
        using var foreign = new RetainedImage(AssemblyBytes());
        ArtifactAssemblyProjection projection = mismatch switch
        {
            "generation" => foreign.Project(),
            "artifact" => ExpectedProjection(artifact) with
            {
                Registration = new(artifact.Owner.Generation,
                    artifact.Add(AssemblyBytes()).Registration.Artifact, Mvid),
            },
            _ => ExpectedProjection(artifact) with
            {
                Identity = new("Other", new Version(1, 0, 0, 0), null, null),
            },
        };
        artifact.Publish();
        int producers = 0;
        var outcome = artifact.Execute(projection, (_, _) => ++producers);
        Assert.Equal(failure, Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Rejected>(outcome).Failure.Kind);
        Assert.Equal(0, producers);
    }

    static byte[] AssemblyBytes() =>
        InspectionAcquisitionPlanTests.BuildSimpleAssembly("ArtifactBound", "Type", Mvid);

    static byte[] ImageBytes(string kind) => kind switch
    {
        "native" => InspectionAcquisitionPlanTests.BuildNativePeImage(),
        "module" => InspectionAcquisitionPlanTests.BuildModuleImage(),
        "malformed" => [1, 2, 3],
        "malformed-metadata" => TruncatedMetadataBytes("v4.0.30319"),
        "empty" => [],
        "empty-mvid" => InspectionAcquisitionPlanTests.BuildSimpleAssembly("ArtifactBound", "Type", Guid.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    static byte[] WindowsMetadataBytes(string version) => TruncatedMetadataBytes(version);

    static byte[] TruncatedMetadataBytes(string version)
    {
        byte[] image = MetadataFormatAdmissionTests.BuildImage(version);
        MetadataFormatAdmissionTests.TruncateMetadataAfterVersionField(image);
        return image;
    }

    static ArtifactAssemblyProjection ExpectedProjection(RetainedImage image) =>
        new(new(image.Owner.Generation, image.Contribution.Registration.Artifact, Mvid),
            new("ArtifactBound", new Version(1, 0, 0, 0), null, null));

    sealed record Provenance : IArtifactProvenance;

    sealed class RetainedImage : IDisposable
    {
        public ArtifactGenerationAuthority Owner { get; } = new();
        public ArtifactAdmissionAuthorization Admission { get; }
        public ArtifactAdmissionLease AdmissionLease { get; }
        public ArtifactContribution Contribution { get; }
        public RetainedArtifactContent Content { get; }
        public ArtifactQueryAuthorization? QueryAuthorization { get; private set; }
        public ArtifactQueryLease? QueryLease { get; private set; }

        public RetainedImage(byte[] image)
        {
            Admission = Owner.CreateAdmissionAuthorization();
            AdmissionLease = Owner.IssueLease(Admission);
            using ArtifactContributionScope scope = Owner.BeginContribution(Admission);
            Contribution = scope.Register(new Provenance(),
                _ => throw new InvalidOperationException("No source reopen is permitted."));
            Content = Owner.CreateRetainedContent(Contribution.Registration, ImmutableArray.Create(image));
        }

        public RetainedArtifactContent Add(byte[] image)
        {
            using ArtifactContributionScope scope = Owner.BeginContribution(Admission);
            ArtifactContribution contribution = scope.Register(new Provenance(),
                _ => throw new InvalidOperationException("No source reopen is permitted."));
            return Owner.CreateRetainedContent(contribution.Registration, ImmutableArray.Create(image));
        }

        public ArtifactAssemblyProjection Project() =>
            Assert.IsType<ArtifactAssemblyProjectionOutcome.Projected>(
                ArtifactAssemblyProjectionOutcome.FromAccess(
                    Content.WithAdmissionContent(AdmissionLease, ArtifactAssemblyInspection.Project, Token))).Value;

        public void Publish()
        {
            Owner.CompleteAdmission(Admission);
            QueryAuthorization = Owner.CreateQueryAuthorization();
            QueryLease = Owner.IssueLease(QueryAuthorization);
        }

        public ArtifactAssemblyQueryOutcome<TResult> Execute<TResult>(
            ArtifactAssemblyProjection projection,
            Func<AssemblyInspectionSession, CancellationToken, TResult> producer) =>
            ArtifactAssemblyQueryOutcome<TResult>.FromAccess(
                Content.WithQueryContent(QueryLease,
                    (view, token) => ArtifactAssemblyInspection.Execute(view, projection, producer, token), Token));

        public void Dispose()
        {
            QueryLease?.Dispose();
            AdmissionLease.Dispose();
            Owner.EndGeneration();
        }
    }
}
