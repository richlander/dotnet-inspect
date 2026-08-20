using System.Collections.Generic;
using System.Linq;

public class CandidateLookupTests
{
    [Fact]
    public void FindNearestByTokenOffset_ReturnsNearestPrecedingAllocationInSameModule()
    {
        var before = Candidate(id: 1, methodToken: 0x06000001, ilOffset: 0x0010);
        var nearest = Candidate(id: 2, methodToken: 0x06000001, ilOffset: 0x0020);
        var after = Candidate(id: 3, methodToken: 0x06000001, ilOffset: 0x0030);
        var otherModule = Candidate(id: 4, methodToken: 0x06000001, ilOffset: 0x0028, libraryPath: "/tmp/Other.dll");
        var lookup = CandidateLookup.Create([before, nearest, after, otherModule]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000001,
            0x0025,
            "/tmp/Fixture.dll",
            "Fixture",
            "Fixture.Type::M");

        var match = Assert.Single(matches);
        Assert.Same(nearest, match);
    }

    [Fact]
    public void FindNearestByTokenOffset_ReturnsAllRowsAtNearestOffsetForAmbiguityGating()
    {
        var first = Candidate(id: 1, methodToken: 0x06000002, ilOffset: 0x0010, kind: "Closure");
        var second = Candidate(id: 2, methodToken: 0x06000002, ilOffset: 0x0010, kind: "Delegate");
        var lookup = CandidateLookup.Create([first, second]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000002,
            0x0015,
            "/tmp/Fixture.dll",
            "Fixture",
            "Fixture.Type::M");

        Assert.Equal([first.Id, second.Id], matches.Select(static candidate => candidate.Id).Order());
    }

    [Fact]
    public void FindNearestByTokenOffset_SelectsNearestWithinEachBuild()
    {
        var firstBuild = Candidate(
            id: 1,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            moduleVersionId:
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"));
        var secondBuild = Candidate(
            id: 2,
            methodToken: 0x06000002,
            ilOffset: 0x0020,
            moduleVersionId:
                Guid.Parse(
                    "22222222-2222-2222-2222-222222222222"));
        var lookup = CandidateLookup.Create(
            [firstBuild, secondBuild]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000002,
            0x0020,
            "/tmp/Fixture.dll",
            "Fixture",
            "Fixture.Type.M()");

        Assert.Equal(
            [firstBuild.Id, secondBuild.Id],
            matches
                .Select(static candidate =>
                    candidate.Id)
                .Order());
    }

    [Fact]
    public void FindNearestByTokenOffset_FallsBackToMethodNameWhenModuleIsUnavailable()
    {
        var target = Candidate(id: 1, methodToken: 0x06000003, ilOffset: 0x0040);
        var unrelated = Candidate(id: 2, methodToken: 0x06000003, ilOffset: 0x0050, method: "Other.Type.M()");
        var lookup = CandidateLookup.Create([target, unrelated]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000003,
            0x0060,
            modulePath: null,
            moduleName: null,
            methodName: "Fixture.Type::M");

        var match = Assert.Single(matches);
        Assert.Same(target, match);
    }

    [Fact]
    public void FindNearestByTokenOffset_MatchesNativeImageModuleNames()
    {
        var candidate = Candidate(id: 1, methodToken: 0x06000004, ilOffset: 0x0010, libraryPath: "/tmp/Fixture.dll");
        var lookup = CandidateLookup.Create([candidate]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000004,
            0x0011,
            modulePath: "/runtime/Fixture.ni.dll",
            moduleName: "Fixture.ni.dll",
            methodName: null);

        var match = Assert.Single(matches);
        Assert.Same(candidate, match);
    }

    [Fact]
    public void FindNearestByTokenOffset_DoesNotFallbackAcrossAssembliesWithoutMethodName()
    {
        var candidate = Candidate(id: 1, methodToken: 0x06000005, ilOffset: 0x0010, libraryPath: "/tmp/Fixture.dll");
        var lookup = CandidateLookup.Create([candidate]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000005,
            0x0011,
            modulePath: null,
            moduleName: null,
            methodName: null);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindNearestByTokenOffset_DoesNotUseSourceNameForDistinctEvidenceBody()
    {
        var candidate = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            evidenceMethodToken: 0x06000002);
        var lookup = CandidateLookup.Create([candidate]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000002,
            0x0011,
            modulePath: null,
            moduleName: null,
            methodName: "Fixture.Type::M");

        Assert.Empty(matches);
    }

    [Fact]
    public void FindNearestByTokenOffset_DoesNotUseSourceNameWhenSourceTokenIsUnknown()
    {
        var candidate = Candidate(
            id: 1,
            methodToken: 0,
            ilOffset: 0x0010,
            evidenceMethodToken: 0x06000002);
        var lookup = CandidateLookup.Create([candidate]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000002,
            0x0011,
            modulePath: null,
            moduleName: null,
            methodName: "Fixture.Type::M");

        Assert.Empty(matches);
    }

    [Fact]
    public void FindByMethodText_RetainsSourceNameForDistinctEvidenceBody()
    {
        var candidate = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            evidenceMethodToken: 0x06000002);
        var lookup = CandidateLookup.Create([candidate]);

        Assert.Contains(
            candidate,
            lookup.FindByMethodText(
                    "sample: Fixture.Type::M")
                .Select(static match =>
                    match.Candidate));
    }

    [Fact]
    public void FindByTokenOffset_RejectsCoordinateSharedAcrossAssemblies()
    {
        var assemblyA = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            assemblyName: "AssemblyA",
            evidenceMethodToken: 0x06000003);
        var assemblyB = Candidate(
            id: 2,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            assemblyName: "AssemblyB",
            evidenceMethodToken: 0x06000003);
        var lookup = CandidateLookup.Create(
            [assemblyA, assemblyB]);

        Assert.Empty(
            lookup.FindByTokenOffset(
                0x06000003,
                0x0010));
    }

    [Fact]
    public void FindByTokenOffset_RejectsCoordinateSharedAcrossModuleVersions()
    {
        var firstBuild = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            moduleVersionId:
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"),
            evidenceMethodToken: 0x06000003);
        var secondBuild = Candidate(
            id: 2,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            moduleVersionId:
                Guid.Parse(
                    "22222222-2222-2222-2222-222222222222"),
            evidenceMethodToken: 0x06000003);
        var lookup = CandidateLookup.Create(
            [firstBuild, secondBuild]);

        Assert.Empty(
            lookup.FindByTokenOffset(
                0x06000003,
                0x0010));
    }

    [Fact]
    public void Create_ProjectsMatchingBuildBeforeRejectingCoordinate()
    {
        var library = Candidate(
            id: 1,
            methodToken: 0x06000003,
            ilOffset: 0x0010,
            moduleVersionId:
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"));
        var matchingTriage = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            moduleVersionId:
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"),
            evidenceMethodToken: 0x06000003,
            source: "triage");
        var otherBuildTriage = Candidate(
            id: 3,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            moduleVersionId:
                Guid.Parse(
                    "22222222-2222-2222-2222-222222222222"),
            evidenceMethodToken: 0x06000003,
            source: "triage");

        var lookup = CandidateLookup.Create(
            [
                library,
                matchingTriage,
                otherBuildTriage
            ]);

        Assert.True(library.ProjectedByTriage);
        Assert.False(library.SupersededByTriage);
        Assert.Empty(
            lookup.FindByTokenOffset(
                0x06000003,
                0x0010));
        Assert.Equal(
            [matchingTriage.Id, otherBuildTriage.Id],
            lookup.FindRejectedByTokenOffset(
                    0x06000003,
                    0x0010)
                .Select(static candidate =>
                    candidate.Id)
                .Order());
    }

    [Fact]
    public void Create_DoesNotProjectAcrossAssemblies()
    {
        var library = Candidate(
            id: 1,
            methodToken: 0x06000003,
            ilOffset: 0x0010,
            assemblyName: "AssemblyA");
        var triage = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            assemblyName: "AssemblyB",
            evidenceMethodToken: 0x06000003,
            source: "triage");

        var lookup = CandidateLookup.Create(
            [library, triage]);

        Assert.False(library.ProjectedByTriage);
        Assert.False(library.SupersededByTriage);
        Assert.Empty(
            lookup.FindByTokenOffset(
                0x06000003,
                0x0010));
        Assert.Equal(
            [library.Id, triage.Id],
            lookup.FindRejectedByTokenOffset(
                    0x06000003,
                    0x0010)
                .Select(static candidate =>
                    candidate.Id)
                .Order());
    }

    [Fact]
    public void Create_ProjectsLegacyRowWithinMatchingAssembly()
    {
        var matchingLibrary = Candidate(
            id: 1,
            methodToken: 0x06000003,
            ilOffset: 0x0010,
            assemblyName: "AssemblyA");
        var triage = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            assemblyName: "AssemblyA",
            evidenceMethodToken: 0x06000003,
            source: "triage");
        var unrelatedLibrary = Candidate(
            id: 3,
            methodToken: 0x06000003,
            ilOffset: 0x0010,
            assemblyName: "AssemblyB");

        CandidateLookup.Create(
            [
                matchingLibrary,
                triage,
                unrelatedLibrary
            ]);

        Assert.True(
            matchingLibrary.ProjectedByTriage);
        Assert.False(
            unrelatedLibrary.ProjectedByTriage);
    }

    [Fact]
    public void Create_ScopesShapeAmbiguityToActivePhysicalMethod()
    {
        var library = Candidate(
            id: 1,
            methodToken: 0x06000003,
            ilOffset: 0x0010);
        var triage = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            evidenceMethodToken: 0x06000003,
            source: "triage");
        var otherAssembly = Candidate(
            id: 3,
            methodToken: 0x06000003,
            ilOffset: 0x0010,
            assemblyName: "OtherAssembly");

        CandidateLookup.Create(
            [library, triage, otherAssembly]);

        Assert.True(library.ProjectedByTriage);
        Assert.Equal(1, triage.SameMethodShapeRows);
        Assert.Equal(
            1,
            otherAssembly.SameMethodShapeRows);
    }

    [Fact]
    public void Create_ScopesTokenlessAmbiguityByMethod()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0,
            ilOffset: -1,
            method: "Fixture.Type.First()");
        var second = Candidate(
            id: 2,
            methodToken: 0,
            ilOffset: -1,
            method: "Fixture.Type.Second()");

        CandidateLookup.Create([first, second]);

        Assert.Equal(1, first.SameMethodShapeRows);
        Assert.Equal(1, second.SameMethodShapeRows);
    }

    [Fact]
    public void AttributeBytes_IsStableAndRotatesRemainders()
    {
        var objectRow = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "Object");
        var delegateRow = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "Delegate");
        var lookup = CandidateLookup.Create(
            [objectRow, delegateRow]);

        var first = lookup.AttributeBytes(
            [objectRow, delegateRow],
            1);
        var second = lookup.AttributeBytes(
            [delegateRow, objectRow],
            1);

        Assert.Equal(
            1,
            first.Values.Sum());
        Assert.Equal(
            1,
            second.Values.Sum());
        Assert.Equal(
            1,
            first[objectRow.Id]
                + second[objectRow.Id]);
        Assert.Equal(
            1,
            first[delegateRow.Id]
                + second[delegateRow.Id]);

        var reversedObject = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "Object");
        var reversedDelegate = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "Delegate");
        var reversedLookup =
            CandidateLookup.Create(
                [
                    reversedDelegate,
                    reversedObject
                ]);
        var reorderedFirst =
            reversedLookup.AttributeBytes(
                [
                    reversedObject,
                    reversedDelegate
                ],
                1);

        Assert.Equal(
            first[objectRow.Id],
            reorderedFirst[
                reversedObject.Id]);
        Assert.Equal(
            first[delegateRow.Id],
            reorderedFirst[
                reversedDelegate.Id]);
    }

    [Fact]
    public void AttributeBytes_LengthPrefixesUserControlledIdentity()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            method: "Fixture.Type.M()\u001fY",
            kind: "Z");
        var second = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            method: "Fixture.Type.M()",
            kind: "Y\u001fZ");
        var lookup = CandidateLookup.Create(
            [first, second]);
        var attributed = lookup.AttributeBytes(
            [first, second],
            1);

        var reversedFirst = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            method: "Fixture.Type.M()\u001fY",
            kind: "Z");
        var reversedSecond = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            method: "Fixture.Type.M()",
            kind: "Y\u001fZ");
        var reversedLookup = CandidateLookup.Create(
            [reversedSecond, reversedFirst]);
        var reversed = reversedLookup.AttributeBytes(
            [reversedFirst, reversedSecond],
            1);

        Assert.Equal(
            attributed[first.Id],
            reversed[reversedFirst.Id]);
        Assert.Equal(
            attributed[second.Id],
            reversed[reversedSecond.Id]);
    }

    [Fact]
    public void AttributeBytes_IncludesSourceTokenAndUnknownBuild()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            libraryPath: "/tmp/First/Fixture.dll",
            evidenceMethodToken: 0x06000003);
        var second = Candidate(
            id: 2,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            libraryPath: "/tmp/Second/Fixture.dll",
            evidenceMethodToken: 0x06000003);
        var lookup = CandidateLookup.Create(
            [first, second]);
        var attributed = lookup.AttributeBytes(
            [first, second],
            1);

        var reversedFirst = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            libraryPath: "/tmp/First/Fixture.dll",
            evidenceMethodToken: 0x06000003);
        var reversedSecond = Candidate(
            id: 1,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            libraryPath: "/tmp/Second/Fixture.dll",
            evidenceMethodToken: 0x06000003);
        var reversedLookup = CandidateLookup.Create(
            [reversedSecond, reversedFirst]);
        var reversed = reversedLookup.AttributeBytes(
            [reversedFirst, reversedSecond],
            1);

        Assert.Equal(
            attributed[first.Id],
            reversed[reversedFirst.Id]);
        Assert.Equal(
            attributed[second.Id],
            reversed[reversedSecond.Id]);
    }

    [Fact]
    public void AttributeBytes_AlternatingGroupsRotateIndependently()
    {
        var firstA = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "A1");
        var secondA = Candidate(
            id: 3,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "A2");
        var firstB = Candidate(
            id: 16,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            kind: "B1");
        var secondB = Candidate(
            id: 17,
            methodToken: 0x06000002,
            ilOffset: 0x0010,
            kind: "B2");
        var lookup = CandidateLookup.Create(
            [firstA, secondA, firstB, secondB]);
        long firstATotal = 0;
        long secondATotal = 0;
        long firstBTotal = 0;
        long secondBTotal = 0;

        for (int index = 0; index < 100; index++)
        {
            var a = lookup.AttributeBytes(
                [firstA, secondA],
                1);
            var b = lookup.AttributeBytes(
                [firstB, secondB],
                1);
            firstATotal += a[firstA.Id];
            secondATotal += a[secondA.Id];
            firstBTotal += b[firstB.Id];
            secondBTotal += b[secondB.Id];
        }

        Assert.Equal(50, firstATotal);
        Assert.Equal(50, secondATotal);
        Assert.Equal(50, firstBTotal);
        Assert.Equal(50, secondBTotal);
    }

    [Fact]
    public void AttributeBytes_IncludesOperationAndOperandToken()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            operation: "newobj",
            operandToken: 0x0A000001);
        var second = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            operation: "box",
            operandToken: 0x01000002);
        var lookup = CandidateLookup.Create(
            [first, second]);
        var attributed = lookup.AttributeBytes(
            [first, second],
            1);

        var reversedFirst = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            operation: "newobj",
            operandToken: 0x0A000001);
        var reversedSecond = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            operation: "box",
            operandToken: 0x01000002);
        var reversedLookup = CandidateLookup.Create(
            [reversedSecond, reversedFirst]);
        var reversed = reversedLookup.AttributeBytes(
            [reversedFirst, reversedSecond],
            1);

        Assert.Equal(
            attributed[first.Id],
            reversed[reversedFirst.Id]);
        Assert.Equal(
            attributed[second.Id],
            reversed[reversedSecond.Id]);
    }

    [Fact]
    public void AttributeBytes_DivisibleAccessRefreshesRemainderState()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "TargetA");
        var second = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "TargetB");
        var lookup = CandidateLookup.Create(
            [first, second]);
        var initial = lookup.AttributeBytes(
            [first, second],
            1);

        for (int index = 0; index < 4096; index++)
        {
            var otherFirst = Candidate(
                id: 10_000 + index * 2,
                methodToken:
                    0x06001000 + index,
                ilOffset: 0x0010,
                kind: $"Other{index}A");
            var otherSecond = Candidate(
                id: 10_001 + index * 2,
                methodToken:
                    0x06001000 + index,
                ilOffset: 0x0010,
                kind: $"Other{index}B");
            lookup.AttributeBytes(
                [otherFirst, otherSecond],
                1);
            lookup.AttributeBytes(
                [first, second],
                2);
        }

        var afterChurn = lookup.AttributeBytes(
            [first, second],
            1);

        Assert.NotEqual(
            initial[first.Id],
            afterChurn[first.Id]);
        Assert.NotEqual(
            initial[second.Id],
            afterChurn[second.Id]);
    }

    [Fact]
    public void AttributeWeight_SplitsLogicalAlternativesBeforeDuplicateRows()
    {
        var duplicate1 = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010);
        var duplicate2 = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010);
        var alternative = Candidate(
            id: 3,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "Delegate");
        var candidates = new[]
        {
            duplicate1,
            duplicate2,
            alternative,
        };
        var lookup = CandidateLookup.Create(candidates);

        var weights = lookup.AttributeWeight(
            candidates,
            1);

        Assert.Equal(0.25, weights[duplicate1.Id]);
        Assert.Equal(0.25, weights[duplicate2.Id]);
        Assert.Equal(0.5, weights[alternative.Id]);
        Assert.Equal(1, weights.Values.Sum());
    }

    [Fact]
    public void Create_LogicalDuplicatesCountAsOneSameShapeRow()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010);
        var duplicate = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010);

        CandidateLookup.Create([first, duplicate]);

        Assert.Equal(1, first.SameMethodShapeRows);
        Assert.Equal(1, duplicate.SameMethodShapeRows);
    }

    [Fact]
    public void Create_UnknownBuildTriageInputsRemainDistinct()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            libraryPath: "/tmp/triage-a.json",
            source: "triage");
        var second = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            libraryPath: "/tmp/triage-b.json",
            source: "triage");

        var lookup = CandidateLookup.Create(
            [first, second]);
        var weights = lookup.AttributeWeight(
            [first, second],
            1);

        Assert.Equal(1, first.SameMethodShapeRows);
        Assert.Equal(1, second.SameMethodShapeRows);
        Assert.Equal(0.5, weights[first.Id]);
        Assert.Equal(0.5, weights[second.Id]);
    }

    [Fact]
    public void AttributeBytes_NegativeTotal_DoesNotMutateRemainderState()
    {
        var first = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "Object");
        var second = Candidate(
            id: 2,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            kind: "Delegate");
        var candidates = new[] { first, second };
        var lookup = CandidateLookup.Create(candidates);

        Assert.Throws<InvalidDataException>(
            () => lookup.AttributeBytes(
                candidates,
                -1));
        Assert.Equal(0, lookup.RemainderStateCount);

        var attributed = lookup.AttributeBytes(
            candidates,
            1);

        Assert.Equal(1, attributed.Values.Sum());
        Assert.Equal(1, lookup.RemainderStateCount);
    }

    [Fact]
    public void WhitespaceAssemblyHasNoRuntimeCoordinate()
    {
        var candidate = Candidate(
            id: 1,
            methodToken: 0x06000001,
            ilOffset: 0x0010,
            assemblyName: " ",
            evidenceMethodToken: 0x06000002);
        var lookup = CandidateLookup.Create([candidate]);

        Assert.False(candidate.HasRuntimeCoordinate);
        Assert.Empty(
            lookup.FindByTokenOffset(
                0x06000002,
                0x0010));
    }

    [Fact]
    public void MarkAllocationHit_SplitsAmbiguousBytesWithoutInflatingTotal()
    {
        var first = Candidate(id: 1, methodToken: 0x06000006, ilOffset: 0x0010);
        var second = Candidate(id: 2, methodToken: 0x06000006, ilOffset: 0x0010, kind: "Delegate");
        var matched = new HashSet<int>();

        ProgramSupport.MarkAllocationHitForTest(first, matched, "trace", "Fixture.Allocated", 51, ilOffsetJoin: true, exactOffset: false, ambiguousIlJoin: true);
        ProgramSupport.MarkAllocationHitForTest(second, matched, "trace", "Fixture.Allocated", 50, ilOffsetJoin: true, exactOffset: false, ambiguousIlJoin: true);

        Assert.Equal(101, first.AllocationBytes + second.AllocationBytes);
        Assert.Equal(51, first.AllocationBytes);
        Assert.Equal(50, second.AllocationBytes);
        Assert.True(first.AmbiguousIlOffsetJoin);
        Assert.True(second.AmbiguousIlOffsetJoin);
    }

    [Fact]
    public void MarkAllocationHit_ZeroTickShareIsObservedNotCold()
    {
        var candidate = Candidate(
            id: 1,
            methodToken: 0x06000006,
            ilOffset: 0x0010,
            kind: "Delegate");
        var matched = new HashSet<int>();

        ProgramSupport.MarkAllocationHitForTest(
            candidate,
            matched,
            "trace",
            "Fixture.Other",
            50,
            allocationHits: 0,
            ilOffsetJoin: true,
            exactOffset: false,
            ambiguousIlJoin: true);

        Assert.True(candidate.IsObserved);
        Assert.Equal(0, candidate.AllocationHits);
        Assert.Equal("allocation-hot", candidate.Status);
    }

    static AllocationCandidate Candidate(
        int id,
        int methodToken,
        int ilOffset,
        string libraryPath = "/tmp/Fixture.dll",
        string method = "Fixture.Type.M()",
        string kind = "Object",
        string assemblyName = "Fixture",
        Guid? moduleVersionId = null,
        int? evidenceMethodToken = null,
        string source = "library",
        string? operation = null,
        int? operandToken = null)
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
            kind,
            "Fixture.Allocated",
            null,
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
            "Capture",
            operation: operation,
            operandToken: operandToken,
            evidenceMethodToken: evidenceMethodToken);
    }
}
