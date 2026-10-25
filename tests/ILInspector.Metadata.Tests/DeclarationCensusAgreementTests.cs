using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The declaration cursor's gates (<c>docs/design/finding-census.md#gates</c>,
/// adoption step 3): per Type over the Finding Diff Any fixture pair and a
/// real image against itself, Exists over two declaration cursors agrees with
/// the walk's verdicts, the identity-set comparison of the drained censuses
/// has a non-Present pair exactly when Exists is Changed, every fact kind
/// the fixtures declare appears in a drained census, and the identity images
/// read through the cursor are Indeterminate where the walk is (fault order;
/// the safety images are read through it in
/// <see cref="FindingDiffAnyMetadataSafetyTests"/>).
/// </summary>
public sealed class DeclarationCensusAgreementTests
{
    [Fact]
    public void FixturePair_ApiExistsPerTypeEqualsTheWalksApiState()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        List<Row> rows = Agree(before, after);

        Assert.True(rows.Count > 40, $"{rows.Count} Type pairs");
        Assert.Contains(rows, row => row.Walk.Api == FindingDiffAnyState.Changed);
        Assert.Contains(rows, row => row.Walk.Api == FindingDiffAnyState.Unchanged);
        Assert.Contains(rows, row => row.Walk.Body == FindingDiffAnyState.Changed && row.BodyDeclarations.Decision == FindingExistsDecision.Changed);
        Assert.Contains(rows, row => row.Walk.Body == FindingDiffAnyState.Changed && row.BodyDeclarations.Decision == FindingExistsDecision.Unchanged);
    }

    [Fact]
    public void FixturePair_IndependentSidesAgreeWithTheWalk()
    {
        // Two sides with interning tables of their own: the expanded keys
        // carry no table id, so lockstep agrees without the pair's shared table.
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        List<Row> rows = Agree(before, after, sharedTable: false);

        Assert.True(rows.Count > 40, $"{rows.Count} Type pairs");
        Assert.Contains(rows, row => row.Walk.Api == FindingDiffAnyState.Changed);
        Assert.Contains(rows, row => row.Walk.Body == FindingDiffAnyState.Changed && row.BodyDeclarations.Decision == FindingExistsDecision.Changed);
    }

    [Fact]
    public void DuplicateTypeRows_AreFailedIdentityAndIndeterminate()
    {
        // Two N.C rows spell one key on each side, so neither is a unit the
        // cursor can read: its API cursor ends Failed at Identity, and Exists
        // against any row of the other side is Indeterminate on both axes.
        byte[] oldImage = FindingDiffAnyIdentityTests.Image(
            [new("N", "C", null, FindingDiffAnyIdentityTests.ReturnOne), new("N", "C", null, FindingDiffAnyIdentityTests.ReturnOne), new("N", "Good", null, FindingDiffAnyIdentityTests.ReturnOne)]);
        byte[] newImage = FindingDiffAnyIdentityTests.Image(
            [new("N", "C", null, FindingDiffAnyIdentityTests.ReturnOne), new("N", "C", null, FindingDiffAnyIdentityTests.ReturnOne), new("N", "Good", null, FindingDiffAnyIdentityTests.ReturnOne)]);
        using var before = new PEReader(new MemoryStream(oldImage, writable: false));
        using var after = new PEReader(new MemoryStream(newImage, writable: false));
        (DeclarationCensusSide oldSide, DeclarationCensusSide newSide) = DeclarationCensusSide.CreatePair(before, after);
        using (oldSide)
        using (newSide)
        {
            List<DeclarationCensusType> duplicates = oldSide.Types.Where(type => type.FullName == "N.C").ToList();
            Assert.Equal(2, duplicates.Count);
            Assert.All(duplicates, type => Assert.True(type.IsMalformed));
            DeclarationCensusType good = Assert.Single(newSide.Types, type => type.FullName == "N.Good");
            Assert.False(good.IsMalformed);

            foreach (DeclarationCensusType duplicate in duplicates)
            {
                DeclarationCensusCursor cursor = DeclarationCensusCursor.Create(oldSide, duplicate.Type, DeclarationAxis.Api);
                Assert.False(cursor.Advance());
                Assert.True(cursor.Terminal!.IsIdentityFailure);
                foreach (DeclarationCensusType other in newSide.Types)
                {
                    Assert.Equal(FindingExistsDecision.Indeterminate, DeclarationFindings.Exists(oldSide, duplicate.Type, newSide, other.Type, DeclarationAxis.Api).Decision);
                    Assert.Equal(FindingExistsDecision.Indeterminate, DeclarationFindings.Exists(oldSide, duplicate.Type, newSide, other.Type, DeclarationAxis.Body).Decision);
                }
            }

            // The healthy row is read after the failures from the same sides.
            DeclarationCensusType oldGood = Assert.Single(oldSide.Types, type => type.FullName == "N.Good");
            Assert.Equal(FindingExistsDecision.Unchanged, DeclarationFindings.Exists(oldSide, oldGood.Type, newSide, good.Type, DeclarationAxis.Api).Decision);
            Assert.Equal(FindingExistsDecision.Unchanged, DeclarationFindings.Exists(oldSide, oldGood.Type, newSide, good.Type, DeclarationAxis.Body).Decision);
        }
    }

    [Fact]
    public void IllFormedTypeNameOnOneSide_IsIndeterminateAgainstAWellFormedRow()
    {
        // The row's name is not well-formed UTF-8 on one side, so it has no
        // identity to pair by: against any row of the other side, including the
        // well-formed spelling it was changed from, Exists is Indeterminate.
        FindingDiffAnyIdentityTests.TypeSpec[] types =
            [new("N", "QQX", null, FindingDiffAnyIdentityTests.ReturnOne), new("N", "Good", null, FindingDiffAnyIdentityTests.ReturnOne)];
        byte[] oldImage = FindingDiffAnyIdentityTests.Patch(FindingDiffAnyIdentityTests.Image(types), "QQX"u8, [0xFF, 0xFE, (byte)'X']);
        byte[] newImage = FindingDiffAnyIdentityTests.Image(types);
        using var before = new PEReader(new MemoryStream(oldImage, writable: false));
        using var after = new PEReader(new MemoryStream(newImage, writable: false));
        FindingDiffAnyResult walk = FindingDiffAny.Compare(before, after, cancellationToken: TestContext.Current.CancellationToken);
        (DeclarationCensusSide oldSide, DeclarationCensusSide newSide) = DeclarationCensusSide.CreatePair(before, after);
        using (oldSide)
        using (newSide)
        {
            DeclarationCensusType malformed = Assert.Single(oldSide.Types, type => type.IsMalformed);
            Assert.Equal(FindingDiffAnyState.Indeterminate, Assert.Single(walk.Types, type => type.Identifier == malformed.Identifier).Api);
            Assert.All(newSide.Types, type => Assert.False(type.IsMalformed));
            foreach (DeclarationCensusType other in newSide.Types)
            {
                Assert.Equal(FindingExistsDecision.Indeterminate, DeclarationFindings.Exists(oldSide, malformed.Type, newSide, other.Type, DeclarationAxis.Api).Decision);
                Assert.Equal(FindingExistsDecision.Indeterminate, DeclarationFindings.Exists(oldSide, malformed.Type, newSide, other.Type, DeclarationAxis.Body).Decision);
            }

            DeclarationCensusType oldGood = Assert.Single(oldSide.Types, type => type.FullName == "N.Good");
            DeclarationCensusType newGood = Assert.Single(newSide.Types, type => type.FullName == "N.Good");
            Assert.Equal(FindingExistsDecision.Unchanged, DeclarationFindings.Exists(oldSide, oldGood.Type, newSide, newGood.Type, DeclarationAxis.Api).Decision);
            Assert.Equal(FindingExistsDecision.Unchanged, DeclarationFindings.Exists(oldSide, oldGood.Type, newSide, newGood.Type, DeclarationAxis.Body).Decision);
        }
    }

    [Fact]
    public void FixturePair_IdentitySetComparisonDiffersExactlyWhenExistsIsChanged()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        (DeclarationCensusSide oldSide, DeclarationCensusSide newSide) = DeclarationCensusSide.CreatePair(before, after);
        using (oldSide)
        using (newSide)
        {
            Dictionary<string, DeclarationCensusType> newTypes = newSide.Types.Where(t => !t.IsMalformed).ToDictionary(t => t.Identifier, StringComparer.Ordinal);
            var kinds = new HashSet<DeclarationFactKind>();
            int compared = 0;
            foreach (DeclarationCensusType oldType in oldSide.Types.Where(t => !t.IsMalformed))
            {
                if (!newTypes.TryGetValue(oldType.Identifier, out DeclarationCensusType? newType))
                    continue;
                foreach (DeclarationAxis axis in new[] { DeclarationAxis.Api, DeclarationAxis.Body })
                {
                    bool? ownerVisible = axis == DeclarationAxis.Body
                        ? oldSide.IsVisible(oldType.Type) || newSide.IsVisible(newType.Type)
                        : null;
                    var subject = new FindingSubject(oldType.Identifier, oldType.FullName);
                    FindingInspection<DeclarationFact> oldInspection = DeclarationFindings.Inspect(oldSide, oldType.Type, axis, subject, ownerVisible);
                    FindingInspection<DeclarationFact> newInspection = DeclarationFindings.Inspect(newSide, newType.Type, axis, subject, ownerVisible);
                    FindingExistsResult exists = DeclarationFindings.Exists(oldSide, oldType.Type, newSide, newType.Type, axis);
                    var complete = Assert.IsType<FindingComparison<DeclarationFact>.Complete>(
                        DeclarationFindings.Compare(oldInspection, newInspection).Value);

                    bool anyDifference = complete.Pairs.Any(pair => pair.Kind != PairKind.Present);
                    Assert.True(
                        anyDifference == (exists.Decision == FindingExistsDecision.Changed),
                        $"{oldType.FullName} {axis}: comparison {(anyDifference ? "differs" : "is all Present")}, Exists {exists.Decision}");
                    Assert.NotEqual(FindingExistsDecision.Indeterminate, exists.Decision);

                    foreach (Finding<DeclarationFact> atom in ((FindingInspection<DeclarationFact>.Complete)oldInspection.Value!).Findings)
                    {
                        kinds.Add(atom.Payload.Kind);
                        Assert.Equal(axis, atom.Payload.Axis);
                        Assert.False(string.IsNullOrEmpty(atom.Payload.Text), "a sealed fact is spelled");
                        Assert.Equal(atom.Payload.IdentityKey, atom.Key.IdentityKey);
                    }
                    compared++;
                }
            }
            Assert.True(compared > 80, $"{compared} censuses compared");
            // The kinds the fixture pair declares; Import, Layout, and
            // MethodImplementation need rows the fixture does not author.
            foreach (DeclarationFactKind kind in new[]
            {
                DeclarationFactKind.TypeVisibility, DeclarationFactKind.TypeRow, DeclarationFactKind.Interface,
                DeclarationFactKind.NullableContext, DeclarationFactKind.FieldRow, DeclarationFactKind.MethodRow,
                DeclarationFactKind.Parameter, DeclarationFactKind.PropertyRow, DeclarationFactKind.GenericParameter,
                DeclarationFactKind.Constraint, DeclarationFactKind.Attribute, DeclarationFactKind.BeforeFieldInit,
            })
            {
                Assert.Contains(kind, kinds);
            }
        }
    }

    [Fact]
    public void RealImageAgainstItself_EveryTypeIsUnchangedOnBothAxes()
    {
        string path = typeof(FindingDiffAny).Assembly.Location;
        using var before = new PEReader(File.OpenRead(path));
        using var after = new PEReader(File.OpenRead(path));
        List<Row> rows = Agree(before, after);

        Assert.True(rows.Count > 300, $"{rows.Count} Type pairs");
        Assert.All(rows, row =>
        {
            Assert.Equal(FindingExistsDecision.Unchanged, row.Api.Decision);
            Assert.Equal(FindingExistsDecision.Unchanged, row.BodyDeclarations.Decision);
            Assert.Equal(row.Api.OldAtoms, row.Api.NewAtoms);
            Assert.Equal(row.BodyDeclarations.OldAtoms, row.BodyDeclarations.NewAtoms);
        });
    }

    [Fact]
    public void GeneratedNestedTypeAndModulePseudoType_AreAbsentNotFailed()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var image = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        using DeclarationCensusSide side = DeclarationCensusSide.Create(image);
        MetadataReader reader = side.Reader;
        HashSet<TypeDefinitionHandle> units = side.Types.Select(t => t.Type).ToHashSet();
        TypeDefinitionHandle generated = reader.TypeDefinitions
            .First(handle => !units.Contains(handle) && reader.GetString(reader.GetTypeDefinition(handle).Name).StartsWith('<')
                && !reader.GetTypeDefinition(handle).GetDeclaringType().IsNil);

        DeclarationCensusCursor cursor = DeclarationCensusCursor.Create(side, generated, DeclarationAxis.Body);
        Assert.False(cursor.Advance());
        Assert.Equal(FindingCursorTerminalKind.Absent, cursor.Terminal!.Kind);
        Assert.Equal(FindingInspectionAbsenceKind.NoApplicableInput, cursor.Terminal.AbsenceKind);

        TypeDefinitionHandle module = reader.TypeDefinitions.First();
        Assert.DoesNotContain(module, units);
        DeclarationCensusCursor moduleCursor = DeclarationCensusCursor.Create(side, module, DeclarationAxis.Api);
        Assert.False(moduleCursor.Advance());
        Assert.Equal(FindingCursorTerminalKind.Absent, moduleCursor.Terminal!.Kind);
    }

    sealed record Row(FindingDiffAnyTypeState Walk, FindingExistsResult Api, FindingExistsResult BodyDeclarations);

    // Every Type the walk pairs, decided by the walk and by lockstep over the two sides, agreeing.
    static List<Row> Agree(PEReader before, PEReader after, bool sharedTable = true)
    {
        FindingDiffAnyResult walk = FindingDiffAny.Compare(before, after);
        (DeclarationCensusSide oldSide, DeclarationCensusSide newSide) = sharedTable
            ? DeclarationCensusSide.CreatePair(before, after)
            : (DeclarationCensusSide.Create(before), DeclarationCensusSide.Create(after));
        HashSet<string> uniqueNames = walk.Types.GroupBy(type => type.FullName, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var rows = new List<Row>();
        using (oldSide)
        using (newSide)
        {
            Dictionary<string, DeclarationCensusType> oldTypes = oldSide.Types.ToDictionary(t => t.Identifier, StringComparer.Ordinal);
            Dictionary<string, DeclarationCensusType> newTypes = newSide.Types.ToDictionary(t => t.Identifier, StringComparer.Ordinal);
            foreach (FindingDiffAnyTypeState state in walk.Types)
            {
                if (!oldTypes.TryGetValue(state.Identifier, out DeclarationCensusType? oldType)
                    || !newTypes.TryGetValue(state.Identifier, out DeclarationCensusType? newType))
                {
                    continue;
                }
                FindingExistsResult api = DeclarationFindings.Exists(oldSide, oldType.Type, newSide, newType.Type, DeclarationAxis.Api);
                FindingExistsResult body = DeclarationFindings.Exists(oldSide, oldType.Type, newSide, newType.Type, DeclarationAxis.Body);
                Assert.True(
                    Decision(state.Api) == api.Decision,
                    $"{state.FullName} API: walk {state.Api}, lockstep {api.Decision} after {api.OldAtoms}/{api.NewAtoms} facts");
                // The walk's Body axis is the declaration facts and the IL: a
                // Body Unchanged has equal declaration facts, and a Body Changed
                // has unequal declaration facts exactly when the walk's first
                // Body difference is a declaration fact rather than a method's IL.
                if (state.Body == FindingDiffAnyState.Unchanged)
                    Assert.Equal(FindingExistsDecision.Unchanged, body.Decision);
                if (state.Body == FindingDiffAnyState.Changed && uniqueNames.Contains(state.FullName))
                {
                    (_, string? explained) = FindingDiffAny.Explain(before, after, state.FullName);
                    Assert.NotNull(explained);
                    bool declarationsDiffer = !explained.StartsWith("IL ", StringComparison.Ordinal);
                    Assert.True(
                        declarationsDiffer == (body.Decision == FindingExistsDecision.Changed),
                        $"{state.FullName} Body: walk's first difference {explained.ReplaceLineEndings(" ")}, lockstep {body.Decision}");
                }
                if (body.Decision == FindingExistsDecision.Changed)
                    Assert.Equal(FindingDiffAnyState.Changed, state.Body);
                if (state.Body == FindingDiffAnyState.Indeterminate && state.Api == FindingDiffAnyState.Indeterminate)
                    Assert.Equal(FindingExistsDecision.Indeterminate, body.Decision);
                rows.Add(new Row(state, api, body));
            }
        }
        return rows;
    }

    static FindingExistsDecision Decision(FindingDiffAnyState state) => state switch
    {
        FindingDiffAnyState.Unchanged => FindingExistsDecision.Unchanged,
        FindingDiffAnyState.Changed => FindingExistsDecision.Changed,
        FindingDiffAnyState.Indeterminate => FindingExistsDecision.Indeterminate,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A paired Type's API state is decided."),
    };
}
