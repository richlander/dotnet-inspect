using System.Collections.Generic;

// Tests for the type-level confirmation fallback (issue #2264): when a small allocating method is
// inlined, its GCAllocationTick leaf frame belongs to the inliner and the IL-offset site-join misses
// it, but the allocated type is still realized-hot and can confirm the finding by type.
public class TypeConfirmationTests
{
    [Fact]
    public void CanonicalTypeSignature_ReversesArrayRanksOnlyWithinPointerBoundedRuns()
    {
        // Array ranks reverse only within a consecutive run; a pointer bounds the run. So
        // int[][,]*[] (reflection System.Int32[,][]*[]) reconciles, and the distinct int[][]*[,]
        // (reflection System.Int32[][]*[,]) must not collide with it.
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("int[][,]*[]", reflection: false),
            ProgramSupport.CanonicalTypeSignature("System.Int32[,][]*[]", reflection: true));
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("int[][]*[,]", reflection: false),
            ProgramSupport.CanonicalTypeSignature("System.Int32[][]*[,]", reflection: true));
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("System.Int32[,][]*[]", reflection: true),
            ProgramSupport.CanonicalTypeSignature("int[][]*[,]", reflection: false));
    }

    [Fact]
    public void CanonicalTypeSignature_KeepsPointerPositionWhenReversingArrayRanks()
    {
        // Only array ranks reverse between C# and reflection; pointer '*' stays in place. int*[]
        // (array of pointers) and int[]* (pointer to array) are distinct and must not collide, and
        // int*[][,] must reconcile across forms.
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("int*[]", reflection: false),
            ProgramSupport.CanonicalTypeSignature("System.Int32*[]", reflection: true));
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("int*[][,]", reflection: false),
            ProgramSupport.CanonicalTypeSignature("System.Int32*[,][]", reflection: true));
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("int[]*", reflection: false),
            ProgramSupport.CanonicalTypeSignature("System.Int32*[]", reflection: true));
    }

    [Fact]
    public void CanonicalTypeSignature_ReconcilesArrayRankOrderingAcrossForms()
    {
        // Reflection orders array modifiers inside-out relative to C# (int[][,] is emitted as
        // System.Int32[,][]). Display and reflection forms of the same type must agree, and the two
        // genuinely-distinct jagged/multidim shapes must not collide.
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("int[][,]", reflection: false),
            ProgramSupport.CanonicalTypeSignature("System.Int32[,][]", reflection: true));
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("int[,][]", reflection: false),
            ProgramSupport.CanonicalTypeSignature("System.Int32[][,]", reflection: true));
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("int[][,]", reflection: false),
            ProgramSupport.CanonicalTypeSignature("int[,][]", reflection: false));
    }

    [Fact]
    public void CanonicalTypeSignature_ExpandsTupleShorthandToValueTuple()
    {
        // C# tuple shorthand must expand to the runtime ValueTuple form, must not collapse distinct
        // tuples to an empty string, and must ignore element names.
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("System.ValueTuple`2[System.Int32,System.String][]", reflection: true),
            ProgramSupport.CanonicalTypeSignature("(int, string)[]", reflection: false));
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("(int, string)", reflection: false),
            ProgramSupport.CanonicalTypeSignature("(int a, string b)", reflection: false));
        var a = ProgramSupport.CanonicalTypeSignature("(int, string)", reflection: false);
        var b = ProgramSupport.CanonicalTypeSignature("(double, float)", reflection: false);
        Assert.NotEqual(a, b);
        Assert.NotEqual(string.Empty, a);
    }

    [Fact]
    public void CanonicalTypeSignature_ExpandsNullableShorthandToNullable()
    {
        // C# nullable value-type shorthand (T?) must expand to System.Nullable<T> so it reconciles
        // with the runtime generic form (e.g. an array of nullable ints).
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("System.Nullable`1[System.Int32][]", reflection: true),
            ProgramSupport.CanonicalTypeSignature("int?[]", reflection: false));
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("System.Nullable`1[System.Int32]", reflection: true),
            ProgramSupport.CanonicalTypeSignature("int?", reflection: false));
    }

    [Fact]
    public void CanonicalTypeSignature_ReconcilesStaticAngleAndRuntimeBacktickForms()
    {
        var staticForm = ProgramSupport.CanonicalTypeSignature("System.Func<string, System.Lazy<int>>");
        var runtimeForm = ProgramSupport.CanonicalTypeSignature("System.Func`2[System.String,System.Lazy`1[System.Int32]]");

        Assert.Equal(staticForm, runtimeForm);
    }

    [Fact]
    public void CanonicalTypeSignature_DistinguishesDifferentTypeArguments()
    {
        var a = ProgramSupport.CanonicalTypeSignature("System.Func<string, System.Lazy<int>>");
        var b = ProgramSupport.CanonicalTypeSignature("System.Func<string, System.Lazy<long>>");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(
        "System.Func<System.Int32>",
        "System.Func`1[System.Int32]",
        true)]
    [InlineData(
        "System.Func<System.Int32>",
        "System.Func`1[System.String]",
        false)]
    [InlineData(
        "display class (N.C+<>c__DisplayClass1_0)",
        "N.C+<>c__DisplayClass1_0",
        true)]
    [InlineData(
        "state machine (N.C+<M>d__1)",
        "N.C+<M>d__1",
        true)]
    public void AllocationTypeMatch_PreservesExactTypeIdentity(
        string staticType,
        string runtimeType,
        bool expected)
    {
        var candidate = CandidateWithType(
            1,
            "Fixture.A.M()",
            staticType,
            detail: "delegate allocation");

        Assert.Equal(
            expected,
            candidate.MatchesAllocatedType(runtimeType));
    }

    [Fact]
    public void ApplyTypeConfirmation_MarksUnobservedCandidate_WhenPredictedTypeIsRealizedHot()
    {
        var candidate = CandidateWithType(1, "Aspire.ColorGenerator.GetColorIndex(string)", "System.Func<string, System.Lazy<int>>");
        var result = new CorrelationResult();
        result.Candidates.Add(candidate);
        // Runtime reports the reflection (backtick) form; site-join saw no frame for this method.
        result.RecordTypeVolume("System.Func`2[System.String,System.Lazy`1[System.Int32]]", 700_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(candidate.TypeConfirmed);
        Assert.Equal(700_000_000, candidate.TypeConfirmedBytes);
        Assert.False(candidate.TypeConfirmedAmbiguous);
        Assert.Equal("type-hot", candidate.Status);
    }

    [Theory]
    [InlineData("state machine (N.C+<M>d__1)", "N.C+<M>d__1")]
    [InlineData(
        "display class (N.C+<>c__DisplayClass1_0)",
        "N.C+<>c__DisplayClass1_0")]
    public void ApplyTypeConfirmation_UnwrapsProducerType(
        string predictedType,
        string runtimeType)
    {
        var candidate = CandidateWithType(
            1,
            "Fixture.A.M()",
            predictedType);
        var result = new CorrelationResult();
        result.Candidates.Add(candidate);
        result.RecordTypeVolume(
            runtimeType,
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(candidate.TypeConfirmed);
    }

    [Fact]
    public void ApplyTypeConfirmation_MarksAmbiguous_WhenMultipleSitesSharePredictedType()
    {
        var one = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.Func<string, System.Lazy<int>>",
            methodToken: 0x06000001);
        var two = CandidateWithType(
            2,
            "Fixture.B.M()",
            "System.Func<string, System.Lazy<int>>",
            methodToken: 0x06000002);
        var result = new CorrelationResult();
        result.Candidates.Add(one);
        result.Candidates.Add(two);
        result.RecordTypeVolume("System.Func`2[System.String,System.Lazy`1[System.Int32]]", 500_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(one.TypeConfirmedAmbiguous);
        Assert.True(two.TypeConfirmedAmbiguous);
        Assert.Equal(2, one.TypeConfirmedSiteCount);
        Assert.Equal("type-hot-ambiguous", one.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_PrefersTriageForDuplicatePhysicalSite()
    {
        var library = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library");
        var triage = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "triage");
        var result = new CorrelationResult();
        result.Candidates.Add(library);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);
        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(library.SupersededByTriage);
        Assert.False(library.TypeConfirmed);
        Assert.Equal("superseded-by-triage", library.Status);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(1, triage.TypeConfirmedSiteCount);
        Assert.Equal("type-hot", triage.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_PropagatesToProjectedPhysicalRow()
    {
        var library = CandidateWithType(
            1,
            "Fixture.A.M()",
            "Fixture.Specific",
            source: "library");
        var triage = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.Object",
            source: "triage");
        library.ProjectedByTriage = true;
        triage.ProjectedLibraries.Add(library);
        var result = new CorrelationResult();
        result.Candidates.Add(library);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "Fixture.Specific",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(triage.TypeConfirmed);
        Assert.Equal(
            "Fixture.Specific",
            triage.TypeConfirmedType);
        Assert.True(library.SupersededByTriage);
        Assert.Equal(
            "superseded-by-triage",
            library.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_KeepsTypeVolumeAndSiteCountAtomic()
    {
        var library = CandidateWithType(
            1,
            "Fixture.A.M()",
            "Fixture.Specific",
            source: "library");
        var target = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.Object",
            source: "triage");
        library.ProjectedByTriage = true;
        target.ProjectedLibraries.Add(library);
        var result = new CorrelationResult();
        result.Candidates.Add(library);
        result.Candidates.Add(target);
        for (int index = 0; index < 7; index++)
        {
            result.Candidates.Add(
                CandidateWithType(
                    3 + index,
                    $"Fixture.Other{index}.M()",
                    "System.Object",
                    methodToken:
                        0x06000010 + index));
        }
        result.RecordTypeVolume(
            "System.Object",
            2_000_000);
        result.RecordTypeVolume(
            "Fixture.Specific",
            3_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.Equal(
            "Fixture.Specific",
            target.TypeConfirmedType);
        Assert.Equal(
            3_000_000,
            target.TypeConfirmedBytes);
        Assert.Equal(
            1,
            target.TypeConfirmedSiteCount);
        Assert.False(target.TypeConfirmedAmbiguous);
    }

    [Fact]
    public void ApplyTypeConfirmation_ObservedRuntimeTypeExplainsVolume()
    {
        var observed = CandidateWithType(
            1,
            "Fixture.Observed.M()",
            "System.Object");
        var cold = CandidateWithType(
            2,
            "Fixture.Cold.M()",
            "System.String",
            methodToken: 0x06000002);
        var matched = new HashSet<int>();
        ProgramSupport.MarkAllocationHitForTest(
            observed,
            matched,
            "trace",
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);
        var result = new CorrelationResult();
        result.Candidates.Add(observed);
        result.Candidates.Add(cold);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(observed.IsObserved);
        Assert.False(cold.TypeConfirmed);
        Assert.Equal(
            "cold-for-this-workload",
            cold.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_CollapsesSharedEvidenceBody()
    {
        var first = CandidateWithType(
            1,
            "Fixture.A.SourceOne()",
            "System.String",
            source: "triage",
            methodToken: 0x06000001,
            evidenceMethodToken: 0x06000003);
        var second = CandidateWithType(
            2,
            "Fixture.A.SourceTwo()",
            "System.String",
            source: "triage",
            methodToken: 0x06000002,
            evidenceMethodToken: 0x06000003);
        var result = new CorrelationResult();
        result.Candidates.Add(first);
        result.Candidates.Add(second);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.Equal(1, first.TypeConfirmedSiteCount);
        Assert.Equal(1, second.TypeConfirmedSiteCount);
        Assert.Equal("type-hot", first.Status);
        Assert.Equal("type-hot", second.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_NormalizesAssemblyNameForDuplicatePhysicalSite()
    {
        var library = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            assemblyName: "Fixture.dll");
        var triage = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "triage",
            assemblyName: "Fixture");
        var result = new CorrelationResult();
        result.Candidates.Add(library);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(library.SupersededByTriage);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(1, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_UsesPhysicalSitesForCap()
    {
        var result = new CorrelationResult();
        var triageCandidates = new List<AllocationCandidate>();
        var libraryCandidates = new List<AllocationCandidate>();
        for (int i = 0; i < ProgramSupport.TypeConfirmMaxSites; i++)
        {
            int token = 0x06000001 + i;
            var library = CandidateWithType(
                (i * 2) + 1,
                $"Fixture.T{i}.M()",
                "System.String",
                source: "library",
                methodToken: token);
            var triage = CandidateWithType(
                (i * 2) + 2,
                $"Fixture.T{i}.M()",
                "System.String",
                source: "triage",
                methodToken: token);
            libraryCandidates.Add(library);
            triageCandidates.Add(triage);
            result.Candidates.Add(library);
            result.Candidates.Add(triage);
        }
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.All(
            libraryCandidates,
            candidate =>
            {
                Assert.True(candidate.SupersededByTriage);
                Assert.False(candidate.TypeConfirmed);
            });
        Assert.All(
            triageCandidates,
            candidate =>
            {
                Assert.True(candidate.TypeConfirmed);
                Assert.Equal(
                    ProgramSupport.TypeConfirmMaxSites,
                    candidate.TypeConfirmedSiteCount);
            });
    }

    [Fact]
    public void ApplyTypeConfirmation_StillRejectsAbovePhysicalSiteCap()
    {
        var result = new CorrelationResult();
        var triageCandidates = new List<AllocationCandidate>();
        var libraryCandidates = new List<AllocationCandidate>();
        for (int i = 0; i <= ProgramSupport.TypeConfirmMaxSites; i++)
        {
            int token = 0x06000001 + i;
            var library = CandidateWithType(
                (i * 2) + 1,
                $"Fixture.T{i}.M()",
                "System.String",
                source: "library",
                methodToken: token);
            libraryCandidates.Add(library);
            result.Candidates.Add(library);
            var triage = CandidateWithType(
                (i * 2) + 2,
                $"Fixture.T{i}.M()",
                "System.String",
                source: "triage",
                methodToken: token);
            triageCandidates.Add(triage);
            result.Candidates.Add(triage);
        }
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.All(
            triageCandidates,
            candidate => Assert.False(candidate.TypeConfirmed));
        Assert.All(
            libraryCandidates,
            candidate =>
            {
                Assert.False(candidate.SupersededByTriage);
                Assert.False(candidate.TypeConfirmed);
                Assert.Equal(
                    "cold-for-this-workload",
                    candidate.Status);
            });
    }

    [Fact]
    public void ApplyTypeConfirmation_CountsRepeatedTriageCoordinateOnce()
    {
        var result = new CorrelationResult();
        var triageCandidates = new List<AllocationCandidate>();
        for (int i = 0; i < ProgramSupport.TypeConfirmMaxSites; i++)
        {
            int token = 0x06000001 + i;
            for (int duplicate = 0; duplicate < 2; duplicate++)
            {
                var triage = CandidateWithType(
                    (i * 2) + duplicate,
                    $"Fixture.T{i}.M()",
                    "System.String",
                    source: "triage",
                    methodToken: token);
                triageCandidates.Add(triage);
                result.Candidates.Add(triage);
            }
        }
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.All(
            triageCandidates,
            candidate =>
            {
                Assert.True(candidate.TypeConfirmed);
                Assert.Equal(
                    ProgramSupport.TypeConfirmMaxSites,
                    candidate.TypeConfirmedSiteCount);
            });
    }

    [Fact]
    public void ApplyTypeConfirmation_CountsUnknownTriageInputsSeparately()
    {
        var first = CandidateWithType(
            1,
            "Fixture.T.M()",
            "System.String",
            source: "triage",
            libraryPath: "/tmp/triage-a.json");
        var second = CandidateWithType(
            2,
            "Fixture.T.M()",
            "System.String",
            source: "triage",
            libraryPath: "/tmp/triage-b.json");
        var result = new CorrelationResult();
        result.Candidates.Add(first);
        result.Candidates.Add(second);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(first.TypeConfirmed);
        Assert.True(second.TypeConfirmed);
        Assert.Equal(2, first.TypeConfirmedSiteCount);
        Assert.Equal(2, second.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotCollapseAmbiguousLibraryVersions()
    {
        var firstVersion = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            moduleVersionId: Guid.Parse(
                "11111111-1111-1111-1111-111111111111"));
        var secondVersion = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            moduleVersionId: Guid.Parse(
                "22222222-2222-2222-2222-222222222222"));
        var triage = CandidateWithType(
            3,
            "Fixture.A.M()",
            "System.String",
            source: "triage");
        var result = new CorrelationResult();
        result.Candidates.Add(firstVersion);
        result.Candidates.Add(secondVersion);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(firstVersion.SupersededByTriage);
        Assert.False(secondVersion.SupersededByTriage);
        Assert.True(firstVersion.TypeConfirmed);
        Assert.True(secondVersion.TypeConfirmed);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(3, firstVersion.TypeConfirmedSiteCount);
        Assert.Equal(3, secondVersion.TypeConfirmedSiteCount);
        Assert.Equal(3, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotCollapseUnknownLibraryInputs()
    {
        var firstLibrary = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            libraryPath: "/tmp/first/Fixture.dll");
        var secondLibrary = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            libraryPath: "/tmp/second/Fixture.dll");
        var triage = CandidateWithType(
            3,
            "Fixture.A.M()",
            "System.String",
            source: "triage");
        var result = new CorrelationResult();
        result.Candidates.Add(firstLibrary);
        result.Candidates.Add(secondLibrary);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(firstLibrary.SupersededByTriage);
        Assert.False(secondLibrary.SupersededByTriage);
        Assert.True(firstLibrary.TypeConfirmed);
        Assert.True(secondLibrary.TypeConfirmed);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(3, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_CollapsesRepeatedUnknownLibraryInput()
    {
        string absolutePath = Path.Combine(
            Environment.CurrentDirectory,
            "Fixture.dll");
        var firstLibraryRow = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            libraryPath: absolutePath);
        var secondLibraryRow = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            libraryPath: "Fixture.dll");
        var triage = CandidateWithType(
            3,
            "Fixture.A.M()",
            "System.String",
            source: "triage");
        var result = new CorrelationResult();
        result.Candidates.Add(firstLibraryRow);
        result.Candidates.Add(secondLibraryRow);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(firstLibraryRow.SupersededByTriage);
        Assert.True(secondLibraryRow.SupersededByTriage);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(1, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_CollapsesKnownBuildAcrossInputPaths()
    {
        Guid moduleVersionId = Guid.Parse(
            "11111111-1111-1111-1111-111111111111");
        var firstLibraryRow = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            moduleVersionId: moduleVersionId,
            libraryPath: "/tmp/first/Fixture.dll");
        var secondLibraryRow = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            moduleVersionId: moduleVersionId,
            libraryPath: "/tmp/second/Fixture.dll");
        var triage = CandidateWithType(
            3,
            "Fixture.A.M()",
            "System.String",
            source: "triage");
        var result = new CorrelationResult();
        result.Candidates.Add(firstLibraryRow);
        result.Candidates.Add(secondLibraryRow);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(firstLibraryRow.SupersededByTriage);
        Assert.True(secondLibraryRow.SupersededByTriage);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(1, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_KnownTriageDoesNotCollapseUnknownLibrary()
    {
        var library = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library");
        var triage = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "triage",
            moduleVersionId: Guid.Parse(
                "11111111-1111-1111-1111-111111111111"));
        var result = new CorrelationResult();
        result.Candidates.Add(library);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(library.SupersededByTriage);
        Assert.True(library.TypeConfirmed);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(2, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void FindTraceLibrariesSupersededByTriage_NoTriage_DoesNotAllocate()
    {
        var library = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library");
        AllocationCandidate[] candidates =
            [library];
        _ = ProgramSupport.FindTraceLibrariesSupersededByTriage(
            candidates,
            candidates,
            "System.String");

        long before =
            GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = ProgramSupport.FindTraceLibrariesSupersededByTriage(
                candidates,
                candidates,
                "System.String");
        }
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FindTraceLibrariesSupersededByTriage_TriageOnly_DoesNotAllocate()
    {
        var triage = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "triage");
        AllocationCandidate[] candidates =
            [triage];
        _ = ProgramSupport.FindTraceLibrariesSupersededByTriage(
            candidates,
            candidates,
            "System.String");

        long before =
            GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = ProgramSupport.FindTraceLibrariesSupersededByTriage(
                candidates,
                candidates,
                "System.String");
        }
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FindTraceLibrariesSupersededByTriage_PreservesDifferentNearestOffsets()
    {
        var triage = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "triage",
            ilOffset: 0x0010,
            libraryPath: "/tmp/triage.json");
        var library = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            ilOffset: 0x0020,
            moduleVersionId: Guid.Parse(
                "22222222-2222-2222-2222-222222222222"));

        var superseded =
            ProgramSupport.FindTraceLibrariesSupersededByTriage(
                [triage, library],
                [triage, library],
                "System.String");

        Assert.Empty(superseded);
    }

    [Fact]
    public void FindTraceLibrariesSupersededByTriage_CollapsesSameNearestOffset()
    {
        var triage = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "triage",
            ilOffset: 0x0010,
            libraryPath: "/tmp/triage.json");
        var library = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            ilOffset: 0x0010,
            moduleVersionId: Guid.Parse(
                "22222222-2222-2222-2222-222222222222"));

        var superseded =
            ProgramSupport.FindTraceLibrariesSupersededByTriage(
                [triage, library],
                [triage, library],
                "System.String");

        Assert.Collection(
            superseded,
            candidate => Assert.Same(library, candidate));
    }

    [Fact]
    public void FindTraceLibrariesSupersededByTriage_SupportCoordinateDoesNotRequireSampledTypeMatch()
    {
        Guid mvid = Guid.Parse(
            "22222222-2222-2222-2222-222222222222");
        var triage = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.Func<System.String, bool>",
            source: "triage",
            moduleVersionId: mvid,
            libraryPath: "/tmp/triage.json",
            supportingCallSite: true);
        var library = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.Func<System.String, bool>",
            source: "library",
            moduleVersionId: mvid);

        var superseded =
            ProgramSupport
                .FindTraceLibrariesSupersededByTriage(
                    [triage, library],
                    [triage, library],
                    "System.Linq.Enumerable+WhereIterator<System.String>");

        Assert.Collection(
            superseded,
            candidate => Assert.Same(
                library,
                candidate));
    }

    [Fact]
    public void ApplyAcceptedSupportPrecedence_IsIndependentPerBuild()
    {
        Guid firstMvid = Guid.Parse(
            "11111111-1111-1111-1111-111111111111");
        Guid secondMvid = Guid.Parse(
            "22222222-2222-2222-2222-222222222222");
        var firstExact = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.Object",
            source: "triage",
            moduleVersionId: firstMvid);
        var firstSupport = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.Object",
            source: "triage",
            moduleVersionId: firstMvid,
            supportingCallSite: true);
        var secondExact = CandidateWithType(
            3,
            "Fixture.A.M()",
            "System.Object",
            source: "triage",
            moduleVersionId: secondMvid);
        var secondSupport = CandidateWithType(
            4,
            "Fixture.A.M()",
            "System.Object",
            source: "triage",
            moduleVersionId: secondMvid,
            supportingCallSite: true);

        var selected = ProgramSupport
            .ApplyAcceptedSupportPrecedence(
                [
                    firstExact,
                    firstSupport,
                    secondExact,
                    secondSupport,
                ]);

        Assert.Equal(
            [firstSupport, secondSupport],
            selected);
        Assert.True(firstExact.SupersededByTriage);
        Assert.True(secondExact.SupersededByTriage);
    }

    [Fact]
    public void ApplyTypeConfirmation_CollapsesOnlyMatchingTriageModuleVersion()
    {
        Guid firstMvid = Guid.Parse(
            "11111111-1111-1111-1111-111111111111");
        Guid secondMvid = Guid.Parse(
            "22222222-2222-2222-2222-222222222222");
        var firstVersion = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            moduleVersionId: firstMvid);
        var secondVersion = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            moduleVersionId: secondMvid);
        var triage = CandidateWithType(
            3,
            "Fixture.A.M()",
            "System.String",
            source: "triage",
            moduleVersionId: firstMvid);
        var result = new CorrelationResult();
        result.Candidates.Add(firstVersion);
        result.Candidates.Add(secondVersion);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.True(firstVersion.SupersededByTriage);
        Assert.False(firstVersion.TypeConfirmed);
        Assert.False(secondVersion.SupersededByTriage);
        Assert.True(secondVersion.TypeConfirmed);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(2, secondVersion.TypeConfirmedSiteCount);
        Assert.Equal(2, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotDeduplicateDifferentAssemblies()
    {
        var library = CandidateWithType(
            1,
            "Fixture.A.M()",
            "System.String",
            source: "library",
            assemblyName: "Fixture.One");
        var triage = CandidateWithType(
            2,
            "Fixture.A.M()",
            "System.String",
            source: "triage",
            assemblyName: "Fixture.Two");
        var result = new CorrelationResult();
        result.Candidates.Add(library);
        result.Candidates.Add(triage);
        result.RecordTypeVolume(
            "System.String",
            ProgramSupport.TypeConfirmMinBytes);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(library.SupersededByTriage);
        Assert.True(library.TypeConfirmed);
        Assert.True(triage.TypeConfirmed);
        Assert.Equal(2, library.TypeConfirmedSiteCount);
        Assert.Equal(2, triage.TypeConfirmedSiteCount);
    }

    [Fact]
    public void ApplyTypeConfirmation_LeavesColdCandidateCold_WhenTypeNotRealized()
    {
        var candidate = CandidateWithType(1, "Fixture.A.M()", "System.Func<string, System.Lazy<int>>");
        var result = new CorrelationResult();
        result.Candidates.Add(candidate);
        result.RecordTypeVolume("System.String", 900_000_000); // a different type is hot

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(candidate.TypeConfirmed);
        Assert.Equal("cold-for-this-workload", candidate.Status);
    }

    [Fact]
    public void CanonicalTypeSignature_DistinguishesArraysFromScalarsAndRanks()
    {
        var scalar = ProgramSupport.CanonicalTypeSignature("int");
        var array = ProgramSupport.CanonicalTypeSignature("int[]");
        var runtimeArray = ProgramSupport.CanonicalTypeSignature("System.Int32[]");
        var jagged = ProgramSupport.CanonicalTypeSignature("int[][]");
        var rank2 = ProgramSupport.CanonicalTypeSignature("int[,]");

        Assert.Equal(array, runtimeArray);       // static alias array == runtime array
        Assert.NotEqual(scalar, array);          // scalar != array
        Assert.NotEqual(array, jagged);          // [] != [][]
        Assert.NotEqual(array, rank2);           // [] != [,]
    }

    [Fact]
    public void CanonicalTypeSignature_DistinguishesLeafNameNamespaceCollisions()
    {
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("A.Foo"),
            ProgramSupport.CanonicalTypeSignature("B.Foo"));
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesCompilerGeneratedNames()
    {
        // A closure/state-machine name must not collapse to its parent type (previously
        // Enumerable+<>c__DisplayClass18_0 canonicalized to Enumerable), and two distinct closures
        // in the same parent must stay distinct.
        var parent = ProgramSupport.CanonicalTypeSignature("System.Linq.Enumerable");
        var closure = ProgramSupport.CanonicalTypeSignature("System.Linq.Enumerable+<>c__DisplayClass18_0");
        var otherClosure = ProgramSupport.CanonicalTypeSignature("System.Linq.Enumerable+<>c__DisplayClass19_0");

        Assert.NotEqual(parent, closure);
        Assert.NotEqual(closure, otherClosure);
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesNestedTypeAfterGenericArguments()
    {
        // A nested type on a generic parent must not be dropped. Reflection emits the parent's type
        // arguments in the trailing bracket after the whole nested chain
        // (Dictionary`2+KeyCollection[String,Int32]); the nested name must survive and the arguments
        // must bind to the parent, matching the display form Dictionary<String,Int32>.KeyCollection.
        var runtime = ProgramSupport.CanonicalTypeSignature(
            "System.Collections.Generic.Dictionary`2+KeyCollection[System.String,System.Int32]");
        var display = ProgramSupport.CanonicalTypeSignature(
            "System.Collections.Generic.Dictionary<System.String,System.Int32>.KeyCollection");
        var parentOnly = ProgramSupport.CanonicalTypeSignature(
            "System.Collections.Generic.Dictionary`2[System.String,System.Int32]");

        Assert.Equal(display, runtime);       // reflection nested form == display nested form
        Assert.NotEqual(parentOnly, runtime); // KeyCollection is not dropped
    }

    [Fact]
    public void CanonicalTypeSignature_DistributesTrailingArgsAcrossNestedGenerics()
    {
        // Reflection puts the whole chain's arguments in one trailing bracket, distributed by each
        // segment's arity (Outer`1+Inner`1[A,B] == Outer<A>.Inner<B>). Previously the arity of the
        // non-final segment was dropped and the arguments misattributed to the last segment.
        var runtime = ProgramSupport.CanonicalTypeSignature("Outer`1+Inner`1[System.Int32,System.String]");
        var display = ProgramSupport.CanonicalTypeSignature("Outer<System.Int32>.Inner<System.String>");

        Assert.Equal(display, runtime);
        // Swapping the arguments across the two levels must not collide.
        Assert.NotEqual(
            runtime,
            ProgramSupport.CanonicalTypeSignature("Outer`1+Inner`1[System.String,System.Int32]"));
    }

    [Fact]
    public void CanonicalTypeSignature_HandlesAssemblyQualifiedGenericArguments()
    {
        // Assembly-qualified reflection long form must not hang and must reconcile with the short
        // form (the assembly qualifier is stripped).
        var longForm = ProgramSupport.CanonicalTypeSignature(
            "System.Func`2[[System.String, System.Private.CoreLib],[System.Int32, System.Private.CoreLib]]");
        var shortForm = ProgramSupport.CanonicalTypeSignature("System.Func`2[System.String,System.Int32]");

        Assert.Equal(shortForm, longForm);
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesBacktickOnlyGenericArity()
    {
        // An open generic definition (backtick arity with no argument bracket) must keep its arity so
        // distinct definitions do not collide (My.Generic`2 != My.Generic`3 != My.Generic).
        var arity2 = ProgramSupport.CanonicalTypeSignature("My.Generic`2");
        var arity3 = ProgramSupport.CanonicalTypeSignature("My.Generic`3");
        var nonGeneric = ProgramSupport.CanonicalTypeSignature("My.Generic");

        Assert.NotEqual(arity2, arity3);
        Assert.NotEqual(arity2, nonGeneric);
    }

    [Fact]
    public void CanonicalTypeSignature_ReconcilesNestedGenericArgumentOrdering()
    {
        // A nested generic type must canonicalize the same regardless of whether the parent's
        // arguments trail the whole chain (reflection's normal form) or precede the nested separator.
        var trailing = ProgramSupport.CanonicalTypeSignature(
            "System.Collections.Generic.Dictionary`2+KeyCollection[System.String,System.Int32]");
        var preSeparator = ProgramSupport.CanonicalTypeSignature(
            "System.Collections.Generic.Dictionary`2[System.String,System.Int32]+KeyCollection");
        var display = ProgramSupport.CanonicalTypeSignature(
            "System.Collections.Generic.Dictionary<System.String,System.Int32>.KeyCollection");

        Assert.Equal(display, trailing);
        Assert.Equal(display, preSeparator);
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesGenericArgumentOrderAndSeparators()
    {
        // The argument separator must be preserved so different splits do not collide.
        Assert.NotEqual(
            ProgramSupport.CanonicalTypeSignature("Func<AB, C>"),
            ProgramSupport.CanonicalTypeSignature("Func<A, BC>"));
    }

    [Fact]
    public void CanonicalTypeSignature_PreservesUnboundGenericArity()
    {
        var arity2 = ProgramSupport.CanonicalTypeSignature("System.String<,>");
        var arity0 = ProgramSupport.CanonicalTypeSignature("System.String<>");
        var arity3 = ProgramSupport.CanonicalTypeSignature("System.String<,,>");

        Assert.NotEqual(arity2, arity0);
        Assert.NotEqual(arity2, arity3);
    }

    [Fact]
    public void CanonicalTypeSignature_ReconcilesArrayOfGenericAcrossForms()
    {
        Assert.Equal(
            ProgramSupport.CanonicalTypeSignature("Entry<System.String,System.Object>[]"),
            ProgramSupport.CanonicalTypeSignature("Entry`2[System.String,System.Object][]"));
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotConfirmColdSite_WhenTypeAlreadySiteObserved()
    {
        // A hot site-observed candidate explains the String volume; a cold same-type site must not
        // steal that credit as a "unique" type-hot confirmation.
        var observed = CandidateWithType(1, "Fixture.Hot.M()", "System.String");
        observed.AllocationHits = 1;
        observed.AllocationBytes = 900_000_000;
        var cold = CandidateWithType(2, "Fixture.Cold.M()", "System.String");
        var result = new CorrelationResult();
        result.Candidates.Add(observed);
        result.Candidates.Add(cold);
        result.RecordTypeVolume("System.String", 900_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(cold.TypeConfirmed);
        Assert.Equal("cold-for-this-workload", cold.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_WrappedObservedTypeExplainsRuntimeVolume()
    {
        var observed = CandidateWithType(
            1,
            "Fixture.Hot.M()",
            "boxed System.Int32");
        observed.AllocationHits = 1;
        observed.AllocationBytes = 900_000_000;
        var cold = CandidateWithType(
            2,
            "Fixture.Cold.M()",
            "System.Int32");
        var result = new CorrelationResult();
        result.Candidates.Add(observed);
        result.Candidates.Add(cold);
        result.RecordTypeVolume("System.Int32", 900_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(cold.TypeConfirmed);
        Assert.Equal("cold-for-this-workload", cold.Status);
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotConfirm_BelowVolumeFloor()
    {
        var candidate = CandidateWithType(1, "Fixture.A.M()", "System.Func<string, System.Lazy<int>>");
        var result = new CorrelationResult();
        result.Candidates.Add(candidate);
        result.RecordTypeVolume("System.Func`2[System.String,System.Lazy`1[System.Int32]]", ProgramSupport.TypeConfirmMinBytes - 1);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.False(candidate.TypeConfirmed);
    }

    [Fact]
    public void ApplyTypeConfirmation_DoesNotConfirm_WhenTooManySitesShareType()
    {
        var result = new CorrelationResult();
        for (int i = 0; i < ProgramSupport.TypeConfirmMaxSites + 1; i++)
        {
            result.Candidates.Add(CandidateWithType(
                i + 1,
                $"Fixture.T{i}.M()",
                "System.String",
                methodToken: 0x06000001 + i));
        }
        result.RecordTypeVolume("System.String", 900_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.All(result.Candidates, c => Assert.False(c.TypeConfirmed));
    }

    [Fact]
    public void ApplyTypeConfirmation_CountsCoordinateLessDuplicatesOnce()
    {
        var result = new CorrelationResult();
        for (int i = 0;
             i < ProgramSupport.TypeConfirmMaxSites + 1;
             i++)
        {
            result.Candidates.Add(
                CandidateWithType(
                    i + 1,
                    "Fixture.T.M()",
                    "System.String",
                    source: "triage",
                    assemblyName: "",
                    methodToken: 0,
                    ilOffset: 0,
                    libraryPath: ""));
        }
        result.RecordTypeVolume(
            "System.String",
            900_000_000);

        ProgramSupport.ApplyTypeConfirmation(result);

        Assert.All(
            result.Candidates,
            candidate =>
            {
                Assert.True(candidate.TypeConfirmed);
                Assert.Equal(
                    1,
                    candidate.TypeConfirmedSiteCount);
            });
    }

    [Fact]
    public void AllocationCandidate_FromOccurrence_TreatsEmptyMvidAsUnknown()
    {
        var method = new ILInspector.Analysis.MethodIdentity(
            "Fixture",
            Guid.Empty,
            ILInspector.Analysis.TypeRef.Definition(
                "Fixture",
                "Fixture",
                "A"),
            "M",
            [],
            ILInspector.Analysis.TypeRef.CoreLib(
                "System",
                "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
        var occurrence = new ILInspector.Analysis.AllocationOccurrence(
            method,
            ILOffset: 0x10,
            OperandToken: null,
            ILInspector.Analysis.AllocationKind.Object,
            AllocatedType: ILInspector.Analysis.TypeRef.CoreLib(
                "System",
                "Object"),
            Detail: null,
            CountsAsHeapAllocation: true,
            ILInspector.Analysis.AllocationFrequency.Always,
            InLoop: false,
            ILInspector.Analysis.AllocationEscape.Escapes,
            ILInspector.Analysis.AllocationFactSource.Newobj);

        var candidate = AllocationCandidate.FromOccurrence(
            1,
            "/tmp/Fixture.dll",
            occurrence);

        Assert.Null(candidate.ModuleVersionId);
    }

    static AllocationCandidate CandidateWithType(
        int id,
        string method,
        string predictedType,
        string? detail = null,
        string source = "library",
        string assemblyName = "Fixture",
        int methodToken = 0x06000001,
        int ilOffset = 0x0010,
        Guid? moduleVersionId = null,
        string libraryPath = "/tmp/Fixture.dll",
        int? evidenceMethodToken = null,
        bool supportingCallSite = false)
    {
        string methodKey = method[..method.IndexOf('(')];
        int lastDot = methodKey.LastIndexOf('.');
        string stackKey = lastDot < 0 ? methodKey : $"{methodKey[..lastDot]}::{methodKey[(lastDot + 1)..]}";
        return new(
            id,
            source,
            libraryPath,
            assemblyName,
            moduleVersionId,
            methodToken,
            ilOffset,
            method,
            methodKey,
            stackKey,
            "Delegate",
            predictedType,
            detail,
            false,
            "Always",
            "Escapes",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            evidenceMethodToken: evidenceMethodToken,
            supportingCallSite: supportingCallSite);
    }
}
