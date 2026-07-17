using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

using ILInspector.Analysis;
using ILInspector.AnalysisHarness;
using ILInspector.Findings;

namespace ILInspector.Analysis.Tests;

public sealed class LeakTriageAnalyzerTests
{
    [Fact]
    public void CleanArrayPoolFixtures_ProduceZeroRows()
    {
        var findings = FixtureFindings();

        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.CorrectRentReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.TryFinallyReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.TryFinallyThrowReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.ReturnOnAllPaths)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.CorrelatedReturnOnAllPaths)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.NestedFinallyLeaveReturn)));
    }

    [Fact]
    public void MisuseArrayPoolFixtures_FireExactlyOnceEach()
    {
        var findings = FixtureFindings();

        AssertSingleShape(findings, nameof(ArrayPoolLeakFixtures.UseAfterReturn), "arraypool-use-after-return");
        AssertSingleShape(findings, nameof(ArrayPoolLeakFixtures.RentNotReturnedOnSomePath), "arraypool-rent-not-returned");
        AssertSingleShape(findings, nameof(ArrayPoolLeakFixtures.DoubleReturn), "arraypool-double-return");
    }

    [Fact]
    public void DetailedAnalysis_ReportsMeasurementCandidatesWithoutChangingFindings()
    {
        var result = FixtureResult();

        AssertSingleShape(result.Findings, nameof(ArrayPoolLeakFixtures.UseAfterReturn), "arraypool-use-after-return");
        AssertSingleShape(result.Findings, nameof(ArrayPoolLeakFixtures.RentNotReturnedOnSomePath), "arraypool-rent-not-returned");
        AssertSingleShape(result.Findings, nameof(ArrayPoolLeakFixtures.DoubleReturn), "arraypool-double-return");

        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.UseAfterReturn), "use-after-return-candidate");
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentNotReturnedOnSomePath), "normal-path-leak-candidate");
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.DoubleReturn), "double-return-candidate");
    }

    [Fact]
    public void CatchAllCleanup_SuppressesExceptionPathCandidate()
    {
        var result = FixtureResult();

        // A catch-all (`catch {}` or `catch (Exception)`) that returns the buffer protects the
        // exception path, so no exception-path-leak-candidate (the JsonDocument.Parse FP class).
        AssertNoCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchAllReturn), "exception-path-leak-candidate");
        AssertNoCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchExceptionReturn), "exception-path-leak-candidate");

        // Near miss: a typed catch does not cover every exception type, so it stays a candidate.
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallTypedCatchReturn), "exception-path-leak-candidate");

        // Near miss: a sibling typed catch precedes the catch-all and can handle an exception
        // without releasing, so the catch-all must not be credited - stays a candidate.
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentCrossCallSiblingTypedThenCatchAllReturn), "exception-path-leak-candidate");

        // Near miss: an inner typed catch on a nested try intercepts and swallows before the outer
        // catch-all, so the catch-all must not be credited - stays a candidate.
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.RentNestedInnerCatchSwallowThenCatchAll), "exception-path-leak-candidate");

        // The fix is measurement-only: it changes no finding.
        Assert.Empty(ForMethod(result.Findings, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchAllReturn)));
        Assert.Empty(ForMethod(result.Findings, nameof(ArrayPoolLeakFixtures.RentCrossCallCatchExceptionReturn)));
        Assert.Empty(ForMethod(result.Findings, nameof(ArrayPoolLeakFixtures.RentCrossCallTypedCatchReturn)));
    }

    [Fact]
    public void ResourceLifecycleAnalysis_ReportsExactBoundaryEvidence()
    {
        var inspection = ResourceLifecycleAnalysis.InspectAssembly(
            typeof(ArrayPoolLeakFixtures).Assembly.Location,
            new FindingSubject("fixtures", "fixtures"));
        var complete =
            Assert.IsType<FindingInspection<ResourceLifecycleOccurrence>.Complete>(
                inspection.Value);

        var external = Assert.Single(complete.Findings.Where(finding =>
            finding.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalReadBeforeReturn)));
        Assert.Equal("analysis.resource-lifecycle", external.Descriptor.Id);
        var externalBoundary = Assert.Single(external.Payload.Boundaries);
        Assert.Equal("Read", externalBoundary.Operation.Name);
        Assert.True(
            externalBoundary.ILOffset > external.Payload.AcquireOffset);

        Assert.DoesNotContain(
            typeof(ResourceLifecycleOccurrence).GetProperties(),
            property => property.Name == "Actionability");
        Assert.DoesNotContain(
            typeof(ResourceBoundaryEvidence).GetProperties(),
            property => property.Name == "Kind");

        var clone = external.Payload with
        {
            Boundaries = [.. external.Payload.Boundaries],
        };
        Assert.Equal(external.Payload, clone);
        Assert.Equal(external.Payload.GetHashCode(), clone.GetHashCode());
    }

    [Fact]
    public void ResourceTriageAnalysis_AssessesExactBoundaryEvidence()
    {
        var inspection = ResourceLifecycleAnalysis.InspectAssembly(
            typeof(ArrayPoolLeakFixtures).Assembly.Location,
            new FindingSubject("fixtures", "fixtures"));
        var complete =
            Assert.IsType<FindingInspection<ResourceLifecycleOccurrence>.Complete>(
                inspection.Value);
        var assessments = ResourceTriageAnalysis.Assess(complete);

        var external = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalReadBeforeReturn)));
        Assert.StartsWith("rt~", external.CandidateId);
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            external.Actionability);
        Assert.Equal(
            ResourceTriageReason.ExternalInputBoundaryBeforeCleanup,
            external.Reason);
        Assert.Equal(
            ResourceTriageImpact.PoolChurnOnException,
            external.Impact);
        Assert.Equal(
            ResourceTriageRemediation.EnsureExceptionalCleanup,
            external.Remediation);
        Assert.Equal(ResourceTriageConfidence.Medium, external.Confidence);
        var externalBoundary = Assert.Single(external.Boundaries);
        Assert.Equal("Read", externalBoundary.Evidence.Operation.Name);
        Assert.Equal(
            ResourceTriageBoundaryKind.ExternalInput,
            externalBoundary.Kind);

        var throughSetup = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalDecodeThroughSpan)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughSetup.Actionability);
        Assert.Contains(throughSetup.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == "GetChars"
            && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);
        Assert.DoesNotContain(throughSetup.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == "AsSpan");

        var throughLocal = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalDecodeThroughSpanLocal)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughLocal.Actionability);
        Assert.Contains(throughLocal.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == "GetChars"
            && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);

        var throughImplicitSpan = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalDecodeThroughImplicitSpan)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughImplicitSpan.Actionability);
        Assert.Contains(throughImplicitSpan.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == "GetChars"
            && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);
        Assert.DoesNotContain(throughImplicitSpan.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == "op_Implicit");

        var throughMemory = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalReadThroughMemory)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughMemory.Actionability);
        Assert.Contains(throughMemory.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == "Read"
            && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);
        Assert.Contains(throughMemory.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == ".ctor"
            && boundary.Kind == ResourceTriageBoundaryKind.Unknown);

        var throughReadOnlyMemory = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalReadThroughReadOnlyMemory)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughReadOnlyMemory.Actionability);
        Assert.Contains(throughReadOnlyMemory.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == "Read"
            && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);
        Assert.Contains(throughReadOnlyMemory.Boundaries, boundary =>
            boundary.Evidence.Operation.Name == ".ctor"
            && boundary.Kind == ResourceTriageBoundaryKind.Unknown);

        var throughStringArgument = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalParseThroughSpanWithTag)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughStringArgument.Actionability);
        Assert.Contains(
            throughStringArgument.Boundaries,
            boundary =>
                boundary.Evidence.Operation.Name == "Parse"
                && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);

        var throughConstructedArgument = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalParseThroughSpanWithConstructedTag)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughConstructedArgument.Actionability);
        Assert.Contains(
            throughConstructedArgument.Boundaries,
            boundary =>
                boundary.Evidence.Operation.Name == "Parse"
                && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);
        Assert.DoesNotContain(
            throughConstructedArgument.Boundaries,
            boundary => boundary.Evidence.Operation.Name == ".ctor");

        var throughStaticArgument = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalParseThroughSpanWithStaticTag)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            throughStaticArgument.Actionability);
        Assert.Contains(
            throughStaticArgument.Boundaries,
            boundary =>
                boundary.Evidence.Operation.Name == "Parse"
                && boundary.Kind == ResourceTriageBoundaryKind.ExternalInput);

        var constructorBoundary = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.SpanConsumedByConstructor)));
        Assert.Equal(
            ResourceTriageActionability.Unknown,
            constructorBoundary.Actionability);
        Assert.Equal(
            ".ctor",
            Assert.Single(constructorBoundary.Boundaries)
                .Evidence.Operation.Name);

        var implicitConstructorBoundary = Assert.Single(
            assessments.Where(assessment =>
                assessment.Source.Payload.Method.Name
                    == nameof(ArrayPoolLeakFixtures.ImplicitSpanConsumedByConstructor)));
        Assert.Equal(
            ResourceTriageActionability.Unknown,
            implicitConstructorBoundary.Actionability);
        Assert.Equal(
            ".ctor",
            Assert.Single(implicitConstructorBoundary.Boundaries)
                .Evidence.Operation.Name);

        var unrelatedReader = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.UnrelatedTextReaderReadBeforeReturn)));
        Assert.Equal(
            ResourceTriageActionability.Unknown,
            unrelatedReader.Actionability);
        Assert.Equal(
            ResourceTriageBoundaryKind.Unknown,
            Assert.Single(unrelatedReader.Boundaries).Kind);

        var frameworkReader = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.FrameworkTextReaderReadBeforeReturn)));
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            frameworkReader.Actionability);
        Assert.Equal(
            ResourceTriageBoundaryKind.ExternalInput,
            Assert.Single(frameworkReader.Boundaries).Kind);

        var trusted = Assert.Single(assessments.Where(assessment =>
            assessment.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.TrustedTransformWithUnrelatedReadAfterReturn)));
        Assert.Equal(
            ResourceTriageActionability.TrustedLowActionability,
            trusted.Actionability);
        Assert.Equal(
            ResourceTriageReason.InMemoryBoundaryBeforeCleanup,
            trusted.Reason);
        var trustedBoundary = Assert.Single(trusted.Boundaries);
        Assert.Equal("GetBytes", trustedBoundary.Evidence.Operation.Name);
        Assert.Equal(
            ResourceTriageBoundaryKind.InMemoryTransform,
            trustedBoundary.Kind);

        var actionable = assessments.Where(assessment =>
            assessment.Actionability
                == ResourceTriageActionability.UntrustedActionable);
        Assert.Contains(actionable, candidate =>
            candidate.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalReadBeforeReturn));
        Assert.DoesNotContain(actionable, candidate =>
            candidate.Source.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.TrustedTransformWithUnrelatedReadAfterReturn));

        var clone = external with
        {
            Boundaries = [.. external.Boundaries],
        };
        Assert.Equal(external, clone);
        Assert.Equal(external.GetHashCode(), clone.GetHashCode());
    }

    [Fact]
    public void ResourceLifecycleAnalysis_ReportsInspectionFailure()
    {
        var subject = new FindingSubject("missing", "missing");
        var inspection = ResourceLifecycleAnalysis.InspectAssembly(
            Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.dll"),
            subject);

        var failed =
            Assert.IsType<FindingInspection<ResourceLifecycleOccurrence>.Failed>(
                inspection.Value);
        Assert.Equal(subject, failed.Error.Subject);
        Assert.Equal(
            AnalysisFindings.ResourceLifecycleDescriptor,
            failed.Error.Descriptor);
        Assert.Contains("FileNotFoundException", failed.Error.Reason);
    }

    [Fact]
    public void LibraryBodyIndex_RequiresLeakTriageFeature()
    {
        string path = typeof(ArrayPoolLeakFixtures).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.MethodEvidence);

        Assert.Throws<InvalidOperationException>(() => index.LeakTriage);

        var leakIndex = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.LeakTriage);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.LeakTriage,
            leakIndex.Features);
        Assert.Empty(leakIndex.DeclaredMethods);
        Assert.Empty(leakIndex.Methods);
        Assert.Empty(leakIndex.DirectCalls);
        Assert.Empty(leakIndex.UnsafeEvidence);
        Assert.Empty(leakIndex.GetAllocationOccurrences());
        Assert.Empty(leakIndex.GetUnsafetyOccurrences());
        Assert.Contains(leakIndex.LeakTriage.ExceptionPathCandidates, candidate =>
            candidate.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalReadBeforeReturn));
    }

    [Fact]
    public void LibraryBodyIndex_NormalizesFeatureDependencies()
    {
        string path = typeof(ArrayPoolLeakFixtures).Assembly.Location;

        var allocations = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.Allocations);
        var opportunities = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);

        Assert.Equal(
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.Allocations,
            allocations.Features);
        Assert.Equal(
            LibraryBodyAnalysisFeatures.Default,
            opportunities.Features);
    }

    [Fact]
    public void LibraryBodyIndex_ConsumesCallerOwnedPrefetchedImage()
    {
        string path = typeof(ArrayPoolLeakFixtures).Assembly.Location;
        var image = ImmutableArray.Create(File.ReadAllBytes(path));

        var shared = LibraryBodyIndex.OpenFromPrefetchedImage(
            path,
            image,
            LibraryBodyAnalysisFeatures.All);
        var owned = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.All);

        Assert.True(owned.DirectCalls.SequenceEqual(shared.DirectCalls));
        Assert.True(
            owned.LeakTriage.Candidates.SequenceEqual(
                shared.LeakTriage.Candidates));
        Assert.True(
            owned.LeakTriage.ExceptionPathCandidates.SequenceEqual(
                shared.LeakTriage.ExceptionPathCandidates));
        Assert.False(image.IsDefaultOrEmpty);
    }

    [Fact]
    public void LibraryBodyIndex_RejectsScopedLeakTriageCensus()
    {
        string path = typeof(ArrayPoolLeakFixtures).Assembly.Location;

        Assert.Throws<ArgumentException>(() => LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.LeakTriage,
            bodyScope: new HashSet<int>()));
    }

    [Fact]
    public void ResourceLifecycleAnalysis_ConsumesSharedBodyIndex()
    {
        string path = typeof(ArrayPoolLeakFixtures).Assembly.Location;
        var index = LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.LeakTriage);
        int acquisitions = 0;

        var inspection = ResourceLifecycleAnalysis.InspectAssembly(
            () =>
            {
                acquisitions++;
                return index;
            },
            new FindingSubject("fixtures", "fixtures"));

        Assert.Equal(1, acquisitions);
        var complete =
            Assert.IsType<FindingInspection<ResourceLifecycleOccurrence>.Complete>(
                inspection.Value);
        Assert.Contains(complete.Findings, finding =>
            finding.Payload.Method.Name
                == nameof(ArrayPoolLeakFixtures.ExternalReadBeforeReturn));
    }

    [Fact]
    public void LeakTriageAssemblyIdentity_PreservesLegacyShape()
    {
        var indexed = new MethodIdentity(
            "Fixtures",
            Guid.NewGuid(),
            TypeRef.Definition("Fixtures", "Fixtures", "Extensions"),
            "Read",
            [TypeRef.CoreLib("System.IO", "Stream")],
            TypeRef.CoreLib("System", "Int32"),
            0x06000001,
            IsStatic: true,
            IsExtension: true,
            CallerUnsafeMode.Explicit,
            GenericArity: 1,
            GenericParameterNames: ["T"]);

        var assemblyScan =
            LeakTriageAnalyzer.CreateAssemblyScanMethodIdentity(indexed);

        Assert.False(assemblyScan.IsExtension);
        Assert.Equal(CallerUnsafeMode.None, assemblyScan.CallerUnsafeMode);
        Assert.Equal(0, assemblyScan.GenericArity);
        Assert.Empty(assemblyScan.GenericParameterNames);
        Assert.Equal(indexed.AssemblyName, assemblyScan.AssemblyName);
        Assert.Equal(indexed.ModuleVersionId, assemblyScan.ModuleVersionId);
        Assert.Equal(indexed.DeclaringType, assemblyScan.DeclaringType);
        Assert.Equal(indexed.Name, assemblyScan.Name);
        Assert.Equal(indexed.ParameterTypes, assemblyScan.ParameterTypes);
        Assert.Equal(indexed.ReturnType, assemblyScan.ReturnType);
        Assert.Equal(indexed.MetadataToken, assemblyScan.MetadataToken);
        Assert.Equal(indexed.IsStatic, assemblyScan.IsStatic);
    }

    [Fact]
    public void LeakActionabilitySensor_ConsumesProductClassification()
    {
        var report = LeakActionabilitySensor.Measure(
            [typeof(ArrayPoolLeakFixtures).Assembly.Location],
            examplesPerAssembly: 1000);
        var assembly = Assert.Single(report.Assemblies);

        Assert.True(assembly.Opened);
        Assert.Contains(assembly.Examples, example =>
            example.Class == LeakActionabilitySensor.Untrusted
            && example.Method.EndsWith(
                $"::{nameof(ArrayPoolLeakFixtures.ExternalReadBeforeReturn)}",
                StringComparison.Ordinal)
            && example.BoundarySet.Contains(
                "Stream::Read",
                StringComparison.Ordinal));
        Assert.Contains(assembly.Examples, example =>
            example.Class == LeakActionabilitySensor.Trusted
            && example.Method.EndsWith(
                $"::{nameof(ArrayPoolLeakFixtures.TrustedTransformWithUnrelatedReadAfterReturn)}",
                StringComparison.Ordinal)
            && !example.BoundarySet.Contains(
                "ReadByte",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AmbiguousArrayPoolFixtures_FailClosed()
    {
        var findings = FixtureFindings();

        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.CrossMethodReturn)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.FieldStoredArray)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.NonSharedPoolRent)));
        Assert.Empty(ForMethod(findings, nameof(ArrayPoolLeakFixtures.ReturnsRentedArray)));
    }

    [Fact]
    public void DetailedAnalysis_BucketsSuppressedOwnershipShapes()
    {
        var crossMethod = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenUnknown),
            0x2A,
        ], []);
        var fieldStore = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x80, .. TokenBytes(TokenField),
            0x2A,
        ], []);
        var returned = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x2A,
        ], []);

        AssertCandidate(crossMethod, nameof(Synthetic), "cross-method-suppressed");
        AssertCandidate(fieldStore, nameof(Synthetic), "alias-or-field-suppressed");
        AssertCandidate(returned, nameof(Synthetic), "ownership-transfer-suppressed");

        var result = FixtureResult();
        AssertCandidate(result, nameof(ArrayPoolLeakFixtures.NonSharedPoolRent), "ownership-transfer-suppressed");
    }

    [Fact]
    public void DetailedAnalysis_CrossMethodBeforeReturn_IsExceptionPathCandidate()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenUnknown),
            .. Call(TokenShared),
            0x06,
            .. Callvirt(TokenReturn),
            0x2A,
        ], []);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "cross-method-suppressed");
        AssertCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
    }

    [Fact]
    public void DetailedAnalysis_FinallyProtectedCrossMethod_DoesNotAddExceptionPathCandidate()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),       // IL_0000 ArrayPool<byte>.Shared
            0x1F, 0x10,                // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),     // IL_0007 Rent
            0x0A,                       // IL_000C stloc.0
            0x06,                       // IL_000D ldloc.0
            .. Call(TokenUnknown),      // IL_000E call Unknown.Use
            0xDE, 0x0C,                 // IL_0013 leave.s IL_0021
            .. Call(TokenShared),       // IL_0015 ArrayPool<byte>.Shared
            0x06,                       // IL_001A ldloc.0
            .. Callvirt(TokenReturn),   // IL_001B Return
            0xDC,                       // IL_0020 endfinally
            0x2A,                       // IL_0021 ret
        ], [Region(ExceptionRegionKind.Finally, tryOffset: 13, tryLength: 8, handlerOffset: 21, handlerLength: 12)]);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
    }

    [Fact]
    public void DetailedAnalysis_NonThrowingSetupBoundary_DoesNotAddExceptionPathCandidate()
    {
        var keepAlive = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenKeepAlive),
            0x2A,
        ], []);
        var arrayCopy = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x06,
            0x17,
            .. Call(TokenArrayCopy),
            0x2A,
        ], []);
        var systemMemorySpanClear = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x16,
            .. Call(TokenSystemMemoryAsSpan),
            .. Call(TokenSystemMemorySpanClear),
            0x2A,
        ], []);
        var arrayClear = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            0x16,
            0x1F, 0x10,
            .. Call(TokenArrayClear),
            0x2A,
        ], []);
        var spanCopyTo = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenSpanImplicit),
            .. Call(TokenSpanCopyTo),
            0x2A,
        ], []);
        var stagedSpanCopyTo = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenSpanImplicit),
            0x0B,
            0x12, 0x01,
            0x07,
            .. Call(TokenSpanCopyTo),
            0x2A,
        ], []);

        Assert.Empty(keepAlive.Findings);
        AssertCandidate(keepAlive, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(keepAlive, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(arrayCopy.Findings);
        AssertCandidate(arrayCopy, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(arrayCopy, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(systemMemorySpanClear.Findings);
        AssertCandidate(systemMemorySpanClear, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(systemMemorySpanClear, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(arrayClear.Findings);
        AssertCandidate(arrayClear, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(arrayClear, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(spanCopyTo.Findings);
        AssertCandidate(spanCopyTo, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(spanCopyTo, nameof(Synthetic), "exception-path-leak-candidate");

        Assert.Empty(stagedSpanCopyTo.Findings);
        AssertCandidate(stagedSpanCopyTo, nameof(Synthetic), "cross-method-suppressed");
        AssertNoCandidate(stagedSpanCopyTo, nameof(Synthetic), "exception-path-leak-candidate");
    }

    [Fact]
    public void DetailedAnalysis_ThrowingBoundaryAfterSetup_IsExceptionPathCandidate()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x06,
            .. Call(TokenKeepAlive),
            0x06,
            .. Call(TokenUnknown),
            .. Call(TokenShared),
            0x06,
            .. Callvirt(TokenReturn),
            0x2A,
        ], []);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "cross-method-suppressed");
        AssertCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
    }

    [Fact]
    public void IncompleteDataflow_FailsClosed()
    {
        byte[] externalBranch = [0x2B, 0x7F, 0x2A]; // br.s outside the method, then ret
        var method = new MethodIdentity(
            "Fixture",
            Guid.Empty,
            TypeRef.Definition("Fixture", "Fixtures", "Incomplete"),
            "Malformed",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

        var findings = LeakTriageAnalyzer.AnalyzeMethod(
            method,
            externalBranch,
            Array.Empty<ExceptionRegion>(),
            _ => MemberRef.Unsupported("not used"));

        Assert.Empty(findings);
    }

    [Fact]
    public void DetailedAnalysis_IncompleteDataflowWithoutRent_HasNoSuppressedBucket()
    {
        byte[] externalBranch = [0x2B, 0x7F, 0x2A]; // br.s outside the method, then ret
        var method = new MethodIdentity(
            "Fixture",
            Guid.Empty,
            TypeRef.Definition("Fixture", "Fixtures", "Incomplete"),
            "Malformed",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

        var result = LeakTriageAnalyzer.AnalyzeMethodDetailed(
            method,
            externalBranch,
            Array.Empty<ExceptionRegion>(),
            _ => MemberRef.Unsupported("not used"));

        Assert.Empty(result.Findings);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void DetailedAnalysis_IncompleteDataflowWithRent_IsSuppressedBucket()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            0x2B, 0x7F,
            0x2A,
        ], []);

        Assert.Empty(result.Findings);
        AssertCandidate(result, nameof(Synthetic), "incomplete-cfg-or-rd-suppressed");
    }

    [Fact]
    public void FaultHandlerReturn_DoesNotSatisfyNormalLeavePath()
    {
        var findings = AnalyzeSynthetic([
            .. Call(TokenShared),       // IL_0000 ArrayPool<byte>.Shared
            0x1F, 0x10,                // IL_0005 ldc.i4.s 16
            .. Callvirt(TokenRent),     // IL_0007 Rent
            0x0A,                       // IL_000C stloc.0
            0xDE, 0x0C,                 // IL_000D leave.s IL_001B
            .. Call(TokenShared),       // IL_000F ArrayPool<byte>.Shared
            0x06,                       // IL_0014 ldloc.0
            .. Callvirt(TokenReturn),   // IL_0015 Return
            0xDC,                       // IL_001A endfinally
            0x2A,                       // IL_001B ret
        ], [Region(ExceptionRegionKind.Fault, tryOffset: 13, tryLength: 2, handlerOffset: 15, handlerLength: 12)]);

        AssertSingleShape(findings, nameof(Synthetic), "arraypool-rent-not-returned");
    }

    [Fact]
    public void DetailedAnalysis_ExceptionPathLeak_IsSeparateCandidateBucket()
    {
        var result = AnalyzeSyntheticDetailed([
            .. Call(TokenShared),
            0x1F, 0x10,
            .. Callvirt(TokenRent),
            0x0A,
            .. Newobj(TokenUnknown),
            0x7A,
        ], []);

        AssertSingleShape(result.Findings, nameof(Synthetic), "arraypool-rent-not-returned");
        AssertCandidate(result, nameof(Synthetic), "exception-path-leak-candidate");
    }

    static LeakTriageResult FixtureResult()
    {
        var result = LeakTriageAnalyzer.AnalyzeAssemblyDetailed(typeof(ArrayPoolLeakFixtures).Assembly.Location);
        return result with
        {
            Findings = [.. result.Findings.Where(finding => finding.Method.DeclaringType.Name == nameof(ArrayPoolLeakFixtures))],
            Candidates = [.. result.Candidates.Where(candidate => candidate.Method.DeclaringType.Name == nameof(ArrayPoolLeakFixtures))],
        };
    }

    static ImmutableArray<LeakTriageFinding> FixtureFindings()
        => FixtureResult().Findings;

    const int TokenShared = 0x0A000001;
    const int TokenRent = 0x0A000002;
    const int TokenReturn = 0x0A000003;
    const int TokenUnknown = 0x0A000004;
    const int TokenField = 0x0A000005;
    const int TokenKeepAlive = 0x0A000006;
    const int TokenArrayCopy = 0x0A000007;
    const int TokenSystemMemoryAsSpan = 0x0A000008;
    const int TokenSystemMemorySpanClear = 0x0A000009;
    const int TokenArrayClear = 0x0A00000A;
    const int TokenSpanCopyTo = 0x0A00000B;
    const int TokenSpanImplicit = 0x0A00000C;

    static ImmutableArray<LeakTriageFinding> AnalyzeSynthetic(byte[] il, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
        => AnalyzeSyntheticDetailed(il, exceptionRegions).Findings;

    static LeakTriageResult AnalyzeSyntheticDetailed(byte[] il, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        var method = new MethodIdentity(
            "Fixture",
            Guid.Empty,
            TypeRef.Definition("Fixture", "Fixtures", nameof(Synthetic)),
            nameof(Synthetic),
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);
        return LeakTriageAnalyzer.AnalyzeMethodDetailed(method, il, exceptionRegions, ResolveSyntheticMember);
    }

    static MemberRef ResolveSyntheticMember(int token)
    {
        var arrayPoolOfByte = TypeRef.GenericInstance(
            TypeRef.Definition("System.Buffers", "System.Buffers", "ArrayPool`1"),
            [TypeRef.CoreLib("System", "Byte")]);
        var byteArray = TypeRef.SzArray(TypeRef.CoreLib("System", "Byte"));
        var systemMemorySpanOfByte = TypeRef.GenericInstance(
            TypeRef.Definition("System.Memory", "System", "Span`1", trustedFrameworkAssembly: true),
            [TypeRef.CoreLib("System", "Byte")]);
        var coreLibSpanOfByte = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "Span`1"),
            [TypeRef.CoreLib("System", "Byte")]);

        return token switch
        {
            TokenShared => new MemberRef(arrayPoolOfByte, "get_Shared", [], arrayPoolOfByte, MemberKind.Method),
            TokenRent => new MemberRef(arrayPoolOfByte, "Rent", [TypeRef.CoreLib("System", "Int32")], byteArray, MemberKind.Method) { HasThis = true },
            TokenReturn => new MemberRef(arrayPoolOfByte, "Return", [byteArray], TypeRef.CoreLib("System", "Void"), MemberKind.Method) { HasThis = true },
            TokenUnknown => new MemberRef(TypeRef.Definition("Fixture", "Fixtures", "Unknown"), "Use", [], TypeRef.CoreLib("System", "Void"), MemberKind.Method),
            TokenKeepAlive => new MemberRef(TypeRef.CoreLib("System", "GC"), "KeepAlive", [TypeRef.CoreLib("System", "Object")], TypeRef.CoreLib("System", "Void"), MemberKind.Method),
            TokenArrayCopy => new MemberRef(
                TypeRef.CoreLib("System", "Array"),
                "Copy",
                [TypeRef.CoreLib("System", "Array"), TypeRef.CoreLib("System", "Array"), TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            TokenSystemMemoryAsSpan => new MemberRef(
                TypeRef.Definition("System.Memory", "System", "MemoryExtensions", trustedFrameworkAssembly: true),
                "AsSpan",
                [byteArray, TypeRef.CoreLib("System", "Int32")],
                systemMemorySpanOfByte,
                MemberKind.Method),
            TokenSystemMemorySpanClear => new MemberRef(
                systemMemorySpanOfByte,
                "Clear",
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method)
            { HasThis = true },
            TokenArrayClear => new MemberRef(
                TypeRef.CoreLib("System", "Array"),
                "Clear",
                [TypeRef.CoreLib("System", "Array"), TypeRef.CoreLib("System", "Int32"), TypeRef.CoreLib("System", "Int32")],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            TokenSpanCopyTo => new MemberRef(
                coreLibSpanOfByte,
                "CopyTo",
                [coreLibSpanOfByte],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method)
            { HasThis = true },
            TokenSpanImplicit => new MemberRef(
                coreLibSpanOfByte,
                "op_Implicit",
                [byteArray],
                coreLibSpanOfByte,
                MemberKind.Method),
            _ => MemberRef.Unsupported($"unknown token 0x{token:X8}"),
        };
    }

    static byte[] Call(int token) => [0x28, .. TokenBytes(token)];
    static byte[] Callvirt(int token) => [0x6F, .. TokenBytes(token)];
    static byte[] Newobj(int token) => [0x73, .. TokenBytes(token)];
    static byte[] TokenBytes(int token) => BitConverter.GetBytes(token);

    static readonly ConstructorInfo s_exceptionRegionConstructor =
        typeof(ExceptionRegion).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(ExceptionRegionKind), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
            modifiers: null)
        ?? throw new InvalidOperationException("ExceptionRegion constructor not found.");

    static ExceptionRegion Region(
        ExceptionRegionKind kind,
        int tryOffset,
        int tryLength,
        int handlerOffset,
        int handlerLength,
        int filterOffset = 0)
        => (ExceptionRegion)s_exceptionRegionConstructor.Invoke([kind, tryOffset, tryLength, handlerOffset, handlerLength, filterOffset]);

    static IEnumerable<LeakTriageFinding> ForMethod(ImmutableArray<LeakTriageFinding> findings, string methodName)
        => findings.Where(finding => finding.Method.Name == methodName);

    static void AssertCandidate(LeakTriageResult result, string methodName, string shape)
    {
        var candidate = Assert.Single(result.Candidates.Where(candidate => candidate.Method.Name == methodName && candidate.Shape == shape));
        Assert.Equal(shape, candidate.Shape);
    }

    static void AssertNoCandidate(LeakTriageResult result, string methodName, string shape)
        => Assert.DoesNotContain(result.Candidates, candidate => candidate.Method.Name == methodName && candidate.Shape == shape);

    static void AssertSingleShape(ImmutableArray<LeakTriageFinding> findings, string methodName, string shape)
    {
        var finding = Assert.Single(ForMethod(findings, methodName));
        Assert.Equal(shape, finding.Shape);
    }
}

internal static class Synthetic
{
}

internal sealed class ArrayPoolLeakFixtures
{
    static byte[]? s_field;
    static readonly string s_tag = "wire";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CorrectRentReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        buffer[0] = 1;
        ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TryFinallyReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            buffer[0] = 1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TryFinallyThrowReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            throw new InvalidOperationException();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ReturnOnAllPaths(bool condition)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        if (condition)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            return;
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CorrelatedReturnOnAllPaths(bool condition)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        if (condition)
            ArrayPool<byte>.Shared.Return(buffer);
        if (!condition)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NestedFinallyLeaveReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            try
            {
                goto Done;
            }
            finally
            {
                Consume(1);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

    Done:
        return;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UseAfterReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer[0] = 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentNotReturnedOnSomePath(bool condition)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        if (condition)
            ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DoubleReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        ArrayPool<byte>.Shared.Return(buffer);
        ArrayPool<byte>.Shared.Return(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CrossMethodReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        ReturnHelper(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void FieldStoredArray()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        s_field = buffer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NonSharedPoolRent(ArrayPool<byte> pool)
    {
        var buffer = pool.Rent(16);
        buffer[0] = 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static byte[] ReturnsRentedArray()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        return buffer;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowAfterRent()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        throw new InvalidOperationException(buffer.Length.ToString());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalReadBeforeReturn(Stream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        int read = stream.Read(buffer, 0, 16);
        ArrayPool<byte>.Shared.Return(buffer);
        return read;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalDecodeThroughSpan(byte[] source)
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        int written = System.Text.Encoding.UTF8
            .GetDecoder()
            .GetChars(
                source.AsSpan(),
                chars.AsSpan(),
                flush: true);
        ArrayPool<char>.Shared.Return(chars);
        return written;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalDecodeThroughSpanLocal(byte[] source)
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        Span<char> destination = chars.AsSpan();
        int written = System.Text.Encoding.UTF8
            .GetDecoder()
            .GetChars(
                source.AsSpan(),
                destination,
                flush: true);
        ArrayPool<char>.Shared.Return(chars);
        return written;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalDecodeThroughImplicitSpan(byte[] source)
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        Span<char> destination = chars;
        int written = System.Text.Encoding.UTF8
            .GetDecoder()
            .GetChars(
                source.AsSpan(),
                destination,
                flush: true);
        ArrayPool<char>.Shared.Return(chars);
        return written;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalReadThroughMemory(ExternalMemoryStream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        int read = stream.Read(new Memory<byte>(buffer, 0, 16));
        ArrayPool<byte>.Shared.Return(buffer);
        return read;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalReadThroughReadOnlyMemory(ExternalMemoryStream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        int read = stream.Read(new ReadOnlyMemory<byte>(buffer, 0, 16));
        ArrayPool<byte>.Shared.Return(buffer);
        return read;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalParseThroughSpanWithTag()
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        int written = ExternalInputReader.Parse(chars.AsSpan(), "wire");
        ArrayPool<char>.Shared.Return(chars);
        return written;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalParseThroughSpanWithConstructedTag()
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        int written = ExternalInputReader.Parse(
            chars.AsSpan(),
            new string('a', 5));
        ArrayPool<char>.Shared.Return(chars);
        return written;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ExternalParseThroughSpanWithStaticTag()
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        int written = ExternalInputReader.Parse(chars.AsSpan(), s_tag);
        ArrayPool<char>.Shared.Return(chars);
        return written;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int SpanConsumedByConstructor()
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        var consumer = new SpanConsumer(chars.AsSpan());
        ArrayPool<char>.Shared.Return(chars);
        return consumer.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ImplicitSpanConsumedByConstructor()
    {
        var chars = ArrayPool<char>.Shared.Rent(16);
        Span<char> value = chars;
        var consumer = new SpanConsumer(value);
        ArrayPool<char>.Shared.Return(chars);
        return consumer.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int UnrelatedTextReaderReadBeforeReturn(TextReader reader)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        int read = reader.Read(buffer);
        ArrayPool<byte>.Shared.Return(buffer);
        return read;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int FrameworkTextReaderReadBeforeReturn(System.IO.TextReader reader)
    {
        var buffer = ArrayPool<char>.Shared.Rent(16);
        int read = reader.Read(buffer, 0, 16);
        ArrayPool<char>.Shared.Return(buffer);
        return read;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int TrustedTransformWithUnrelatedReadAfterReturn(Stream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        int written = System.Text.Encoding.UTF8.GetBytes(
            "value",
            0,
            5,
            buffer,
            0);
        ArrayPool<byte>.Shared.Return(buffer);
        _ = stream.ReadByte();
        return written;
    }

    // A cross-method throwing boundary returned on both the normal path and a catch-ALL cleanup
    // (`catch {}`) - correct code with no try/finally. The exception path is protected, so it must
    // NOT produce an exception-path-leak-candidate once catch-all cleanup is modeled (mirrors
    // System.Text.Json's JsonDocument.Parse idiom).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallCatchAllReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    internal sealed class TextReader
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read(byte[] buffer) => buffer.Length;
    }

    internal sealed class ExternalMemoryStream
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read(Memory<byte> buffer) => buffer.Length;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read(ReadOnlyMemory<byte> buffer) => buffer.Length;
    }

    internal static class ExternalInputReader
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Parse(Span<char> destination, string tag)
            => destination.Length + tag.Length;
    }

    internal sealed class SpanConsumer
    {
        public SpanConsumer(Span<char> value)
        {
            Length = value.Length;
        }

        public int Length { get; }
    }

    // Same shape with `catch (Exception)`, which also runs for every managed exception type.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallCatchExceptionReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch (Exception)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // Near miss: a TYPED catch only covers InvalidOperationException; another exception type from
    // Sink would bypass it and leak, so this MUST still be an exception-path-leak-candidate.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallTypedCatchReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch (InvalidOperationException)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // Near miss: a sibling typed catch precedes the catch-all. An InvalidOperationException is
    // handled by the FIRST catch, which does NOT return, so the buffer leaks - the catch-all must
    // NOT be credited, and this MUST still be an exception-path-leak-candidate.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentCrossCallSiblingTypedThenCatchAllReturn()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            Sink(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // Near miss: an INNER typed catch on a nested try intercepts the boundary's exception first
    // and swallows it without releasing, so the outer catch-all never runs for that exception -
    // the array leaks. The outer catch-all must NOT be credited (reviewers, PR #2521).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RentNestedInnerCatchSwallowThenCatchAll()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16);
        try
        {
            try
            {
                Sink(buffer);
            }
            catch (InvalidOperationException)
            {
                return;
            }
            ArrayPool<byte>.Shared.Return(buffer);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    static void ReturnHelper(byte[] buffer)
        => ArrayPool<byte>.Shared.Return(buffer);

    static int Sink(byte[] buffer) => buffer.Length;

    static void Consume(int value)
    {
        if (value == int.MinValue)
            throw new InvalidOperationException();
    }
}
