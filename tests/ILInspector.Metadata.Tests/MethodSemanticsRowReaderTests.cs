using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MethodSemanticsRowReaderTests
{
    static readonly Lazy<IReadOnlyDictionary<(bool WideMethod, bool WideAssociation), byte[]>>
        WidthFixtures = new(BuildWidthFixtures);

    [Fact]
    public void ReadRejectsNullArguments()
    {
        using var peReader = Open(BuildImage());

        Assert.Throws<ArgumentNullException>(
            () => MethodSemanticsRowReader.Read(
                null!,
                new MethodSemanticsReadBudget(1)));
        Assert.Throws<ArgumentNullException>(
            () => MethodSemanticsRowReader.Read(peReader, null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MethodSemanticsReadBudget(-1));
    }

    [Fact]
    public void Mdp016_EmptyTableReturnsCompleteEmptyResult()
    {
        using var peReader = Open(BuildImage());

        var result = Assert.IsType<MethodSemanticsReadResult.Success>(
            MethodSemanticsRowReader.Read(
                peReader,
                new MethodSemanticsReadBudget(0)));

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.RowsVisited);
        Assert.True(result.AssociationsAreNondecreasing);
    }

    [Fact]
    public void Mdp016_RowsPreservePhysicalOrderRawBitsAndDuplicates()
    {
        RawSemanticsRow[] expected =
        [
            new(1, MethodSemanticsAssociationKind.Property, 1, 0),
            new(2, MethodSemanticsAssociationKind.Property, 1, 0x8040),
            new(3, MethodSemanticsAssociationKind.Property, 1, 0x03),
            new(4, MethodSemanticsAssociationKind.Property, 1, 0x02),
            new(5, MethodSemanticsAssociationKind.Property, 1, 0x02),
            new(6, MethodSemanticsAssociationKind.Property, 1, 0x04),
            new(7, MethodSemanticsAssociationKind.Property, 1, 0x04),
        ];
        using var peReader = Open(
            BuildImage(
                methodCount: expected.Length,
                propertyCount: 1,
                rows: expected));

        var result = Assert.IsType<MethodSemanticsReadResult.Success>(
            MethodSemanticsRowReader.Read(
                peReader,
                new MethodSemanticsReadBudget(expected.Length)));

        Assert.True(result.AssociationsAreNondecreasing);
        Assert.Equal(
            expected.Select(
                (row, index) => new MethodSemanticsRow(
                    index + 1,
                    row.RawSemantics,
                    MetadataTokens.MethodDefinitionHandle(row.MethodRow),
                    row.AssociationKind,
                    row.AssociationRow)),
            result.Rows);
    }

    [Fact]
    public void Mdp016_ConventionalRowsAgreeWithSrmAccessors()
    {
        RawSemanticsRow[] rows =
        [
            new(1, MethodSemanticsAssociationKind.Event, 1, 0x08),
            new(2, MethodSemanticsAssociationKind.Event, 1, 0x10),
            new(3, MethodSemanticsAssociationKind.Event, 1, 0x20),
            new(4, MethodSemanticsAssociationKind.Event, 1, 0x04),
            new(5, MethodSemanticsAssociationKind.Property, 1, 0x01),
            new(6, MethodSemanticsAssociationKind.Property, 1, 0x02),
            new(7, MethodSemanticsAssociationKind.Property, 1, 0x04),
        ];
        using var peReader = Open(
            BuildImage(
                methodCount: rows.Length,
                propertyCount: 1,
                eventCount: 1,
                rows: rows));
        MetadataReader reader = peReader.GetMetadataReader();

        var result = Assert.IsType<MethodSemanticsReadResult.Success>(
            MethodSemanticsRowReader.Read(
                peReader,
                new MethodSemanticsReadBudget(rows.Length)));
        PropertyAccessors property = reader
            .GetPropertyDefinition(
                MetadataTokens.PropertyDefinitionHandle(1))
            .GetAccessors();
        EventAccessors @event = reader
            .GetEventDefinition(
                MetadataTokens.EventDefinitionHandle(1))
            .GetAccessors();

        Assert.Equal(property.Setter, result.Rows[4].Method);
        Assert.Equal(property.Getter, result.Rows[5].Method);
        Assert.Equal(property.Others.Single(), result.Rows[6].Method);
        Assert.Equal(@event.Adder, result.Rows[0].Method);
        Assert.Equal(@event.Remover, result.Rows[1].Method);
        Assert.Equal(@event.Raiser, result.Rows[2].Method);
        Assert.Equal(@event.Others.Single(), result.Rows[3].Method);
    }

    [Fact]
    public void Mdp016_CompilerProducedAssemblyPreservesKnownGetter()
    {
        using var stream = File.OpenRead(
            typeof(MethodSemanticsRowReaderTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle declaringType = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name)
                == nameof(CompilerProducedFixture));
        PropertyDefinitionHandle property = reader
            .GetTypeDefinition(declaringType)
            .GetProperties()
            .Single(
                handle => reader.GetString(
                    reader.GetPropertyDefinition(handle).Name)
                    == nameof(CompilerProducedFixture.Value));
        int propertyRow = MetadataTokens.GetRowNumber(property);

        var result = Assert.IsType<MethodSemanticsReadResult.Success>(
            MethodSemanticsRowReader.Read(
                peReader,
                MethodSemanticsReadBudget.Default));
        MethodSemanticsRow getter = Assert.Single(
            result.Rows,
            row =>
                row.AssociationKind
                    == MethodSemanticsAssociationKind.Property
                && row.AssociationRowNumber == propertyRow);

        Assert.Equal((ushort)MethodSemanticsAttributes.Getter, getter.RawSemantics);
        Assert.Equal(
            $"get_{nameof(CompilerProducedFixture.Value)}",
            reader.GetString(
                reader.GetMethodDefinition(getter.Method).Name));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Mdp016_DecodesEveryIndexWidthCombination(
        bool wideMethod,
        bool wideAssociation)
    {
        byte[] image = WidthFixtures.Value[(wideMethod, wideAssociation)];
        using var peReader = Open(image);
        MetadataReader reader = peReader.GetMetadataReader();
        int expectedMethodRow = wideMethod ? 65_536 : 2;
        int expectedPropertyRow = wideAssociation ? 32_768 : 1;
        int expectedRowSize =
            sizeof(ushort)
            + (wideMethod ? sizeof(uint) : sizeof(ushort))
            + (wideAssociation ? sizeof(uint) : sizeof(ushort));

        var result = Assert.IsType<MethodSemanticsReadResult.Success>(
            MethodSemanticsRowReader.Read(
                peReader,
                new MethodSemanticsReadBudget(1)));
        MethodSemanticsRow row = Assert.Single(result.Rows);

        Assert.Equal(
            expectedRowSize,
            reader.GetTableRowSize(TableIndex.MethodSemantics));
        Assert.Equal(expectedMethodRow, MetadataTokens.GetRowNumber(row.Method));
        Assert.Equal(expectedPropertyRow, row.AssociationRowNumber);
        Assert.Equal(MethodSemanticsAssociationKind.Property, row.AssociationKind);
        Assert.Equal((ushort)0x02, row.RawSemantics);
    }

    [Theory]
    [InlineData(0, MethodSemanticsMalformedReason.NilMethod)]
    [InlineData(2, MethodSemanticsMalformedReason.MethodOutOfRange)]
    public void Mdp016_InvalidMethodRowsAreTypedMalformed(
        uint methodRow,
        MethodSemanticsMalformedReason expected)
    {
        byte[] image = BuildImage(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x02),
            ]);
        PatchMethodRow(image, methodRow);
        using var peReader = Open(image);

        AssertMalformed(peReader, expected, rowNumber: 1);
    }

    [Theory]
    [InlineData(0, MethodSemanticsMalformedReason.NilAssociation)]
    [InlineData(2, MethodSemanticsMalformedReason.AssociationOutOfRange)]
    [InlineData(5, MethodSemanticsMalformedReason.AssociationOutOfRange)]
    public void Mdp016_InvalidAssociationRowsAreTypedMalformed(
        uint association,
        MethodSemanticsMalformedReason expected)
    {
        byte[] image = BuildImage(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x02),
            ]);
        PatchAssociation(image, association);
        using var peReader = Open(image);

        AssertMalformed(peReader, expected, rowNumber: 1);
    }

    [Fact]
    public void Mdp016_PhysicalAssociationOrderIsObservedNotRejected()
    {
        byte[] image = BuildImage(
            methodCount: 2,
            propertyCount: 2,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x02),
                new(
                    2,
                    MethodSemanticsAssociationKind.Property,
                    2,
                    0x02),
            ]);
        PatchAssociation(image, encodedAssociation: 5, rowIndex: 0);
        PatchAssociation(image, encodedAssociation: 3, rowIndex: 1);
        using var peReader = Open(image);

        var result = Assert.IsType<MethodSemanticsReadResult.Success>(
            MethodSemanticsRowReader.Read(
                peReader,
                new MethodSemanticsReadBudget(2)));

        Assert.Equal([2, 1], result.Rows.Select(r => r.AssociationRowNumber));
        Assert.False(result.AssociationsAreNondecreasing);
    }

    [Fact]
    public void Mdp016_UnsortedBitClearIsTypedReaderRejection()
    {
        byte[] image = BuildImage(
            methodCount: 2,
            propertyCount: 2,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x02),
                new(
                    2,
                    MethodSemanticsAssociationKind.Property,
                    2,
                    0x02),
            ]);
        PatchAssociation(image, encodedAssociation: 5, rowIndex: 0);
        PatchAssociation(image, encodedAssociation: 3, rowIndex: 1);
        ClearMethodSemanticsSortedBit(image);
        using var peReader = Open(image);

        Assert.Throws<BadImageFormatException>(
            () => peReader.GetMetadataReader());
        AssertMalformed(
            peReader,
            MethodSemanticsMalformedReason.MetadataReaderRejected,
            rowsVisited: 0);
    }

    [Fact]
    public void Mdp016_MetadataStreamCountOverflowIsTypedReaderRejection()
    {
        byte[] image = BuildImage();
        using (var intact = Open(image))
        {
            int metadataStart =
                intact.PEHeaders.MetadataStartOffset;
            int versionLength =
                BinaryPrimitives.ReadInt32LittleEndian(
                    image.AsSpan(
                        metadataStart + 12,
                        sizeof(int)));
            int streamCountOffset =
                metadataStart
                + 16
                + versionLength
                + sizeof(ushort);
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(
                    streamCountOffset,
                    sizeof(ushort)),
                ushort.MaxValue);
        }
        using var peReader = Open(image);
        var budget = MethodSemanticsReadBudget.Default;

        Assert.IsType<MetadataImageFormatResult.SupportedEcma335>(
            MetadataImageFormatClassifier.Classify(peReader));
        Assert.Throws<OverflowException>(
            () => peReader.GetMetadataReader());
        var result =
            Assert.IsType<MethodSemanticsReadResult.MalformedInput>(
                MethodSemanticsRowReader.Read(peReader, budget));
        Assert.Equal(
            MethodSemanticsMalformedReason.MetadataReaderRejected,
            result.Reason);
        Assert.Equal(0, result.RowsVisited);
        Assert.Null(result.RowNumber);
        Assert.Null(result.MetadataRootReason);
        Assert.Equal(0, budget.RetainedAssociations);
    }

    [Fact]
    public void Mdp016_BudgetStopsBeforeOversizedRetention()
    {
        const int RowCount = 10_000;
        const int Budget = 8;
        RawSemanticsRow[] rows = Enumerable
            .Range(1, RowCount)
            .Select(
                method => new RawSemanticsRow(
                    method,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x04))
            .ToArray();
        using var peReader = Open(
            BuildImage(
                methodCount: RowCount,
                propertyCount: 1,
                rows: rows));
        _ = peReader.GetMetadataReader();
        var budget = new MethodSemanticsReadBudget(Budget);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = Assert.IsType<
            MethodSemanticsReadResult.RetainedAssociationBudgetExceeded>(
            MethodSemanticsRowReader.Read(peReader, budget));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Budget + 1, result.RowsVisited);
        Assert.Equal(Budget, budget.RetainedAssociations);
        Assert.InRange(allocated, 0, 128 * 1024);
    }

    [Fact]
    public void Mdp016_ZeroBudgetRejectsBeforeCollectionMaterialization()
    {
        using var emptyReader = Open(BuildImage());
        using var peReader = Open(
            BuildImage(
                methodCount: 1,
                propertyCount: 1,
                rows:
                [
                    new(
                        1,
                        MethodSemanticsAssociationKind.Property,
                        1,
                        0x02),
                ]));
        var budget = new MethodSemanticsReadBudget(0);
        _ = emptyReader.GetMetadataReader();
        _ = peReader.GetMetadataReader();
        _ = MethodSemanticsRowReader.Read(
            emptyReader,
            MethodSemanticsReadBudget.Unbounded);
        _ = MethodSemanticsRowReader.Read(peReader, budget);

        long emptyAllocation = MeasureReadAllocation(
            emptyReader,
            MethodSemanticsReadBudget.Unbounded);
        long rejectedAllocation = MeasureReadAllocation(
            peReader,
            budget);

        Assert.Equal(0, budget.RetainedAssociations);
        Assert.InRange(
            Math.Abs(rejectedAllocation - emptyAllocation),
            0,
            64 * 1024);
    }

    [Fact]
    public void Mdp016_DefaultBudgetPinsMeasuredCorpusMargin()
    {
        const int PinnedCorpusMaximum = 6_592;
        MethodSemanticsReadBudget budget =
            MethodSemanticsReadBudget.Default;

        Assert.Equal(
            64 * 1024,
            MethodSemanticsReadBudget
                .DefaultMaximumRetainedAssociations);
        Assert.Equal(
            MethodSemanticsReadBudget
                .DefaultMaximumRetainedAssociations,
            budget.MaximumRetainedAssociations);
        Assert.True(
            budget.MaximumRetainedAssociations
                > PinnedCorpusMaximum * 9);
    }

    [Fact]
    public void Mdp016_BudgetIsSharedAcrossReaderCalls()
    {
        byte[] image = BuildImage(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x02),
            ]);
        using var firstReader = Open(image);
        using var secondReader = Open(image);
        var budget = new MethodSemanticsReadBudget(1);

        Assert.IsType<MethodSemanticsReadResult.Success>(
            MethodSemanticsRowReader.Read(firstReader, budget));
        var rejected = Assert.IsType<
            MethodSemanticsReadResult.RetainedAssociationBudgetExceeded>(
            MethodSemanticsRowReader.Read(secondReader, budget));

        Assert.Equal(1, rejected.RowsVisited);
        Assert.Equal(1, budget.RetainedAssociations);
    }

    [Fact]
    public void Mdp016_UnsupportedFormatWinsBeforeMetadataReaderWork()
    {
        byte[] image = BuildImage(
            methodCount: 2,
            propertyCount: 2,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x02),
                new(
                    2,
                    MethodSemanticsAssociationKind.Property,
                    2,
                    0x02),
            ],
            metadataVersion: "XindowsRuntime 1.4");
        PatchAssociation(image, encodedAssociation: 5, rowIndex: 0);
        PatchAssociation(image, encodedAssociation: 3, rowIndex: 1);
        ClearMethodSemanticsSortedBit(image);
        using (var intact = Open(image))
        {
            image[
                intact.PEHeaders.MetadataStartOffset
                    + MetadataImageFormatClassifier.FixedPrefixLength] =
                (byte)'W';
        }
        using var peReader = Open(image);

        Assert.Throws<BadImageFormatException>(
            () => peReader.GetMetadataReader());
        Assert.IsType<
            MethodSemanticsReadResult.UnsupportedWindowsMetadata>(
            MethodSemanticsRowReader.Read(
                peReader,
                MethodSemanticsReadBudget.Unbounded));
    }

    [Fact]
    public void Mdp016_LazyMetadataIoFailureRemainsAcquisitionFailure()
    {
        byte[] image = BuildImage(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                new(
                    1,
                    MethodSemanticsAssociationKind.Property,
                    1,
                    0x02),
            ]);
        using var stream = new ArmableReadFailureStream(image);
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen);
        Assert.True(peReader.HasMetadata);
        stream.Arm();

        Assert.Throws<IOException>(
            () => MethodSemanticsRowReader.Read(
                peReader,
                MethodSemanticsReadBudget.Unbounded));
    }

    [Fact]
    public async Task Mdp016_IlasmRowsEqualIldasmOrderedMultiset()
    {
        bool toolsAvailable =
            CanRunTool("ilasm", ["-?"])
            && CanRunTool("ildasm", ["-?"]);
        const string ToolMessage =
            "ildasm/ilasm not found - install them with "
            + "`source eng/activate-iltools.sh`";
        Assert.SkipUnless(toolsAvailable, ToolMessage);

        string temporaryDirectory = Directory
            .CreateTempSubdirectory("method-semantics-oracle-")
            .FullName;
        try
        {
            string sourcePath = Path.Combine(
                temporaryDirectory,
                "probe.il");
            string assemblyPath = Path.Combine(
                temporaryDirectory,
                "probe.dll");
            string outputPath = Path.Combine(
                temporaryDirectory,
                "probe.out.il");
            File.WriteAllText(sourcePath, IlasmOracleSource);
            await RunToolAsync(
                "ilasm",
                [
                    sourcePath,
                    "-dll",
                    $"-output={assemblyPath}",
                    "-quiet",
                ]);
            await RunToolAsync(
                "ildasm",
                [
                    assemblyPath,
                    $"-output={outputPath}",
                    "-utf8",
                ]);

            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            var result =
                Assert.IsType<MethodSemanticsReadResult.Success>(
                    MethodSemanticsRowReader.Read(
                        peReader,
                        MethodSemanticsReadBudget.Default));

            Assert.Equal(
                ReadIldasmAssociations(
                    File.ReadAllLines(outputPath)),
                ReadPrimitiveAssociations(reader, result.Rows));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void Mdp016_NoMetadataAndMalformedRootRemainDistinct()
    {
        byte[] noMetadata = BuildImage();
        using (var intact = Open(noMetadata))
        {
            PEHeader header = intact.PEHeaders.PEHeader!;
            int directoryBase =
                intact.PEHeaders.PEHeaderStartOffset
                + (header.Magic == PEMagic.PE32Plus ? 112 : 96);
            noMetadata.AsSpan(directoryBase + (14 * 8), 8).Clear();
        }
        using var noMetadataReader = Open(noMetadata);

        Assert.IsType<MethodSemanticsReadResult.NoMetadata>(
            MethodSemanticsRowReader.Read(
                noMetadataReader,
                MethodSemanticsReadBudget.Unbounded));

        byte[] malformed = BuildImage();
        using (var intact = Open(malformed))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                malformed.AsSpan(
                    intact.PEHeaders.MetadataStartOffset,
                    sizeof(uint)),
                0xDEADBEEF);
        }
        using var malformedReader = Open(malformed);

        var result =
            Assert.IsType<MethodSemanticsReadResult.MalformedInput>(
                MethodSemanticsRowReader.Read(
                    malformedReader,
                    MethodSemanticsReadBudget.Unbounded));
        Assert.Equal(
            MethodSemanticsMalformedReason.MetadataRootMalformed,
            result.Reason);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            result.MetadataRootReason);
    }

    static IReadOnlyDictionary<(bool WideMethod, bool WideAssociation), byte[]>
        BuildWidthFixtures()
    {
        var fixtures =
            new Dictionary<(bool WideMethod, bool WideAssociation), byte[]>();
        foreach (bool wideMethod in new[] { false, true })
        {
            foreach (bool wideAssociation in new[] { false, true })
            {
                int methodCount = wideMethod ? 65_536 : 2;
                int propertyCount = wideAssociation ? 32_768 : 1;
                fixtures.Add(
                    (wideMethod, wideAssociation),
                    BuildImage(
                        methodCount,
                        propertyCount,
                        rows:
                        [
                            new(
                                methodCount,
                                MethodSemanticsAssociationKind.Property,
                                propertyCount,
                                0x02),
                        ]));
            }
        }

        return fixtures;
    }

    static long MeasureReadAllocation(
        PEReader reader,
        MethodSemanticsReadBudget budget)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        MethodSemanticsReadResult? last = null;
        for (int i = 0; i < 1_000; i++)
            last = MethodSemanticsRowReader.Read(reader, budget);
        GC.KeepAlive(last);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    static void AssertMalformed(
        PEReader peReader,
        MethodSemanticsMalformedReason expected,
        int rowsVisited = 1,
        int? rowNumber = null)
    {
        var result =
            Assert.IsType<MethodSemanticsReadResult.MalformedInput>(
                MethodSemanticsRowReader.Read(
                    peReader,
                    MethodSemanticsReadBudget.Unbounded));
        Assert.Equal(expected, result.Reason);
        Assert.Equal(rowsVisited, result.RowsVisited);
        Assert.Equal(rowNumber, result.RowNumber);
    }

    static void PatchMethodRow(
        byte[] image,
        uint methodRow,
        int rowIndex = 0)
    {
        TableLayout layout = GetLayout(image);
        WriteIndex(
            image.AsSpan(
                layout.Start
                    + (rowIndex * layout.RowSize)
                    + sizeof(ushort),
                layout.MethodIndexSize),
            methodRow);
    }

    static void PatchAssociation(
        byte[] image,
        uint encodedAssociation,
        int rowIndex = 0)
    {
        TableLayout layout = GetLayout(image);
        WriteIndex(
            image.AsSpan(
                layout.Start
                    + (rowIndex * layout.RowSize)
                    + sizeof(ushort)
                    + layout.MethodIndexSize,
                layout.AssociationIndexSize),
            encodedAssociation);
    }

    static TableLayout GetLayout(byte[] image)
    {
        using var peReader = Open(image);
        MetadataReader reader = peReader.GetMetadataReader();
        int methodCount = reader.GetTableRowCount(TableIndex.MethodDef);
        int eventCount = reader.GetTableRowCount(TableIndex.Event);
        int propertyCount = reader.GetTableRowCount(TableIndex.Property);
        int methodIndexSize =
            methodCount < ushort.MaxValue + 1 ? 2 : 4;
        int associationIndexSize =
            Math.Max(eventCount, propertyCount) < (1 << 15) ? 2 : 4;
        return new TableLayout(
            peReader.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.MethodSemantics),
            reader.GetTableRowSize(TableIndex.MethodSemantics),
            methodIndexSize,
            associationIndexSize);
    }

    static void WriteIndex(Span<byte> destination, uint value)
    {
        if (destination.Length == sizeof(ushort))
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                destination,
                checked((ushort)value));
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                destination,
                value);
        }
    }

    static void ClearMethodSemanticsSortedBit(byte[] image)
    {
        int tablesStreamStart = TablesStreamStart(image);
        ulong sorted = BinaryPrimitives.ReadUInt64LittleEndian(
            image.AsSpan(tablesStreamStart + 16, sizeof(ulong)));
        sorted &= ~(1UL << (int)TableIndex.MethodSemantics);
        BinaryPrimitives.WriteUInt64LittleEndian(
            image.AsSpan(tablesStreamStart + 16, sizeof(ulong)),
            sorted);
    }

    static int TablesStreamStart(byte[] image)
    {
        using var peReader = Open(image);
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        int position = metadataStart + 16 + versionLength;
        position += sizeof(ushort);
        ushort streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(position, sizeof(ushort)));
        position += sizeof(ushort);

        for (int i = 0; i < streamCount; i++)
        {
            int streamOffset = BinaryPrimitives.ReadInt32LittleEndian(
                image.AsSpan(position, sizeof(int)));
            position += sizeof(int) * 2;
            int nameStart = position;
            while (image[position] != 0)
                position++;
            string name = System.Text.Encoding.ASCII.GetString(
                image,
                nameStart,
                position - nameStart);
            position = (position + 4) & ~3;
            if (name is "#~" or "#-")
                return metadataStart + streamOffset;
        }

        throw new InvalidOperationException(
            "The fixture has no metadata table stream.");
    }

    static PEReader Open(byte[] image)
        => new(ImmutableArray.Create(image));

    static byte[] BuildImage(
        int methodCount = 0,
        int propertyCount = 0,
        int eventCount = 0,
        IReadOnlyList<RawSemanticsRow>? rows = null,
        string metadataVersion = "v4.0.30319")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle owner = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            default,
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType => returnType.Void(),
                _ => { });
        BlobHandle methodSignatureHandle =
            metadata.GetOrAddBlob(methodSignature);
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{i + 1}"),
                methodSignatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }

        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature)
            .PropertySignature(isInstanceProperty: true)
            .Parameters(
                0,
                returnType => returnType.Type().Int32(),
                _ => { });
        BlobHandle propertySignatureHandle =
            metadata.GetOrAddBlob(propertySignature);
        PropertyDefinitionHandle firstProperty = default;
        for (int i = 0; i < propertyCount; i++)
        {
            PropertyDefinitionHandle property = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString($"P{i + 1}"),
                propertySignatureHandle);
            if (i == 0)
                firstProperty = property;
        }
        if (!firstProperty.IsNil)
            metadata.AddPropertyMap(owner, firstProperty);

        AssemblyReferenceHandle coreLibrary = metadata.AddAssemblyReference(
            metadata.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle eventType = metadata.AddTypeReference(
            coreLibrary,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("EventHandler"));
        EventDefinitionHandle firstEvent = default;
        for (int i = 0; i < eventCount; i++)
        {
            EventDefinitionHandle @event = metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString($"E{i + 1}"),
                eventType);
            if (i == 0)
                firstEvent = @event;
        }
        if (!firstEvent.IsNil)
            metadata.AddEventMap(owner, firstEvent);

        if (rows is not null)
        {
            foreach (RawSemanticsRow row in rows)
            {
                EntityHandle association =
                    row.AssociationKind
                        == MethodSemanticsAssociationKind.Event
                        ? MetadataTokens.EventDefinitionHandle(
                            row.AssociationRow)
                        : MetadataTokens.PropertyDefinitionHandle(
                            row.AssociationRow);
                metadata.AddMethodSemantics(
                    association,
                    (MethodSemanticsAttributes)row.RawSemantics,
                    MetadataTokens.MethodDefinitionHandle(
                        row.MethodRow));
            }
        }

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                metadataVersion,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    readonly record struct RawSemanticsRow(
        int MethodRow,
        MethodSemanticsAssociationKind AssociationKind,
        int AssociationRow,
        ushort RawSemantics);

    readonly record struct TableLayout(
        int Start,
        int RowSize,
        int MethodIndexSize,
        int AssociationIndexSize);

    static string[] ReadPrimitiveAssociations(
        MetadataReader reader,
        ImmutableArray<MethodSemanticsRow> rows)
        => rows
            .Select(row =>
            {
                string association = row.AssociationKind switch
                {
                    MethodSemanticsAssociationKind.Event
                        => reader.GetString(
                            reader.GetEventDefinition(
                                MetadataTokens.EventDefinitionHandle(
                                    row.AssociationRowNumber)).Name),
                    MethodSemanticsAssociationKind.Property
                        => reader.GetString(
                            reader.GetPropertyDefinition(
                                MetadataTokens.PropertyDefinitionHandle(
                                    row.AssociationRowNumber)).Name),
                    _ => throw new InvalidOperationException(
                        "Unknown association kind."),
                };
                string method = reader.GetString(
                    reader.GetMethodDefinition(row.Method).Name);
                string role = row.RawSemantics switch
                {
                    0x01 => "set",
                    0x02 => "get",
                    0x04 => "other",
                    0x08 => "addon",
                    0x10 => "removeon",
                    0x20 => "fire",
                    _ => throw new InvalidOperationException(
                        "The oracle fixture uses a non-conventional role."),
                };
                return $"{row.AssociationKind}:{association}|{role}|{method}";
            })
            .ToArray();

    static string[] ReadIldasmAssociations(string[] lines)
    {
        var rows = new List<string>();
        MethodSemanticsAssociationKind? kind = null;
        string? association = null;
        foreach (string sourceLine in lines)
        {
            string line = sourceLine.Trim();
            if (line.StartsWith(".event ", StringComparison.Ordinal))
            {
                kind = MethodSemanticsAssociationKind.Event;
                association = line[(line.LastIndexOf(' ') + 1)..];
                continue;
            }

            if (line.StartsWith(".property ", StringComparison.Ordinal))
            {
                kind = MethodSemanticsAssociationKind.Property;
                int parenthesis = line.LastIndexOf('(');
                int separator = line.LastIndexOf(' ', parenthesis);
                association = line[(separator + 1)..parenthesis];
                continue;
            }

            if (line == "}")
            {
                kind = null;
                association = null;
                continue;
            }

            string? role = line switch
            {
                _ when line.StartsWith(".get ", StringComparison.Ordinal)
                    => "get",
                _ when line.StartsWith(".set ", StringComparison.Ordinal)
                    => "set",
                _ when line.StartsWith(".other ", StringComparison.Ordinal)
                    => "other",
                _ when line.StartsWith(".addon ", StringComparison.Ordinal)
                    => "addon",
                _ when line.StartsWith(".removeon ", StringComparison.Ordinal)
                    => "removeon",
                _ when line.StartsWith(".fire ", StringComparison.Ordinal)
                    => "fire",
                _ => null,
            };
            if (role is null || kind is null || association is null)
                continue;

            int ownerSeparator = line.LastIndexOf("::", StringComparison.Ordinal);
            int signature = line.IndexOf('(', ownerSeparator);
            string method = line[(ownerSeparator + 2)..signature];
            rows.Add($"{kind}:{association}|{role}|{method}");
        }

        return rows.ToArray();
    }

    static bool CanRunTool(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        try
        {
            using var process = StartTool(fileName, arguments);
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    static async Task RunToolAsync(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        using var process = StartTool(fileName, arguments);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        CancellationToken testCancellation =
            TestContext.Current.CancellationToken;
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(testCancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                }
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);

            if (!testCancellation.IsCancellationRequested)
                throw new TimeoutException($"{fileName} timed out.");
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} exited with code {process.ExitCode}: "
                + $"{await error}\nstdout: {await output}");
        }

        await Task.WhenAll(output, error);
    }

    static Process StartTool(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start {fileName}.");
    }

    const string IlasmOracleSource =
        """
        .assembly extern System.Runtime {}
        .assembly MethodSemanticsProbe {}
        .module MethodSemanticsProbe.dll

        .class public auto ansi abstract beforefieldinit Probe
               extends [System.Runtime]System.Object
        {
          .method public hidebysig specialname abstract virtual instance int32 get_Value() cil managed {}
          .method public hidebysig specialname abstract virtual instance void set_Value(int32) cil managed {}
          .method public hidebysig specialname abstract virtual instance void add_Changed(class [System.Runtime]System.EventHandler) cil managed {}
          .method public hidebysig specialname abstract virtual instance void remove_Changed(class [System.Runtime]System.EventHandler) cil managed {}

          .property instance int32 Value()
          {
            .get instance int32 Probe::get_Value()
            .set instance void Probe::set_Value(int32)
          }

          .event [System.Runtime]System.EventHandler Changed
          {
            .addon instance void Probe::add_Changed(class [System.Runtime]System.EventHandler)
            .removeon instance void Probe::remove_Changed(class [System.Runtime]System.EventHandler)
          }
        }
        """;

    sealed class ArmableReadFailureStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        bool _armed;

        public void Arm() => _armed = true;

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
            => _armed
                ? throw new IOException(
                    "Injected metadata acquisition failure.")
                : base.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer)
            => _armed
                ? throw new IOException(
                    "Injected metadata acquisition failure.")
                : base.Read(buffer);
    }

    sealed class CompilerProducedFixture
    {
        public int Value { get; }
    }
}
