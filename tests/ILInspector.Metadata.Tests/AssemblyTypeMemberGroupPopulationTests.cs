using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ILInspector.Metadata.Tests;

public sealed class AssemblyTypeMemberGroupPopulationTests
{
    [Fact]
    public void
        JsonSerializer_CountAvoidsRowsAndAllocatesLessThanEagerSurface()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(JsonSerializer).Assembly.Location);
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        TypeDefinitionToken token =
            Assert.IsType<TypeDeclarationResult.Defined>(
                    session.ProbeDeclaration(type))
                .Definition;
        var request = new AssemblyTypeMemberGroupPopulationRequest(
            MetadataTypeDefinitionAddress.FromToken(
                session.ModuleVersionId(),
                token.Value),
            AssemblyTypeMemberGroupCategory.Method,
            AssemblyTypeMemberReceiverKinds.All,
            AssemblyTypeMemberGroupTerminal.Count,
            [],
            includeExactMemberCount: false,
            maximumRetainedGroups: 10_000,
            maximumNameWorkBytes: 16 * 1024 * 1024,
            maximumRetainedTextCharacters: 1_000_000);

        _ = Count(session, request);
        _ = EagerMethodGroupCount(session);
        long compact = Allocated(
            () => Count(session, request),
            iterations: 5);
        long eager = Allocated(
            () => EagerMethodGroupCount(session),
            iterations: 5);

        AssemblyTypeMemberGroupPopulationOutcome.Counted counted =
            Count(session, request);
        Assert.Equal(10, counted.Value);
        Assert.Equal(0, counted.Work.RowsMaterialized);
        Assert.Equal(0, counted.Work.ExactMemberRowsMaterialized);
        Assert.True(counted.Work.SignaturesDecoded > 0);
        Assert.Equal(
            0,
            counted.Work.FormattedSignaturesMaterialized);
        Assert.True(
            compact * 2 < eager,
            $"Expected compact Count allocation ({compact:N0} bytes) "
                + $"to be less than half eager allocation ({eager:N0} bytes).");
    }

    [Fact]
    public void JsonSerializer_BoundedRowsMaterializeOnlySelectedGroups()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(JsonSerializer).Assembly.Location);
        TypeDefinitionToken token =
            Assert.IsType<TypeDeclarationResult.Defined>(
                    session.ProbeDeclaration(
                        Name(
                            "System.Text.Json",
                            "JsonSerializer")))
                .Definition;
        var request = new AssemblyTypeMemberGroupPopulationRequest(
            MetadataTypeDefinitionAddress.FromToken(
                session.ModuleVersionId(),
                token.Value),
            AssemblyTypeMemberGroupCategory.Method,
            AssemblyTypeMemberReceiverKinds.All,
            AssemblyTypeMemberGroupTerminal.Rows,
            [AssemblyTypeMemberGroupSelectionStage.Head(1)],
            includeExactMemberCount: true,
            maximumRetainedGroups: 10_000,
            maximumNameWorkBytes: 16 * 1024 * 1024,
            maximumRetainedTextCharacters: 1_000_000);

        AssemblyTypeMemberGroupPopulationOutcome.Read rows =
            Assert.IsType<AssemblyTypeMemberGroupPopulationOutcome.Read>(
                session.TypeMemberGroups(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.Single(rows.Rows);
        Assert.Equal(1, rows.Work.RowsMaterialized);
        Assert.Equal(10, rows.Work.GroupsAggregated);
        Assert.Equal(0, rows.Work.ExactMemberRowsMaterialized);
        Assert.True(rows.Work.SignaturesDecoded > 0);
        Assert.Equal(
            0,
            rows.Work.FormattedSignaturesMaterialized);
    }

    [Fact]
    public void CategoryFixture_PreservesKindsCountsAndReceivers()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(MemberGroupCategoryFixture).Assembly.Location);
        TypeDefinitionToken token =
            Assert.IsType<TypeDeclarationResult.Defined>(
                    session.ProbeDeclaration(
                        Name(
                            "ILInspector.Metadata.Tests",
                            nameof(MemberGroupCategoryFixture))))
                .Definition;
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromToken(
                session.ModuleVersionId(),
                token.Value);

        Assert.Single(
            Rows(
                session,
                address,
                AssemblyTypeMemberGroupCategory.Constructor));
        Assert.Single(
            Rows(
                session,
                address,
                AssemblyTypeMemberGroupCategory.Operator));
        Assert.Single(
            Rows(
                session,
                address,
                AssemblyTypeMemberGroupCategory
                    .ExplicitInterfaceImplementation));
        Assert.Equal(
            2,
            Rows(
                session,
                address,
                AssemblyTypeMemberGroupCategory.Property).Count);
        Assert.Equal(
            2,
            Rows(
                session,
                address,
                AssemblyTypeMemberGroupCategory.Field).Count);
        Assert.Equal(
            2,
            Rows(
                session,
                address,
                AssemblyTypeMemberGroupCategory.Event).Count);

        AssemblyTypeMemberGroupRow method =
            Assert.Single(
                Rows(
                    session,
                    address,
                    AssemblyTypeMemberGroupCategory.Method),
                static row => row.Name.ToString() == "Method");
        Assert.Equal(2, method.ExactMemberCount);
        Assert.Equal(
            AssemblyTypeMemberReceiverKinds.Static
                | AssemblyTypeMemberReceiverKinds.This,
            method.ReceiverKinds);
    }

    [Fact]
    public void ExtensionPropertyIsAttachedToItsReceiverType()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                typeof(MemberGroupExtensionReceiver).Assembly.Location);
        TypeDefinitionToken token =
            Assert.IsType<TypeDeclarationResult.Defined>(
                    session.ProbeDeclaration(
                        Name(
                            "ILInspector.Metadata.Tests",
                            nameof(MemberGroupExtensionReceiver))))
                .Definition;

        AssemblyTypeMemberGroupRow property =
            Assert.Single(
                Rows(
                    session,
                    MetadataTypeDefinitionAddress.FromToken(
                        session.ModuleVersionId(),
                        token.Value),
                    AssemblyTypeMemberGroupCategory.Property));

        Assert.Equal("Value", property.Name.ToString());
        Assert.Equal(
            AssemblyTypeMemberGroupRole.AttachedExtension,
            property.Role);
        Assert.Equal(
            Name(
                "ILInspector.Metadata.Tests",
                nameof(MemberGroupExtensionFixture)),
            property.AttachedDeclaringType);
        Assert.Equal(
            AssemblyTypeMemberReceiverKinds.Extension,
            property.ReceiverKinds);
        Assert.Equal(1, property.ExactMemberCount);
    }

    [Theory]
    [InlineData(AssemblyTypeMemberGroupTerminal.Count)]
    [InlineData(AssemblyTypeMemberGroupTerminal.Rows)]
    public void ExtensionPropertyWithoutExactlyOneReceiverFails(
        AssemblyTypeMemberGroupTerminal terminal)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"member-group-zero-receiver-{Guid.NewGuid():N}.dll");
        try
        {
            byte[] image = File.ReadAllBytes(
                typeof(MemberGroupExtensionReceiver).Assembly.Location);
            using (var pe = new PEReader(
                new MemoryStream(image, writable: false)))
            {
                MetadataReader reader = pe.GetMetadataReader();
                TypeDefinition fixture = reader.GetTypeDefinition(
                    Assert.Single(
                        reader.TypeDefinitions,
                        handle =>
                        {
                            TypeDefinition type =
                                reader.GetTypeDefinition(handle);
                            return reader.StringComparer.Equals(
                                    type.Namespace,
                                    "ILInspector.Metadata.Tests")
                                && reader.StringComparer.Equals(
                                    type.Name,
                                    nameof(MemberGroupExtensionFixture));
                        }));
                TypeDefinition grouping = reader.GetTypeDefinition(
                    Assert.Single(fixture.GetNestedTypes()));
                TypeDefinition marker = reader.GetTypeDefinition(
                    Assert.Single(grouping.GetNestedTypes()));
                MethodDefinition receiver = reader.GetMethodDefinition(
                    Assert.Single(
                        marker.GetMethods(),
                        handle => reader.StringComparer.Equals(
                            reader.GetMethodDefinition(handle).Name,
                            "<Extension>$")));
                byte[] signature = reader.GetBlobBytes(receiver.Signature);
                int entry = pe.PEHeaders.MetadataStartOffset
                    + reader.GetHeapMetadataOffset(HeapIndex.Blob)
                    + MetadataTokens.GetHeapOffset(receiver.Signature);

                Assert.InRange(signature.Length, 3, 0x7f);
                Assert.Equal(signature.Length, image[entry]);
                image[entry] = 3;
                image[entry + 1] = 0x00;
                image[entry + 2] = 0x00;
                image[entry + 3] = 0x01;
            }
            File.WriteAllBytes(path, image);

            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(path);
            TypeDefinitionToken token =
                Assert.IsType<TypeDeclarationResult.Defined>(
                        session.ProbeDeclaration(
                            Name(
                                "ILInspector.Metadata.Tests",
                                nameof(MemberGroupExtensionReceiver))))
                    .Definition;
            AssemblyTypeMemberGroupPopulationOutcome outcome =
                session.TypeMemberGroups(
                    new(
                        MetadataTypeDefinitionAddress.FromToken(
                            session.ModuleVersionId(),
                            token.Value),
                        AssemblyTypeMemberGroupCategory.Property,
                        AssemblyTypeMemberReceiverKinds.All,
                        terminal,
                        [],
                        includeExactMemberCount:
                            terminal
                                == AssemblyTypeMemberGroupTerminal.Rows,
                        maximumRetainedGroups: 100,
                        maximumNameWorkBytes: 1_000_000,
                        maximumRetainedTextCharacters: 10_000),
                    TestContext.Current.CancellationToken);

            Assert.IsType<
                AssemblyTypeMemberGroupPopulationOutcome.Failed>(outcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AssemblyTypeMemberGroupPopulationOutcome.Counted Count(
        AssemblyInspectionSession session,
        AssemblyTypeMemberGroupPopulationRequest request) =>
        Assert.IsType<
            AssemblyTypeMemberGroupPopulationOutcome.Counted>(
            session.TypeMemberGroups(
                request,
                TestContext.Current.CancellationToken));

    private static int EagerMethodGroupCount(
        AssemblyInspectionSession session)
    {
        ApiSurface surface = session.ApiSurface();
        ApiType type = Assert.Single(
            surface.Types,
            static type => type.FullName
                == "System.Text.Json.JsonSerializer");
        int count = type.Members
            .Where(static member => member.Kind == "method")
            .Select(static member => member.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();
        GC.KeepAlive(surface);
        return count;
    }

    private static long Allocated(Action action, int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < iterations; iteration++)
            action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static IReadOnlyList<AssemblyTypeMemberGroupRow> Rows(
        AssemblyInspectionSession session,
        MetadataTypeDefinitionAddress type,
        AssemblyTypeMemberGroupCategory category) =>
        Assert.IsType<AssemblyTypeMemberGroupPopulationOutcome.Read>(
                session.TypeMemberGroups(
                    new(
                        type,
                        category,
                        AssemblyTypeMemberReceiverKinds.All,
                        AssemblyTypeMemberGroupTerminal.Rows,
                        [],
                        includeExactMemberCount: true,
                        maximumRetainedGroups: 100,
                        maximumNameWorkBytes: 1_000_000,
                        maximumRetainedTextCharacters: 10_000),
                    TestContext.Current.CancellationToken))
            .Rows;

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}

public interface IMemberGroupCategoryFixture
{
    void Explicit();
}

public sealed class MemberGroupCategoryFixture :
    IMemberGroupCategoryFixture
{
    ~MemberGroupCategoryFixture()
    {
    }

    public MemberGroupCategoryFixture()
    {
    }

    public MemberGroupCategoryFixture(int value)
    {
        InstanceField = value;
    }

    public static int StaticField;
    public int InstanceField;

    public static int StaticProperty { get; set; }
    public int InstanceProperty { get; set; }

    public static event EventHandler? StaticEvent;
    public event EventHandler? InstanceEvent;

    public static MemberGroupCategoryFixture operator +(
        MemberGroupCategoryFixture left,
        MemberGroupCategoryFixture right) =>
        new(left.InstanceField + right.InstanceField);

    public static void Method(int value)
    {
        StaticField = value;
    }

    public void Method()
    {
        InstanceField++;
    }

    public static void RaiseStaticEvent() =>
        StaticEvent?.Invoke(null, EventArgs.Empty);

    public void RaiseInstanceEvent() =>
        InstanceEvent?.Invoke(this, EventArgs.Empty);

    void IMemberGroupCategoryFixture.Explicit()
    {
    }
}

public sealed class MemberGroupExtensionReceiver;

public static class MemberGroupExtensionFixture
{
    extension(MemberGroupExtensionReceiver receiver)
    {
        public int Value => receiver.GetHashCode();
    }
}
