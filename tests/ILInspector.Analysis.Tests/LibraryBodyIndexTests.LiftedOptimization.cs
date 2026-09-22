using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    [Fact]
    public void
        OptimizationOpportunities_AuthoredIntrinsicRowsSurviveMalformedOwnershipAcrossScopes()
    {
        string path = typeof(MalformedAsyncOwnershipFixture)
            .Assembly.Location;
        LibraryBodyIndex full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures
                .OptimizationOpportunities);
        MethodIdentity poisonedGeneric = Assert.Single(
            full.Methods,
            method => method.Name
                == nameof(
                    MalformedAsyncOwnershipFixture
                        .PoisonedGenericBoxEquals));
        MethodIdentity poisonedInt = Assert.Single(
            full.Methods,
            method => method.Name
                == nameof(
                    MalformedAsyncOwnershipFixture
                        .PoisonedBoxedInt));
        MethodIdentity cleanGeneric = Assert.Single(
            full.Methods,
            method => method.Name
                == nameof(
                    MalformedAsyncOwnershipFixture
                        .CleanGenericBoxEquals));
        MethodIdentity cleanInt = Assert.Single(
            full.Methods,
            method => method.Name
                == nameof(
                    MalformedAsyncOwnershipFixture
                        .CleanBoxedInt));
        Assert.False(
            CompilerGeneratedNames.RequiresDeclaredOwner(
                poisonedGeneric));
        Assert.Contains(
            full.Diagnostics,
            diagnostic => diagnostic.MethodToken
                == poisonedGeneric.MetadataToken);

        foreach (LibraryBodyIndex index in new[]
        {
            full,
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    poisonedGeneric.MetadataToken,
                    poisonedInt.MetadataToken,
                    cleanGeneric.MetadataToken,
                    cleanInt.MetadataToken,
                }),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        poisonedGeneric.DeclaringType)),
        })
        {
            Assert.Contains(
                index.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape
                        == "generic-parameter-object-box"
                    && opportunity.Method.MetadataToken
                        == poisonedGeneric.MetadataToken);
            Assert.Contains(
                index.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape == "box-value-type"
                    && opportunity.Method.MetadataToken
                        == poisonedInt.MetadataToken);
            Assert.Contains(
                index.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape
                        == "generic-parameter-object-box"
                    && opportunity.Method.MetadataToken
                        == cleanGeneric.MetadataToken);
            Assert.Contains(
                index.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape == "box-value-type"
                    && opportunity.Method.MetadataToken
                        == cleanInt.MetadataToken);
        }
    }

    [Fact]
    public void OptimizationOpportunities_GenericObjectEqualsInLocalFunction_IsReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        var row = Assert.Single(index.OptimizationOpportunities.Where(o =>
            o.Method.Name.Contains(
                nameof(OptimizationOpportunityFixtures.GenericObjectEqualsLocalFunction),
                StringComparison.Ordinal)
            && o.Shape == "generic-parameter-object-box"));
        Assert.Contains("value-type instantiations", row.Caveat);
        Assert.Equal(
            nameof(OptimizationOpportunityFixtures.GenericObjectEqualsLocalFunction),
            row.SourceOwner?.Name);
    }

    [Fact]
    public void
        OptimizationOpportunities_UnresolvedLiftedOwnerDoesNotProjectGeneratedBoxing()
    {
        byte[] image = File.ReadAllBytes(
            typeof(OptimizationOpportunityFixtures).Assembly.Location);
        const string unresolvedOwner =
            "<ABCDEFGHIJKLMNOPQRSTUVWX>b__0_0";
        ReplaceAscii(
            image,
            nameof(OptimizationOpportunityFixtures
                .GenericObjectEqualsLocalFunction),
            unresolvedOwner,
            expectedReplacements: 2);

        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "UnresolvedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);
        MethodIdentity intermediate = Assert.Single(
            full.Methods,
            method => method.Name == unresolvedOwner);
        MethodIdentity evidence = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                $"<{unresolvedOwner}>g__EqualsCore|",
                StringComparison.Ordinal));

        Assert.Null(full.ResolveDeclaredMethod(evidence));
        foreach (LibraryBodyIndex index in new[]
        {
            full,
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "UnresolvedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    intermediate.MetadataToken,
                }),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "UnresolvedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        intermediate.DeclaringType)),
        })
        {
            Assert.DoesNotContain(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "generic-parameter-object-box"
                    && opportunity.Method.MetadataToken
                        == evidence.MetadataToken);
        }
    }

    [Fact]
    public void
        OptimizationOpportunities_ResolvedNestedLiftedOwnerProjectsUltimateOwnerAcrossScopes()
    {
        byte[] image = File.ReadAllBytes(
            typeof(LibraryBodyIndexTests).Assembly.Location);
        ReplaceUniqueAscii(
            image,
            new string('B', 32),
            "<<Ultimate>g__Mid|0_0>g__Box|0_1");
        ReplaceUniqueAscii(
            image,
            new string('A', 20),
            "<Ultimate>g__Mid|0_0");

        LibraryBodyIndex identities =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "ResolvedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity ultimate = Assert.Single(
            identities.Methods,
            method => method.Name == "Ultimate"
                && method.DeclaringType.Name.Contains(
                    nameof(
                        ResolvedNestedLiftedOwnerFixture),
                    StringComparison.Ordinal));
        MethodIdentity intermediate = Assert.Single(
            identities.Methods,
            method => method.Name
                == "<Ultimate>g__Mid|0_0");
        MethodIdentity evidence = Assert.Single(
            identities.Methods,
            method => method.Name
                == "<<Ultimate>g__Mid|0_0>g__Box|0_1");

        foreach (LibraryBodyIndex index in new[]
        {
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "ResolvedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "ResolvedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    evidence.MetadataToken,
                }),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "ResolvedNestedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        ultimate.DeclaringType)),
        })
        {
            OptimizationOpportunity opportunity =
                Assert.Single(
                    index.OptimizationOpportunities,
                    opportunity => opportunity.Shape
                            == "generic-parameter-object-box"
                        && opportunity.Method.MetadataToken
                            == evidence.MetadataToken);
            Assert.Equal(
                ultimate,
                opportunity.SourceOwner);
            Assert.Equal(
                ultimate,
                index.ResolveDeclaredMethod(evidence));
            DirectCall call = Assert.Single(
                index.DirectCalls,
                call => call.EvidenceMethod == evidence
                    && call.Callee.Name == "Equals");
            Assert.Equal(
                ultimate,
                call.Caller);
        }

        foreach (int excludedScope in new[]
        {
            ultimate.MetadataToken,
            intermediate.MetadataToken,
        })
        {
            LibraryBodyIndex index =
                LibraryBodyIndex.OpenFromPrefetchedImage(
                    "ResolvedNestedLiftedOwner.dll",
                    [.. image],
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities,
                    bodyScope:
                        new HashSet<int>
                        {
                            excludedScope,
                        });
            Assert.DoesNotContain(
                index.OptimizationOpportunities,
                opportunity => opportunity.Method
                    .MetadataToken
                    == evidence.MetadataToken);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void
        OptimizationOpportunities_GeneratedUltimateSuppressesNestedBoxAcrossScopes()
    {
        byte[] image = File.ReadAllBytes(
            typeof(LibraryBodyIndexTests).Assembly.Location);
        string intermediateName =
            "<GeneratedUltimate>g__Mid|0_0";
        string evidenceName =
            $"<{intermediateName}>g__Box|0_1";
        ReplaceUniqueAscii(
            image,
            new string('D', evidenceName.Length),
            evidenceName);
        ReplaceUniqueAscii(
            image,
            new string('C', intermediateName.Length),
            intermediateName);

        LibraryBodyIndex identities =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "GeneratedUltimateBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity ultimate = Assert.Single(
            identities.Methods,
            method => method.Name == "GeneratedUltimate"
                && method.DeclaringType.Name.Contains(
                    nameof(
                        GeneratedUltimateNestedFixture),
                    StringComparison.Ordinal));
        MethodIdentity evidence = Assert.Single(
            identities.Methods,
            method => method.Name == evidenceName);

        foreach (LibraryBodyIndex index in new[]
        {
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "GeneratedUltimateBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "GeneratedUltimateBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    evidence.MetadataToken,
                }),
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "GeneratedUltimateBox.dll",
                [.. image],
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        ultimate.DeclaringType)),
        })
        {
            Assert.Equal(
                ultimate,
                index.ResolveDeclaredMethod(evidence));
            Assert.DoesNotContain(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "generic-parameter-object-box"
                    && opportunity.Method.MetadataToken
                        == evidence.MetadataToken);
        }
    }

    [Fact]
    public void
        OptimizationOpportunities_GeneratedUltimateSuppressesNestedAsyncAcrossScopes()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyIndex identities = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity ultimate = Assert.Single(
            identities.Methods,
            method => method.Name
                == nameof(
                    ClassicAsyncSiblingFixture
                        .GeneratedUltimateAsyncOwner));
        MethodIdentity child = Assert.Single(
            identities.Methods,
            method => method.Name.StartsWith(
                "<GeneratedUltimateAsyncOwner>g__Child|",
                StringComparison.Ordinal));

        foreach (LibraryBodyIndex index in new[]
        {
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int>
                {
                    ultimate.MetadataToken,
                }),
            LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyTypeScope:
                    type => type.Equals(
                        ultimate.DeclaringType)),
        })
        {
            Assert.Equal(
                ultimate,
                index.ResolveDeclaredMethod(child));
            Assert.Contains(
                index.DirectCalls,
                call => call.Caller == ultimate
                    && call.EvidenceMethod.Name
                        == "MoveNext"
                    && call.Callee.Name
                        == nameof(
                            ClassicAsyncSiblingFixture
                                .GeneratedRead));
            Assert.DoesNotContain(
                index.OptimizationOpportunities,
                opportunity => opportunity.Shape
                        == "sync-call-in-async"
                    && opportunity.Method.MetadataToken
                        == child.MetadataToken);
        }
    }

    [Fact]
    public void DirectCalls_AttributeLiftedBodiesButNotIterators()
    {
        var index = LibraryBodyIndex.Open(
            typeof(OptimizationOpportunityFixtures).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        DirectCall liftedCall = Assert.Single(
            index.DirectCalls,
            call => call.Caller.Name
                    == nameof(OptimizationOpportunityFixtures
                        .GenericObjectEqualsLocalFunction)
                && call.EvidenceMethod.Name.StartsWith(
                    "<GenericObjectEqualsLocalFunction>g__EqualsCore|",
                    StringComparison.Ordinal)
                && call.Callee.Name == "Equals");
        Assert.NotEqual(
            liftedCall.Caller,
            liftedCall.EvidenceMethod);

        DirectCall iteratorCall = Assert.Single(
            index.DirectCalls,
            call => call.Kind == CallKind.NewObject
                && call.Caller.Name == "MoveNext"
                && call.Caller.DeclaringType.Name.Contains(
                    nameof(OptimizationOpportunityFixtures
                        .YieldsPlainObject),
                    StringComparison.Ordinal)
                && !call.Caller.DeclaringType.Name.Contains(
                    nameof(OptimizationOpportunityFixtures
                        .YieldsPlainObjectAsync),
                    StringComparison.Ordinal));
        Assert.Equal(
            iteratorCall.Caller,
            iteratorCall.EvidenceMethod);

        var scoped = LibraryBodyIndex.Open(
            typeof(OptimizationOpportunityFixtures).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                liftedCall.Caller.MetadataToken,
            });
        Assert.Contains(
            scoped.DirectCalls,
            call => call.Caller == liftedCall.Caller
                && call.EvidenceMethod == liftedCall.EvidenceMethod
                && call.Callee == liftedCall.Callee);
        var evidenceScoped = LibraryBodyIndex.Open(
            typeof(OptimizationOpportunityFixtures).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                liftedCall.EvidenceMethod.MetadataToken,
            });
        Assert.Contains(
            evidenceScoped.DirectCalls,
            call => call.Caller == liftedCall.Caller
                && call.EvidenceMethod == liftedCall.EvidenceMethod
                && call.Callee == liftedCall.Callee);
    }

    [Fact]
    public void
        DirectCalls_AttributeAsyncIteratorBodiesToDeclaredSource()
    {
        string path =
            typeof(OptimizationOpportunityFixtures).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(OptimizationOpportunityFixtures
                    .YieldsPlainObjectAsync));
        DirectCall expected = Assert.Single(
            index.DirectCalls,
            call => call.Kind == CallKind.NewObject
                && call.Caller == source
                && call.EvidenceMethod.Name == "MoveNext"
                && call.EvidenceMethod.DeclaringType.Name.Contains(
                    nameof(OptimizationOpportunityFixtures
                        .YieldsPlainObjectAsync),
                    StringComparison.Ordinal));

        Assert.Equal(
            source,
            index.ResolveDeclaredMethod(
                expected.EvidenceMethod));

        var scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                source.MetadataToken,
            });
        Assert.Contains(
            scoped.DirectCalls,
            call => call == expected);
        Assert.Equal(
            source,
            scoped.ResolveDeclaredMethod(
                expected.EvidenceMethod));
    }

    [Fact]
    public void
        DirectCalls_RuntimeAsyncIgnoresAsyncIteratorAttribute()
    {
        byte[] image = File.ReadAllBytes(
            typeof(OptimizationOpportunityFixtures)
                .Assembly.Location);
        SetRuntimeAsyncFlag(
            image,
            reader => Assert.Single(
                reader.MethodDefinitions,
                handle => reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    nameof(OptimizationOpportunityFixtures
                        .YieldsPlainObjectAsync))));

        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncIteratorClaim.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            index.DeclaredMethods,
            method => method.Name
                == nameof(OptimizationOpportunityFixtures
                    .YieldsPlainObjectAsync));
        MethodIdentity moveNext = Assert.Single(
            index.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    source.Name,
                    StringComparison.Ordinal));

        Assert.Contains(
            index.DirectCalls,
            call => call.Kind == CallKind.NewObject
                && call.Caller == moveNext
                && call.EvidenceMethod == moveNext);
        Assert.Null(index.ResolveDeclaredMethod(moveNext));

        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncIteratorClaim.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                });
        Assert.DoesNotContain(
            scoped.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                == moveNext.MetadataToken);
        Assert.Null(
            scoped.ResolveDeclaredMethod(moveNext));
    }

    [Fact]
    public void
        DirectCalls_RuntimeAsyncMoveNextCannotAuthenticateKickoff()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ClassicAsyncSiblingFixture)
                .Assembly.Location);
        SetRuntimeAsyncFlag(
            image,
            reader => Assert.Single(
                reader.MethodDefinitions,
                handle =>
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(handle);
                    if (!reader.StringComparer.Equals(
                            method.Name,
                            "MoveNext"))
                    {
                        return false;
                    }
                    TypeDefinition declaringType =
                        reader.GetTypeDefinition(
                            method.GetDeclaringType());
                    return reader.GetString(
                            declaringType.Name)
                        .Contains(
                            "<CallsSyncSiblingFromAsync>",
                            StringComparison.Ordinal);
                }));

        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncMoveNext.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity source = Assert.Single(
            full.DeclaredMethods,
            method => method.Name
                == nameof(ClassicAsyncSiblingFixture
                    .CallsSyncSiblingFromAsync));
        MethodIdentity moveNext = Assert.Single(
            full.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    source.Name,
                    StringComparison.Ordinal));

        Assert.Null(full.ResolveDeclaredMethod(moveNext));
        Assert.Contains(
            full.DirectCalls,
            call => call.EvidenceMethod == moveNext
                && call.Caller == moveNext);

        MethodIdentity unrelated = Assert.Single(
            full.DeclaredMethods,
            method => method.Name == "ExactPositiveA");
        LibraryBodyIndex unrelatedScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncMoveNext.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    unrelated.MetadataToken,
                });
        Assert.Null(
            unrelatedScoped.ResolveDeclaredMethod(moveNext));

        LibraryBodyIndex sourceScoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "RuntimeAsyncMoveNext.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    source.MetadataToken,
                });
        Assert.Null(
            sourceScoped.ResolveDeclaredMethod(moveNext));
        Assert.DoesNotContain(
            sourceScoped.DirectCalls,
            call => call.EvidenceMethod.MetadataToken
                == moveNext.MetadataToken);
    }

    [Fact]
    public void
        DirectCalls_MalformedIteratorClaimPreservesPhysicalEvidence()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ClassicAsyncSiblingFixture)
                .Assembly.Location);
        const string Owner =
            "ScopedIteratorFinallyAsyncLocalAllocationOwner";
        CorruptStateMachineClaim(
            image,
            Owner);

        LibraryBodyIndex evidence =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedIteratorClaim.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        LibraryBodyIndex opportunities =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedIteratorClaim.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures.Allocations
                    | LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities);

        Assert.Equal(
            evidence.DirectCalls,
            opportunities.DirectCalls);
        Assert.Contains(
            evidence.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "state-machine source",
                StringComparison.Ordinal));
    }

    [Fact]
    public void
        ScopeDiagnosticAggregation_FinalPublicationRetainsMetadataOrder()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ClassicAsyncSiblingFixture)
                .Assembly.Location);
        const string Owner =
            "ScopedIteratorAsyncLocalAllocationOwner";
        CorruptStateMachineClaim(
            image,
            Owner);

        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "OrderedMalformedIteratorClaim.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity moveNext = Assert.Single(
            full.DeclaredMethods,
            method => method.Name == "MoveNext"
                && method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        $"<{Owner}>g__BuildAsync",
                        StringComparison.Ordinal));
        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "OrderedMalformedIteratorClaim.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    moveNext.MetadataToken,
                });

        Assert.True(
            scoped.Diagnostics.Length >= 2,
            string.Join(
                Environment.NewLine,
                scoped.Diagnostics.Select(
                    diagnostic =>
                        $"0x{diagnostic.MethodToken:X8} "
                        + diagnostic.Method)));
        Assert.Equal(
            scoped.Diagnostics
                .OrderBy(diagnostic => diagnostic.MethodToken),
            scoped.Diagnostics);
    }

    [Fact]
    public void
        AsyncSiblingResolution_LiftedFailureRetainsSourceIdentity()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        LibraryBodyIndex full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        const string Owner = "ScopedAsyncLocalAllocationOwner";
        MethodIdentity asyncSource = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                $"<{Owner}>g__BuildAsync|",
                StringComparison.Ordinal));
        MethodIdentity moveNext = Assert.Single(
            full.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType.Name.Contains(
                    asyncSource.Name,
                    StringComparison.Ordinal));

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle moveNextHandle =
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(
                moveNext.MetadataToken);
        MethodDefinition moveNextDefinition =
            reader.GetMethodDefinition(moveNextHandle);
        MethodBodyBlock body = peReader.GetMethodBody(
            moveNextDefinition.RelativeVirtualAddress);
        var instructions = LibraryMethodAnalysisRunner.DecodeBody(
            body.GetILBytes() ?? [],
            body.ExceptionRegions);
        var context = new MethodBodyAnalysisContext(
            moveNext,
            instructions,
            [],
            []);
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodBodyReferenceIndexed: _ =>
                throw new BadImageFormatException(
                    "Injected lifted-owner failure."));
        var infrastructure =
            (ILibraryMethodAnalysisInfrastructure)builder;
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        MethodIdentity? diagnosticSource = null;

        Assert.Throws<BadImageFormatException>(() =>
            infrastructure.CollectAsyncSiblingOpportunities(
                context,
                calls,
                moveNextDefinition,
                typeSourceGenerated: false,
                ref diagnosticSource));

        Assert.Equal(
            asyncSource,
            diagnosticSource);
    }

    [Fact]
    public void
        DirectCalls_ScopedMalformedLiftedOwnerFailsClosed()
    {
        byte[] image = File.ReadAllBytes(
            typeof(ClassicAsyncSiblingFixture)
                .Assembly.Location);
        const string Owner = "ScopedAsyncLocalOwner";
        CorruptStateMachineClaim(
            image,
            Owner);

        LibraryBodyIndex full =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity lifted = full.DeclaredMethods
            .Where(method => method.Name.Contains(
                $"<{Owner}>g__Core",
                StringComparison.Ordinal))
            .OrderBy(method => method.MetadataToken)
            .Last();
        Assert.Null(full.ResolveDeclaredMethod(lifted));

        MethodIdentity scopedOwner = full.DeclaredMethods
            .Where(method => method.Name == Owner)
            .OrderBy(method => method.MetadataToken)
            .Last();
        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedLiftedOwner.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope: new HashSet<int>
                {
                    scopedOwner.MetadataToken,
                });

        Assert.Null(scoped.ResolveDeclaredMethod(lifted));
        Assert.DoesNotContain(
            scoped.DirectCalls,
            call => call.EvidenceMethod == lifted);
        Assert.All(
            scoped.DirectCalls,
            call => Assert.Contains(
                call,
                full.DirectCalls));
    }

    [Fact]
    public void OptimizationOpportunities_LiftedOwnerBody_IsIndexedOnce()
    {
        string path =
            typeof(OptimizationOpportunityFixtures).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle ownerHandle = reader.MethodDefinitions
            .Single(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                nameof(OptimizationOpportunityFixtures.MultipleLiftedFunctions)));
        int indexed = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodBodyReferenceIndexed: handle =>
            {
                if (handle == ownerHandle)
                    Interlocked.Increment(ref indexed);
            });

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: null,
            typeScope: null));

        Assert.Equal(1, indexed);
    }

    [Fact]
    public void OptimizationOpportunities_SourceGeneratedAncestryIsClassifiedOncePerType()
    {
        byte[] image = EmitAssembly(
            "GeneratedAncestry",
            metadata =>
            {
                AssemblyReferenceHandle runtime =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("System.Runtime"),
                        new Version(9, 0, 0, 0),
                        default,
                        default,
                        default,
                        default);
                TypeReferenceHandle attributeType =
                    metadata.AddTypeReference(
                        runtime,
                        metadata.GetOrAddString("Custom"),
                        metadata.GetOrAddString("MarkAttribute"));
                var signature = new BlobBuilder();
                signature.WriteByte(0x20);
                signature.WriteByte(0);
                signature.WriteByte(1);
                MemberReferenceHandle constructor =
                    metadata.AddMemberReference(
                        attributeType,
                        metadata.GetOrAddString(".ctor"),
                        metadata.GetOrAddBlob(signature));
                var value = new BlobBuilder();
                value.WriteUInt16(1);
                value.WriteUInt16(0);
                BlobHandle valueHandle =
                    metadata.GetOrAddBlob(value);

                TypeDefinitionHandle parent = default;
                for (int i = 0; i < 32; i++)
                {
                    TypeDefinitionHandle handle =
                        metadata.AddTypeDefinition(
                            i == 0
                                ? TypeAttributes.Public
                                : TypeAttributes.NestedPublic,
                            i == 0
                                ? metadata.GetOrAddString("N")
                                : default,
                            metadata.GetOrAddString($"A{i}"),
                            default,
                            MetadataTokens.FieldDefinitionHandle(1),
                            MetadataTokens.MethodDefinitionHandle(1));
                    for (int j = 0; j < 16; j++)
                    {
                        metadata.AddCustomAttribute(
                            handle,
                            constructor,
                            valueHandle);
                    }
                    if (!parent.IsNil)
                        metadata.AddNestedType(handle, parent);
                    parent = handle;
                }
                for (int i = 0; i < 1_000; i++)
                {
                    TypeDefinitionHandle leaf =
                        metadata.AddTypeDefinition(
                            TypeAttributes.NestedPublic,
                            default,
                            metadata.GetOrAddString($"L{i}"),
                            default,
                            MetadataTokens.FieldDefinitionHandle(1),
                            MetadataTokens.MethodDefinitionHandle(1));
                    metadata.AddNestedType(leaf, parent);
                }
            });
        using var stream = new MemoryStream(image);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        int classified = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            "GeneratedAncestry.dll",
            reader,
            peReader,
            resolver: null,
            sourceGeneratedTypeClassified:
                _ => Interlocked.Increment(ref classified));

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: null,
            typeScope: null));

        Assert.Equal(reader.TypeDefinitions.Count, classified);
    }

    [Fact]
    public void OptimizationOpportunities_RepeatedMemberRef_IsResolvedOnce()
    {
        string path =
            typeof(OptimizationOpportunityFixtures).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition fixture = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(type => reader.StringComparer.Equals(
                type.Name,
                "MemberRefLiftedOverloadFixture`1"));
        MethodDefinitionHandle ownerHandle = fixture.GetMethods()
            .First(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                "Owner"));
        int resolved = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodReferenceResolved: (method, _) =>
            {
                if (method == ownerHandle)
                    Interlocked.Increment(ref resolved);
            });

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: new HashSet<int>
            {
                MetadataTokens.GetToken(ownerHandle),
            },
            typeScope: null));

        Assert.Equal(1, resolved);
    }

    [Fact]
    public void ScopedLiftedResolution_DoesNotAcquireUnselectedOverload()
    {
        string path =
            typeof(MemberRefLiftedOverloadFixture<>).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition fixture = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(type => reader.StringComparer.Equals(
                type.Name,
                "MemberRefLiftedOverloadFixture`1"));
        MethodDefinitionHandle[] owners = fixture.GetMethods()
            .Where(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                "Owner"))
            .ToArray();
        Assert.Equal(2, owners.Length);
        int selectedIndexed = 0;
        int unselectedIndexed = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodBodyReferenceIndexed: handle =>
            {
                if (handle == owners[0])
                    selectedIndexed++;
                if (handle == owners[1])
                    unselectedIndexed++;
            });

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.MethodEvidence,
            methodScope: new HashSet<int>
            {
                MetadataTokens.GetToken(owners[0]),
            },
            typeScope: null));

        Assert.Equal(1, selectedIndexed);
        Assert.Equal(0, unselectedIndexed);
    }

    [Fact]
    public void
        ScopedAsyncLiftedResolution_DoesNotAcquireUnselectedOverload()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition fixture = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(type => reader.StringComparer.Equals(
                type.Name,
                nameof(ClassicAsyncSiblingFixture)));
        MethodDefinitionHandle[] owners = fixture.GetMethods()
            .Where(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                nameof(ClassicAsyncSiblingFixture
                    .ScopedAsyncLambdaOwner)))
            .ToArray();
        MethodDefinitionHandle selected = owners.Single(handle =>
            (reader.GetMethodDefinition(handle).Attributes
                & MethodAttributes.MemberAccessMask)
            == MethodAttributes.Assembly);
        MethodDefinitionHandle unselected = owners.Single(handle =>
            (reader.GetMethodDefinition(handle).Attributes
                & MethodAttributes.MemberAccessMask)
            == MethodAttributes.Public);
        int selectedIndexed = 0;
        int unselectedIndexed = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodBodyReferenceIndexed: handle =>
            {
                if (handle == selected)
                    selectedIndexed++;
                if (handle == unselected)
                    unselectedIndexed++;
            });

        LibraryBodyAnalysisResult analysis = builder.Build(
            LibraryBodyAnalysisPlan.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence,
                methodScope: new HashSet<int>
                {
                    MetadataTokens.GetToken(selected),
                },
                typeScope: null));

        Assert.Equal(1, selectedIndexed);
        Assert.Equal(0, unselectedIndexed);
        Assert.Contains(
            analysis.Methods.DirectCalls,
            call => call.Caller.MetadataToken
                    == MetadataTokens.GetToken(selected)
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "GetAwaiter");
    }

    [Fact]
    public void
        ScopedAsyncOwnerLiftedResolution_DoesNotAcquireUnselectedOverload()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition fixture = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(type => reader.StringComparer.Equals(
                type.Name,
                nameof(ClassicAsyncSiblingFixture)));
        MethodDefinitionHandle[] owners = fixture.GetMethods()
            .Where(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                nameof(ClassicAsyncSiblingFixture
                    .ScopedAsyncLocalOwner)))
            .ToArray();
        MethodDefinitionHandle selected = owners.Single(handle =>
            (reader.GetMethodDefinition(handle).Attributes
                & MethodAttributes.MemberAccessMask)
            == MethodAttributes.Assembly);
        MethodDefinitionHandle unselected = owners.Single(handle =>
            (reader.GetMethodDefinition(handle).Attributes
                & MethodAttributes.MemberAccessMask)
            == MethodAttributes.Public);
        var full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        int selectedMoveNext = Assert.Single(
            full.DirectCalls,
            call => call.Caller.MetadataToken
                    == MetadataTokens.GetToken(selected)
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "GetAwaiter")
            .EvidenceMethod.MetadataToken;
        int unselectedMoveNext = Assert.Single(
            full.DirectCalls,
            call => call.Caller.MetadataToken
                    == MetadataTokens.GetToken(unselected)
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "GetAwaiter")
            .EvidenceMethod.MetadataToken;
        int selectedIndexed = 0;
        int unselectedIndexed = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodBodyReferenceIndexed: handle =>
            {
                int token = MetadataTokens.GetToken(handle);
                if (token == selectedMoveNext)
                    selectedIndexed++;
                if (token == unselectedMoveNext)
                    unselectedIndexed++;
            });

        LibraryBodyAnalysisResult analysis = builder.Build(
            LibraryBodyAnalysisPlan.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence,
                methodScope: new HashSet<int>
                {
                    MetadataTokens.GetToken(selected),
                },
                typeScope: null));

        Assert.Equal(1, selectedIndexed);
        Assert.Equal(0, unselectedIndexed);
        Assert.Contains(
            analysis.Methods.DirectCalls,
            call => call.Caller.MetadataToken
                    == MetadataTokens.GetToken(selected)
                && call.EvidenceMethod.Name.StartsWith(
                    "<ScopedAsyncLocalOwner>g__Core|",
                    StringComparison.Ordinal)
                && call.Callee.Name
                    == nameof(ClassicAsyncSiblingFixture.ReadValue));
    }

    [Fact]
    public void
        DirectCalls_DirectAsyncLiftedBodyScopeRetainsDeclaredCaller()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        DirectCall expected = Assert.Single(
            full.DirectCalls,
            call => call.Caller.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .ScopedAsyncLambdaOwner)
                && call.Caller.ParameterTypes.Length == 1
                && call.Caller.ParameterTypes[0].Equals(
                    TypeRef.CoreLib("System", "String"))
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "GetAwaiter");

        var scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                expected.EvidenceMethod.MetadataToken,
            });

        Assert.Contains(
            scoped.DirectCalls,
            call => call.Caller == expected.Caller
                && call.EvidenceMethod
                    == expected.EvidenceMethod
                && call.Callee == expected.Callee);
    }

    [Fact]
    public void
        DirectCalls_DirectLiftedTypeScopeRetainsDeclaredCaller()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        DirectCall expected = Assert.Single(
            full.DirectCalls,
            call => call.Caller.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .AwaitTaskInAsyncLambda)
                && call.EvidenceMethod.Name.StartsWith(
                    "<AwaitTaskInAsyncLambda>b__",
                    StringComparison.Ordinal)
                && call.Callee.Name == "Start");

        var scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyTypeScope: type =>
                type.Equals(
                    expected.EvidenceMethod.DeclaringType));

        Assert.Contains(
            scoped.DirectCalls,
            call => call.Caller == expected.Caller
                && call.EvidenceMethod == expected.EvidenceMethod
                && call.Callee == expected.Callee);
    }

    [Fact]
    public void
        DirectCalls_ClosureTypeScopeRetainsAsyncLambdaMoveNext()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        DirectCall expected = Assert.Single(
            full.DirectCalls,
            call => call.Caller.Name
                    == "ScopedCapturingAsyncLambdaOwner"
                && call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "GetAwaiter");
        MethodIdentity kickoff = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                "<ScopedCapturingAsyncLambdaOwner>b__",
                StringComparison.Ordinal));

        var scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyTypeScope:
                type => type.Equals(
                    kickoff.DeclaringType));

        Assert.Contains(
            scoped.DirectCalls,
            call => call.Caller == expected.Caller
                && call.EvidenceMethod
                    == expected.EvidenceMethod
                && call.Callee == expected.Callee);
    }

    [Fact]
    public void
        OptimizationOpportunities_ClosureTypeScopeDoesNotLeakAsyncLambdaOwner()
    {
        const string ownerName =
            "ScopedAsyncLambdaRecommendationOwner";
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(path);
        MethodIdentity owner = Assert.Single(
            full.Methods,
            method => method.Name == ownerName);
        MethodIdentity kickoff = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                $"<{ownerName}>b__",
                StringComparison.Ordinal));
        OptimizationOpportunity opportunity = Assert.Single(
            full.OptimizationOpportunities,
            candidate => candidate.Shape == "sync-call-in-async"
                && candidate.Method == kickoff);
        MethodIdentity evidence = Assert.Single(
            full.Methods,
            method => method.MetadataToken
                == Assert.IsType<int>(
                    opportunity.EvidenceMethodToken));

        Assert.Equal("MoveNext", evidence.Name);
        Assert.Equal(
            owner,
            full.ResolveDeclaredMethod(kickoff));
        Assert.Equal(
            owner,
            full.ResolveDeclaredMethod(evidence));

        DirectCall expectedCall = Assert.Single(
            full.DirectCalls,
            call => call.Caller == owner
                && call.EvidenceMethod == evidence
                && call.Callee.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .ReadValue));
        var scoped = LibraryBodyIndex.Open(
            path,
            bodyTypeScope:
                type => type.Equals(
                    kickoff.DeclaringType));

        Assert.Contains(
            scoped.DirectCalls,
            call => call == expectedCall);
        Assert.Equal(
            owner,
            scoped.ResolveDeclaredMethod(evidence));
        Assert.DoesNotContain(
            scoped.OptimizationOpportunities,
            candidate => candidate.Shape
                    == opportunity.Shape
                && candidate.Method == kickoff
                && candidate.EvidenceMethodToken
                    == opportunity.EvidenceMethodToken);
    }

    [Fact]
    public void
        OptimizationOpportunities_ClosureTypeScopePreservesSuppression()
    {
        const string ownerName =
            "ScopedAllocationHotspotLambdaOwner";
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(path);
        MethodIdentity kickoff = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                $"<{ownerName}>b__",
                StringComparison.Ordinal));
        Assert.True(
            full.GetAllocationOccurrences().TryGetValue(
                kickoff.MetadataToken,
                out ImmutableArray<AllocationOccurrence>
                    fullAllocations));
        Assert.True(
            fullAllocations.Count(
                allocation => allocation.CountsAsHeapAllocation
                    && allocation.InLoop) >= 16);

        Assert.DoesNotContain(
            full.OptimizationOpportunities,
            candidate => candidate.Shape
                    == "allocation-hotspot"
                && candidate.Method == kickoff);
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            candidate => candidate.Method == kickoff);

        var scoped = LibraryBodyIndex.Open(
            path,
            bodyTypeScope:
                type => type.Equals(
                    kickoff.DeclaringType));
        Assert.True(
            scoped.GetAllocationOccurrences().TryGetValue(
                kickoff.MetadataToken,
                out ImmutableArray<AllocationOccurrence>
                    scopedAllocations));
        Assert.Equal(fullAllocations, scopedAllocations);

        Assert.DoesNotContain(
            scoped.OptimizationOpportunities,
            candidate => candidate.Shape
                    == "allocation-hotspot"
                && candidate.Method == kickoff);
        Assert.DoesNotContain(
            scoped.AllocationFanoutOpportunities,
            candidate => candidate.Method == kickoff);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void
        AllocationFanout_TypeScopeAdmittingEveryTypeMatchesFullBuild()
    {
        string path =
            typeof(PdbContext).Assembly.Location;
        var full = LibraryBodyIndex.Open(path);
        var scoped = LibraryBodyIndex.Open(
            path,
            bodyTypeScope: _ => true);

        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name.StartsWith(
                "<EnumerateTypeDocuments>g__AddDocument|",
                StringComparison.Ordinal));
        Assert.Equal(
            full.AllocationFanoutOpportunities,
            scoped.AllocationFanoutOpportunities);
    }

    [Fact]
    public void
        AllocationFanout_TypeScopeAdmittingEveryFixtureTypePreservesAsyncLocals()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(path);
        var scoped = LibraryBodyIndex.Open(
            path,
            bodyTypeScope: _ => true);
        MethodIdentity generatedSource = Assert.Single(
            full.Methods,
            method => method.Name == "StreamAsync"
                && method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "GeneratedAsyncIteratorOwner",
                        StringComparison.Ordinal));
        MethodIdentity generatedMoveNext = Assert.Single(
            full.Methods,
            method => method.Name == "MoveNext"
                && method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "GeneratedAsyncIteratorOwner.<StreamAsync>",
                        StringComparison.Ordinal));
        var generatedSourceScoped =
            LibraryBodyIndex.Open(
                path,
                bodyTypeScope:
                    type => type.Equals(
                        generatedSource.DeclaringType));

        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "ScopedAsyncLocalAllocationOwner",
                        StringComparison.Ordinal));
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "ScopedIteratorAsyncLocalAllocationOwner",
                        StringComparison.Ordinal));
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "ScopedIndirectAsyncLocalAllocationOwner",
                        StringComparison.Ordinal));
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "ScopedNestedAsyncLocalAllocationOwner",
                        StringComparison.Ordinal));
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "ScopedIteratorFinallyAsyncLocalAllocationOwner",
                        StringComparison.Ordinal));
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "ScopedGenericIteratorFinallyAsyncLocalAllocationOwner",
                        StringComparison.Ordinal));
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "GenericIteratorOwner",
                        StringComparison.Ordinal));
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            opportunity => opportunity.Method.Name == "MoveNext"
                && opportunity.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        "GeneratedAsyncIteratorOwner",
                        StringComparison.Ordinal));
        Assert.DoesNotContain(
            scoped.Diagnostics,
            diagnostic => diagnostic.Method.Contains(
                "GeneratedAsyncIteratorOwner",
                StringComparison.Ordinal));
        Assert.Equal(
            generatedSource,
            generatedSourceScoped.ResolveDeclaredMethod(
                generatedMoveNext));
        Assert.Contains(
            generatedSourceScoped.DirectCalls,
            call => call.EvidenceMethod
                == generatedMoveNext);
        Assert.Equal(
            full.AllocationFanoutOpportunities,
            scoped.AllocationFanoutOpportunities);
    }

    [Fact]
    public void
        OptimizationOpportunities_ClosureTypeScopeSuppressesAsyncDerivedRows()
    {
        const string ownerName =
            "ScopedAsyncAllocationHotspotLambdaOwner";
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(path);
        MethodIdentity kickoff = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                $"<{ownerName}>b__",
                StringComparison.Ordinal)
                && method.ReturnType
                    .ToQualifiedDisplayString()
                    .StartsWith(
                        "System.Threading.Tasks.Task<",
                        StringComparison.Ordinal));
        OptimizationOpportunity shaped = Assert.Single(
            full.OptimizationOpportunities,
            candidate => candidate.Shape
                    == "capturing-delegate"
                && candidate.Method.Name == "MoveNext"
                && candidate.Method.DeclaringType
                    .ToQualifiedDisplayString()
                    .Contains(
                        ownerName,
                        StringComparison.Ordinal));
        MethodIdentity evidence = shaped.Method;
        Assert.True(
            full.GetAllocationOccurrences().TryGetValue(
                evidence.MetadataToken,
                out ImmutableArray<AllocationOccurrence>
                    fullAllocations));
        Assert.True(
            fullAllocations.Count(
                allocation => allocation.CountsAsHeapAllocation
                    && allocation.InLoop) >= 16);
        Assert.DoesNotContain(
            full.OptimizationOpportunities,
            candidate => candidate.Shape
                    == "allocation-hotspot"
                && candidate.Method == evidence);
        Assert.Contains(
            full.AllocationFanoutOpportunities,
            candidate => candidate.Method == evidence);

        var scoped = LibraryBodyIndex.Open(
            path,
            bodyTypeScope:
                type => type.Equals(
                    kickoff.DeclaringType));
        Assert.True(
            scoped.GetAllocationOccurrences().TryGetValue(
                evidence.MetadataToken,
                out ImmutableArray<AllocationOccurrence>
                    scopedAllocations));
        Assert.Equal(fullAllocations, scopedAllocations);

        Assert.DoesNotContain(
            scoped.OptimizationOpportunities,
            candidate => candidate.Method == evidence);
        Assert.DoesNotContain(
            scoped.AllocationFanoutOpportunities,
            candidate => candidate.Method == evidence);
    }

    [Fact]
    public void
        ResolveDeclaredMethod_DirectAsyncLiftedKickoffScopeReturnsOwner()
    {
        string path =
            typeof(ClassicAsyncSiblingFixture).Assembly.Location;
        var full = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity owner = Assert.Single(
            full.DeclaredMethods,
            method => method.Name
                    == nameof(ClassicAsyncSiblingFixture
                        .ScopedAsyncLambdaOwner)
                && method.ParameterTypes.Length == 1
                && method.ParameterTypes[0].Equals(
                    TypeRef.CoreLib("System", "String")));
        MethodIdentity kickoff = Assert.Single(
            full.Methods,
            method => method.Name.StartsWith(
                "<ScopedAsyncLambdaOwner>b__",
                StringComparison.Ordinal));
        var scoped = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence,
            bodyScope: new HashSet<int>
            {
                kickoff.MetadataToken,
            });
        DirectCall call = Assert.Single(
            scoped.DirectCalls,
            call => call.EvidenceMethod.Name == "MoveNext"
                && call.Callee.Name == "GetAwaiter");

        Assert.Equal(
            owner,
            scoped.ResolveDeclaredMethod(
                call.EvidenceMethod));
    }

    [Fact]
    public void OptimizationOpportunities_DistinctMethodSpecsShareMemberRefResolution()
    {
        string path =
            typeof(OptimizationOpportunityFixtures).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle ownerHandle = reader.MethodDefinitions
            .Single(handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                nameof(OptimizationOpportunityFixtures
                    .DistinctMethodSpecsWithSharedMemberRef)));
        int emptyResolutions = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            methodReferenceResolved: (method, operand) =>
            {
                if (method != ownerHandle)
                    return;
                EntityHandle handle = MetadataTokens.EntityHandle(operand);
                if (handle.Kind == HandleKind.MemberReference
                    && reader.StringComparer.Equals(
                        reader.GetMemberReference(
                            (MemberReferenceHandle)handle).Name,
                        "Empty"))
                {
                    Interlocked.Increment(ref emptyResolutions);
                }
            });

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: new HashSet<int>
            {
                MetadataTokens.GetToken(ownerHandle),
            },
            typeScope: null));

        Assert.Equal(1, emptyResolutions);
    }

    [Fact]
    public void OptimizationOpportunities_DuplicateMemberRefsResolveStructuralIdentityOnce()
    {
        byte[] image = EmitLiftedMemberReferenceReplayAssembly(
            referenceCount: 100,
            ownerCount: 1,
            parameterCount: 2_000,
            distinctSignatureBlobs: true);

        Assert.Equal(1, CountMethodReferenceResolutions(image));
    }

    [Fact]
    public void OptimizationOpportunities_SharedMemberRefDecodesOnceAcrossOwnerBodies()
    {
        byte[] image = EmitLiftedMemberReferenceReplayAssembly(
            referenceCount: 1,
            ownerCount: 100,
            parameterCount: 2_000);

        Assert.Equal(1, CountMethodReferenceResolutions(image));
    }

    [Theory]
    [InlineData(0x0F, 0x00)]
    [InlineData(0xFF, 0x00)]
    [InlineData(0x1E, 0x7F)]
    public void OptimizationOpportunities_MalformedMethodSpecCannotAuthenticateOwner(
        byte malformedType,
        byte genericParameterIndex)
    {
        byte[] image = File.ReadAllBytes(
            typeof(OptimizationOpportunityFixtures).Assembly.Location);
        int liftedToken;
        using (var stream = new MemoryStream(image, writable: false))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            MethodDefinitionHandle owner = reader.MethodDefinitions
                .Single(handle => reader.StringComparer.Equals(
                    reader.GetMethodDefinition(handle).Name,
                    nameof(OptimizationOpportunityFixtures
                        .GenericObjectEqualsLocalFunction)));
            MethodDefinition definition =
                reader.GetMethodDefinition(owner);
            MethodBodyBlock body =
                peReader.GetMethodBody(
                    definition.RelativeVirtualAddress);
            MethodSpecificationHandle specification =
                LibraryMethodAnalysisRunner.DecodeBody(
                        body.GetILBytes() ?? [],
                        body.ExceptionRegions)
                    .Instructions
                    .Select(instruction =>
                        MetadataTokens.EntityHandle(
                            MethodInstructionFacts.OperandInt32(
                                instruction)))
                    .Where(handle =>
                        handle.Kind
                            == HandleKind.MethodSpecification)
                    .Select(handle =>
                        (MethodSpecificationHandle)handle)
                    .Single(handle =>
                    {
                        EntityHandle method =
                            reader.GetMethodSpecification(handle).Method;
                        return method.Kind == HandleKind.MethodDefinition
                            && reader.GetString(
                                reader.GetMethodDefinition(
                                    (MethodDefinitionHandle)method).Name)
                                .StartsWith(
                                    "<GenericObjectEqualsLocalFunction>"
                                        + "g__EqualsCore|",
                                    StringComparison.Ordinal);
                    });
            EntityHandle lifted =
                reader.GetMethodSpecification(specification).Method;
            Assert.Equal(
                HandleKind.MethodDefinition,
                lifted.Kind);
            liftedToken = MetadataTokens.GetToken(lifted);
            BlobHandle signature =
                reader.GetMethodSpecification(specification).Signature;
            int blobStream = MetadataStreamOffset(
                image,
                peReader.PEHeaders.MetadataStartOffset,
                "#Blob");
            int blob = blobStream
                + MetadataTokens.GetHeapOffset(signature);
            Assert.Equal(4, image[blob]);
            Assert.Equal(0x1E, image[blob + 3]);
            image[blob + 3] = malformedType;
            image[blob + 4] = genericParameterIndex;
        }

        LibraryBodyIndex index =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedMethodSpec.dll",
                ImmutableArray.Create(image),
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities);

        Assert.DoesNotContain(
            index.OptimizationOpportunities,
            row =>
                row.Shape == "generic-parameter-object-box"
                && row.Method.Name.Contains(
                    nameof(OptimizationOpportunityFixtures
                        .GenericObjectEqualsLocalFunction),
                    StringComparison.Ordinal));
        Assert.NotEmpty(index.Diagnostics);

        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "MalformedMethodSpec.dll",
                ImmutableArray.Create(image),
                LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
                bodyScope: new HashSet<int> { liftedToken });
        Assert.Single(
            scoped.Diagnostics.Where(
                diagnostic => diagnostic.Method.Contains(
                    "<GenericObjectEqualsLocalFunction>g__EqualsCore|",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void
        ScopedLiftedResolution_NestedFailurePublishesOneDiagnostic()
    {
        byte[] image =
            BuildNestedLiftedInvalidAsyncSourceAssembly(
                out int sourceToken,
                out int liftedToken);

        LibraryBodyIndex scoped =
            LibraryBodyIndex.OpenFromPrefetchedImage(
                "NestedLiftedInvalidAsyncSource.dll",
                [.. image],
                LibraryBodyAnalysisFeatures.MethodEvidence,
                bodyScope:
                    new HashSet<int> { liftedToken });

        AnalysisDiagnostic diagnostic = Assert.Single(
            scoped.Diagnostics.Where(
                candidate =>
                    candidate.MethodToken == liftedToken));
        Assert.Contains(
            "<>c::<BadSourceLambda>b__0_0",
            diagnostic.Method,
            StringComparison.Ordinal);

        TypeRef sourceType =
            TypeRef.Definition(
                "NestedLiftedInvalidAsyncSource",
                "Sample",
                "Source");
        AnalysisDiagnostic enriched = diagnostic with
        {
            SourceMethodToken = sourceToken,
            SourceDeclaringType = sourceType,
        };
        using var stream =
            new MemoryStream(
                image,
                writable: false);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        using var builder = new LibraryBodyAnalysisBuilder(
            "NestedLiftedInvalidAsyncSource.dll",
            reader,
            peReader);
        LibraryBodyAnalysisPlan plan =
            LibraryBodyAnalysisPlan.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence,
                new HashSet<int> { liftedToken },
                typeScope: null)
            with
            {
                ScopeExpansionDiagnostics = [enriched],
            };

        LibraryBodyAnalysisResult result =
            builder.Build(plan);

        Assert.Equal(
            enriched,
            Assert.Single(
                result.Diagnostics.Where(
                    candidate =>
                        candidate.MethodToken == liftedToken)));
    }

    [Fact]
    public void
        ScopeDiagnosticAggregation_EnrichesFailuresInMetadataOrder()
    {
        TypeRef evidenceType =
            TypeRef.Definition(
                "Fixture",
                "Sample",
                "<Source>d__1");
        TypeRef sourceType =
            TypeRef.Definition(
                "Fixture",
                "Sample",
                "Source");
        var sparse = new AnalysisDiagnostic(
            0x06000002,
            "Sample.<Source>d__1::MoveNext()",
            "BadImageFormatException: Invalid attribute");
        var enriched = sparse with
        {
            SourceMethodToken = 0x06000001,
            DeclaringType = evidenceType,
            SourceDeclaringType = sourceType,
        };
        var later = new AnalysisDiagnostic(
            0x06000003,
            "Sample.Source::Later()",
            "BadImageFormatException: Invalid signature");
        var earlier = new AnalysisDiagnostic(
            0x06000001,
            "Sample.Source::Source()",
            "BadImageFormatException: Invalid body");

        ImmutableArray<AnalysisDiagnostic> diagnostics =
            AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    [sparse, later],
                    [enriched, earlier]);

        Assert.Equal(
            new[]
            {
                0x06000001,
                0x06000002,
                0x06000003,
            },
            diagnostics.Select(
                diagnostic => diagnostic.MethodToken));
        Assert.Equal(enriched, diagnostics[1]);
    }

    [Fact]
    public void
        ScopeDiagnosticAggregation_PreservesConflictingProvenance()
    {
        var first = new AnalysisDiagnostic(
            0x06000003,
            "Sample.<Source>d__1::MoveNext()",
            "BadImageFormatException: Invalid attribute",
            SourceMethodToken: 0x06000001);
        var second = first with
        {
            SourceMethodToken = 0x06000002,
        };

        ImmutableArray<AnalysisDiagnostic> diagnostics =
            AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    [first],
                    [second]);

        Assert.Equal(
            new int?[]
            {
                0x06000001,
                0x06000002,
            },
            diagnostics.Select(
                diagnostic =>
                    diagnostic.SourceMethodToken));
    }

    [Fact]
    public void
        ScopeDiagnosticAggregation_UsesStructuralTypeProvenanceCompatibility()
    {
        TypeRef legacy =
            TypeRef.Definition(
                "Fixture",
                "Sample",
                "Value");
        TypeRef exact =
            ExactDefinition(
                "Value",
                "Value");
        TypeRef exactLiteral =
            ExactDefinition(
                "Outer+Inner",
                "Outer+Inner");
        TypeRef exactNested =
            ExactDefinition(
                "Outer+Inner",
                "Outer",
                "Inner");
        var compatibleLegacy = new AnalysisDiagnostic(
            0x06000003,
            "Sample.<Source>d__1::MoveNext()",
            "Compatible provenance",
            DeclaringType: legacy,
            SourceDeclaringType: exact);
        AnalysisDiagnostic compatibleExact =
            compatibleLegacy with
            {
                DeclaringType = exact,
                SourceDeclaringType = legacy,
            };
        AnalysisDiagnostic declaringLiteral =
            compatibleLegacy with
            {
                Message = "Declaring type conflict",
                DeclaringType = exactLiteral,
                SourceDeclaringType = null,
            };
        AnalysisDiagnostic declaringNested =
            declaringLiteral with
            {
                DeclaringType = exactNested,
            };
        AnalysisDiagnostic sourceLiteral =
            compatibleLegacy with
            {
                Message = "Source declaring type conflict",
                DeclaringType = null,
                SourceDeclaringType = exactLiteral,
            };
        AnalysisDiagnostic sourceNested =
            sourceLiteral with
            {
                SourceDeclaringType = exactNested,
            };

        ImmutableArray<AnalysisDiagnostic> diagnostics =
            AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    [
                        compatibleLegacy,
                        declaringLiteral,
                        sourceLiteral,
                    ],
                    [
                        compatibleExact,
                        declaringNested,
                        sourceNested,
                    ]);

        AnalysisDiagnostic compatible =
            Assert.Single(
                diagnostics.Where(
                    diagnostic =>
                        diagnostic.Message
                            == "Compatible provenance"));
        Assert.Same(
            legacy,
            compatible.DeclaringType);
        Assert.Same(
            exact,
            compatible.SourceDeclaringType);
        Assert.Equal(
            new[]
            {
                exactLiteral,
                exactNested,
            },
            diagnostics
                .Where(
                    diagnostic =>
                        diagnostic.Message
                            == "Declaring type conflict")
                .Select(
                    diagnostic =>
                        diagnostic.DeclaringType));
        Assert.Equal(
            new[]
            {
                exactLiteral,
                exactNested,
            },
            diagnostics
                .Where(
                    diagnostic =>
                        diagnostic.Message
                            == "Source declaring type conflict")
                .Select(
                    diagnostic =>
                        diagnostic.SourceDeclaringType));
    }

    [Fact]
    public void
        ScopeDiagnosticAggregation_DoesNotInferTypeIdentityFromDisplay()
    {
        TypeRef arityOne =
            ExactDefinition(
                "Value`1",
                "Value`1");
        TypeRef arityTwo =
            ExactDefinition(
                "Value`2",
                "Value`2");
        Assert.NotEqual(
            arityOne,
            arityTwo);
        Assert.Equal(
            arityOne.ToDisplayString(),
            arityTwo.ToDisplayString());
        var declaringArityOne = new AnalysisDiagnostic(
            0x06000003,
            "Sample.<Source>d__1::MoveNext()",
            "Same-display declaring type conflict",
            DeclaringType: arityOne);
        AnalysisDiagnostic declaringArityTwo =
            declaringArityOne with
            {
                DeclaringType = arityTwo,
            };
        var sourceArityOne = new AnalysisDiagnostic(
            0x06000003,
            "Sample.<Source>d__1::MoveNext()",
            "Same-display source declaring type conflict",
            SourceDeclaringType: arityOne);
        AnalysisDiagnostic sourceArityTwo =
            sourceArityOne with
            {
                SourceDeclaringType = arityTwo,
            };

        ImmutableArray<AnalysisDiagnostic> diagnostics =
            AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    [
                        declaringArityOne,
                        sourceArityOne,
                    ],
                    [
                        declaringArityTwo,
                        sourceArityTwo,
                    ]);

        Assert.Equal(
            new[]
            {
                arityOne,
                arityTwo,
            },
            diagnostics
                .Where(
                    diagnostic =>
                        diagnostic.Message
                            == "Same-display declaring type conflict")
                .Select(
                    diagnostic =>
                        diagnostic.DeclaringType));
        Assert.Equal(
            new[]
            {
                arityOne,
                arityTwo,
            },
            diagnostics
                .Where(
                    diagnostic =>
                        diagnostic.Message
                            == "Same-display source declaring type conflict")
                .Select(
                    diagnostic =>
                        diagnostic.SourceDeclaringType));
    }

    [Fact]
    public void
        ScopeDiagnosticAggregation_PreservesDistinctFailureMessages()
    {
        var first = new AnalysisDiagnostic(
            0x06000003,
            "Sample.<Source>d__1::MoveNext()",
            "BadImageFormatException: Invalid attribute");
        AnalysisDiagnostic second = first with
        {
            Message =
                "BadImageFormatException: Invalid signature",
        };

        ImmutableArray<AnalysisDiagnostic> diagnostics =
            AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    [first],
                    [second]);

        Assert.Equal(
            new[]
            {
                first.Message,
                second.Message,
            },
            diagnostics.Select(
                diagnostic =>
                    diagnostic.Message));
    }

    [Fact]
    public void
        ScopeDiagnosticAggregation_PreservesPhysicalFailureIdentity()
    {
        const string firstMethod =
            "Sample.<Source>d__1::MoveNext()";
        const string secondMethod =
            "Sample.<Source>d__1::SetStateMachine()";
        const string overloadedMethod =
            "Sample.Source::Run()";
        var firstLabel = new AnalysisDiagnostic(
            0x06000003,
            firstMethod,
            "Canonical label identity");
        var secondLabel = firstLabel with
        {
            Method = secondMethod,
        };
        var firstToken = new AnalysisDiagnostic(
            0x06000004,
            overloadedMethod,
            "Physical token identity");
        var secondToken = firstToken with
        {
            MethodToken = 0x06000005,
        };

        ImmutableArray<AnalysisDiagnostic> diagnostics =
            AnalysisDiagnosticAggregation
                .MergeInMetadataOrder(
                    [firstLabel, firstToken],
                    [secondLabel, secondToken]);

        Assert.Equal(
            new[]
            {
                (0x06000003, firstMethod),
                (0x06000003, secondMethod),
                (0x06000004, overloadedMethod),
                (0x06000005, overloadedMethod),
            },
            diagnostics.Select(
                diagnostic =>
                    (
                        diagnostic.MethodToken,
                        diagnostic.Method
                    )));
    }

    [Fact]
    public void LiftedOwnerMemberIdentity_RetainsExactAssemblyReferenceScope()
    {
        byte[] image = EmitAssembly(
            "SameName",
            metadata =>
            {
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Sample"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
                AssemblyReferenceHandle external =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString("SameName"),
                        new Version(2, 0, 0, 0),
                        default,
                        metadata.GetOrAddBlob(
                            new byte[]
                            {
                                0, 17, 34, 51,
                                68, 85, 102, 119,
                            }),
                        default,
                        default);
                metadata.AddTypeReference(
                    external,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("Sample"));
            });
        using var stream = new MemoryStream(image);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle localHandle =
            reader.TypeDefinitions.Last();
        TypeReferenceHandle externalHandle =
            reader.TypeReferences.Single();
        TypeRef local = TypeRefDecoder.Instance.GetTypeFromDefinition(
            reader,
            localHandle,
            0);
        TypeRef external = TypeRefDecoder.Instance.GetTypeFromReference(
            reader,
            externalHandle,
            0);

        Assert.Equal(local, external);
        Assert.False(
            LibraryBodyMethodReferenceResolver
                .SameMethodReferenceDeclaringType(
                    local,
                    external));
    }

    [Fact]
    public void OptimizationOpportunities_TopLevelLocalFunction_IsReported()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath());

        var row = Assert.Single(index.OptimizationOpportunities.Where(
            opportunity =>
                opportunity.Shape == "generic-parameter-object-box"
                && opportunity.Method.Name.Contains(
                    "TopLevelEqual",
                    StringComparison.Ordinal)));

        Assert.Equal("<Main>$", row.SourceOwner?.Name);
    }

    [Fact]
    public void OptimizationOpportunities_AsyncTopLevelLocalFunction_IsReported()
    {
        var index = LibraryBodyIndex.Open(
            FixtureCatalog.AnalysisTopLevelAsync.AssemblyPath());

        var row = Assert.Single(index.OptimizationOpportunities.Where(
            opportunity =>
                opportunity.Shape == "generic-parameter-object-box"
                && opportunity.Method.Name.Contains(
                    "TopLevelAsyncEqual",
                    StringComparison.Ordinal)));

        Assert.Equal("<Main>$", row.SourceOwner?.Name);
    }

    [Fact]
    public void OptimizationOpportunities_ClassicAsyncTopLevelLocalFunction_IsReported()
    {
        string path =
            FixtureCatalog.AnalysisTopLevelClassicAsync.AssemblyPath();
        using (var stream = File.OpenRead(path))
        using (var peReader = new PEReader(stream))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            MethodDefinition owner = reader.MethodDefinitions
                .Select(reader.GetMethodDefinition)
                .Single(method =>
                    reader.StringComparer.Equals(method.Name, "<Main>$"));
            Assert.False(
                owner.ImplAttributes.HasFlag(MethodImplAttributes.Async));
            Assert.Contains(
                owner.GetCustomAttributes(),
                handle =>
                    AttributeDecoder.GetAttributeTypeName(
                        reader,
                        reader.GetCustomAttribute(handle).Constructor)
                    == "System.Runtime.CompilerServices.AsyncStateMachineAttribute");
        }

        var index = LibraryBodyIndex.Open(path);

        var row = Assert.Single(index.OptimizationOpportunities.Where(
            opportunity =>
                opportunity.Shape == "generic-parameter-object-box"
                && opportunity.Method.Name.Contains(
                    "TopLevelClassicAsyncEqual",
                    StringComparison.Ordinal)));

        Assert.Equal("<Main>$", row.SourceOwner?.Name);
    }

    [Fact]
    public void OptimizationOpportunities_ClassicAsyncTypeDefinitionsAreIndexedOnce()
    {
        string path =
            FixtureCatalog.AnalysisTopLevelClassicAsync.AssemblyPath();
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        int built = 0;
        using var builder = new LibraryBodyAnalysisBuilder(
            path,
            reader,
            peReader,
            resolver: null,
            typeDefinitionIndexBuilt:
                () => Interlocked.Increment(ref built));

        _ = builder.Build(LibraryBodyAnalysisPlan.Create(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            methodScope: null,
            typeScope: null));

        Assert.Equal(1, built);
    }

    [Fact]
    public void OptimizationOpportunities_ClassicAsyncDottedStateMachineName_IsSuppressed()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-classic-async-dotted-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "DottedStateMachine.dll");
        try
        {
            byte[] image = File.ReadAllBytes(
                FixtureCatalog.AnalysisTopLevelClassicAsync.AssemblyPath());
            ReplaceUniqueAscii(
                image,
                "Program+<<Main>$>d__0",
                "Program.<<Main>$>d__0");
            File.WriteAllBytes(path, image);

            var index = LibraryBodyIndex.Open(path);

            Assert.DoesNotContain(
                index.OptimizationOpportunities,
                opportunity =>
                    opportunity.Shape == "generic-parameter-object-box"
                    && opportunity.Method.Name.Contains(
                        "TopLevelClassicAsyncEqual",
                        StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OptimizationOpportunities_TopLevelNameWithoutEntryPoint_IsSuppressed()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-no-entry-point-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "NoEntryPoint.dll");
        try
        {
            byte[] image = File.ReadAllBytes(
                FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath());
            using (var peReader = new PEReader(
                new MemoryStream(image, writable: false)))
            {
                int corHeaderOffset =
                    peReader.PEHeaders.CorHeaderStartOffset;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
                    image.AsSpan(corHeaderOffset + 20, sizeof(int)),
                    0);
            }
            File.WriteAllBytes(path, image);

            var index = LibraryBodyIndex.Open(path);

            Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
                opportunity.Shape == "generic-parameter-object-box"
                && opportunity.Method.Name.Contains(
                    "TopLevelEqual",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OptimizationOpportunities_SourceGeneratedLiftedMethods_AreNotReported()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.DoesNotContain(index.OptimizationOpportunities, opportunity =>
            opportunity.Shape == "generic-parameter-object-box"
            && (opportunity.Method.Name.Contains(
                    nameof(OptimizationOpportunityFixtures.SourceGeneratedLocalFunction),
                    StringComparison.Ordinal)
                || opportunity.Method.Name.Contains(
                    nameof(OptimizationOpportunityFixtures.SourceGeneratedLambda),
                    StringComparison.Ordinal)
                || opportunity.Method.Name.Contains(
                    nameof(SourceGeneratedOptimizationFixtures.SourceGeneratedTypeLambda),
                    StringComparison.Ordinal)
                || opportunity.Method.Name.Contains(
                    nameof(GeneratedMethodNestingFixtures.Nested.SourceGeneratedNestedLocal),
                    StringComparison.Ordinal)
                || opportunity.Method.Name.Contains(
                    nameof(GeneratedMethodNestingFixtures.Nested.SourceGeneratedNestedLambda),
                    StringComparison.Ordinal)
                || opportunity.Method.Name.Contains(
                    "GeneratedCore",
                    StringComparison.Ordinal)
                || opportunity.Method.Name.Contains(
                    nameof(OptimizationOpportunityFixtures.CompilerGeneratedOwner),
                    StringComparison.Ordinal)
                || opportunity.Method.Name.Contains(
                    nameof(CompilerGeneratedOwnerContainer.CompilerGeneratedTypeOwner),
                    StringComparison.Ordinal)));

        Assert.Contains(index.OptimizationOpportunities, opportunity =>
            opportunity.Shape == "generic-parameter-object-box"
            && opportunity.Method.Name.Contains(
                "AuthoredCore",
                StringComparison.Ordinal)
            && opportunity.SourceOwner?.Name == "Handle");
    }

    [Fact]
    public void OptimizationOpportunities_MemberRefLiftedOverloads_MatchFullSignature()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-lifted-overloads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "LiftedOverloads.dll");
        try
        {
            byte[] image = File.ReadAllBytes(
                typeof(MemberRefLiftedOverloadFixture<>).Assembly.Location);
            ReplaceUniqueAscii(
                image,
                "<Owner>g__Core|1_0",
                "<Owner>g__Core|0_0");
            File.WriteAllBytes(path, image);

            var index = LibraryBodyIndex.Open(path);
            var rows = index.OptimizationOpportunities
                .Where(opportunity =>
                    opportunity.Shape == "generic-parameter-object-box"
                    && opportunity.Method.DeclaringType.Name.Contains(
                        nameof(MemberRefLiftedOverloadFixture<object>),
                        StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(2, rows.Length);
            Assert.Equal(
                2,
                rows.Select(row => row.SourceOwner?.MetadataToken)
                    .Distinct()
                    .Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OptimizationOpportunities_MemberRefFunctionPointers_MatchRawSignature()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-lifted-function-pointers-"
                + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "LiftedFunctionPointers.dll");
        try
        {
            byte[] image = File.ReadAllBytes(
                typeof(MemberRefFunctionPointerFixture<>).Assembly.Location);
            ReplaceUniqueAscii(
                image,
                "<Owner>g__Core|1_0",
                "<Owner>g__Core|0_0");
            File.WriteAllBytes(path, image);

            var index = LibraryBodyIndex.Open(path);
            var rows = index.OptimizationOpportunities
                .Where(opportunity =>
                    opportunity.Shape == "generic-parameter-object-box"
                    && opportunity.Method.DeclaringType.Name.Contains(
                        nameof(MemberRefFunctionPointerFixture<object>),
                        StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(2, rows.Length);
            Assert.Equal(
                2,
                rows.Select(row => row.SourceOwner?.MetadataToken)
                    .Distinct()
                    .Count());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(Timeout = 10_000)]
    public void OptimizationOpportunities_CyclicNestedTypes_Terminate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();

        string directory = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-cyclic-nesting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "CyclicNesting.dll");
        try
        {
            File.WriteAllBytes(path, EmitAssembly("CyclicNesting", metadata =>
            {
                TypeDefinitionHandle a = metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic,
                    default,
                    metadata.GetOrAddString("A"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
                TypeDefinitionHandle b = metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic,
                    default,
                    metadata.GetOrAddString("B"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
                metadata.AddNestedType(a, b);
                metadata.AddNestedType(b, a);
            }));

            var index = LibraryBodyIndex.Open(path);

            Assert.Empty(index.OptimizationOpportunities);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
