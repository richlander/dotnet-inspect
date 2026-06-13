using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class ApiDiffAnalyzerTests
{
    #region Helpers

    private static ApiSurface Surface(params ApiType[] types)
    {
        var surface = new ApiSurface();
        surface.Types.AddRange(types);
        return surface;
    }

    private static ApiType Type(
        string name,
        string kind = "class",
        string? ns = "TestNamespace",
        bool isSealed = false,
        bool isAbstract = false,
        string? baseType = null,
        List<string>? interfaces = null,
        List<TypeParameter>? typeParameters = null,
        List<ApiMember>? members = null)
    {
        return new ApiType
        {
            Name = name,
            Kind = kind,
            Namespace = ns,
            IsSealed = isSealed,
            IsAbstract = isAbstract,
            IsStatic = isSealed && isAbstract,
            BaseType = baseType,
            Interfaces = interfaces,
            TypeParameters = typeParameters,
            Members = members ?? []
        };
    }

    private static ApiMember Method(string name, string signature, bool isVirtual = false, bool isAbstract = false)
    {
        return new ApiMember
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            IsVirtual = isVirtual,
            IsAbstract = isAbstract
        };
    }

    private static ApiMember Property(string name, string signature)
    {
        return new ApiMember { Name = name, Kind = "property", Signature = signature };
    }

    private static ApiMember Field(string name, long? enumValue = null)
    {
        return new ApiMember { Name = name, Kind = "field", EnumValue = enumValue };
    }

    private static ApiMember Event(string name)
    {
        return new ApiMember { Name = name, Kind = "event" };
    }

    #endregion

    [Fact]
    public void IdenticalSurfaces_ReturnsEmptyDiff()
    {
        var type = Type("Foo", members: [Method("Bar", "void Bar()")]);
        var oldSurface = Surface(type);
        var newSurface = Surface(type);

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.True(diff.IsEmpty);
        Assert.Equal(0, diff.TotalBreaking);
        Assert.Equal(0, diff.TotalAdditive);
        Assert.Equal(0, diff.TotalPotentiallyBreaking);
    }

    [Fact]
    public void TypeAdded_IsAdditive()
    {
        var oldSurface = Surface();
        var newSurface = Surface(Type("Foo"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Single(diff.TypeDiffs);
        var typeDiff = diff.TypeDiffs[0];
        Assert.True(typeDiff.IsAdded);
        Assert.False(typeDiff.IsRemoved);
        Assert.Equal(1, diff.TotalAdditive);
        Assert.Equal(0, diff.TotalBreaking);
    }

    [Fact]
    public void TypeRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo"));
        var newSurface = Surface();

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Single(diff.TypeDiffs);
        var typeDiff = diff.TypeDiffs[0];
        Assert.True(typeDiff.IsRemoved);
        Assert.False(typeDiff.IsAdded);
        Assert.Equal(1, diff.TotalBreaking);
    }

    [Fact]
    public void TypeKindChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", kind: "class"));
        var newSurface = Surface(Type("Foo", kind: "struct"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeKindChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("class", change.OldValue);
        Assert.Equal("struct", change.NewValue);
    }

    [Fact]
    public void SealedAdded_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", isSealed: false));
        var newSurface = Surface(Type("Foo", isSealed: true));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.SealedAdded);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void SealedRemoved_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", isSealed: true));
        var newSurface = Surface(Type("Foo", isSealed: false));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.SealedRemoved);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void AbstractAdded_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", isAbstract: false));
        var newSurface = Surface(Type("Foo", isAbstract: true));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.AbstractAdded);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void AbstractRemoved_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", isAbstract: true));
        var newSurface = Surface(Type("Foo", isAbstract: false));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.AbstractRemoved);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void StaticType_NoSpuriousSealedOrAbstractChanges()
    {
        // Static types have both IsSealed and IsAbstract true — should not report changes
        var oldSurface = Surface(Type("Foo", isSealed: true, isAbstract: true));
        var newSurface = Surface(Type("Foo", isSealed: true, isAbstract: true));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void BaseTypeChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", baseType: "System.Object"));
        var newSurface = Surface(Type("Foo", baseType: "System.Exception"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.BaseTypeChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("System.Object", change.OldValue);
        Assert.Equal("System.Exception", change.NewValue);
    }

    [Fact]
    public void InterfaceRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", interfaces: ["IDisposable", "ICloneable"]));
        var newSurface = Surface(Type("Foo", interfaces: ["IDisposable"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.InterfaceRemoved);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Contains("ICloneable", change.Message);
    }

    [Fact]
    public void InterfaceAddedOnClass_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", kind: "class", interfaces: ["IDisposable"]));
        var newSurface = Surface(Type("Foo", kind: "class", interfaces: ["IDisposable", "ICloneable"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.InterfaceAdded);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void InterfaceAddedOnInterface_IsPotentiallyBreaking()
    {
        var oldSurface = Surface(Type("IFoo", kind: "interface"));
        var newSurface = Surface(Type("IFoo", kind: "interface", interfaces: ["IBar"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.InterfaceAdded);
        Assert.Equal(ChangeClassification.PotentiallyBreaking, change.Classification);
    }

    [Fact]
    public void GenericParamCountChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }]));
        var newSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }, new TypeParameter { Name = "U" }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterCountChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void GenericVarianceChanged_IsBreaking()
    {
        var oldSurface = Surface(Type("IFoo", kind: "interface", typeParameters:
            [new TypeParameter { Name = "T", Variance = "out" }]));
        var newSurface = Surface(Type("IFoo", kind: "interface", typeParameters:
            [new TypeParameter { Name = "T", Variance = "in" }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterVarianceChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ConstraintTightened_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }]));
        var newSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T", Constraints = { "class" } }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterConstraintTightened);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ConstraintLoosened_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T", Constraints = { "class" } }]));
        var newSurface = Surface(Type("Foo", typeParameters:
            [new TypeParameter { Name = "T" }]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.TypeParameterConstraintLoosened);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
    }

    [Fact]
    public void MemberAddedOnClass_IsAdditive()
    {
        var oldSurface = Surface(Type("Foo", members: [Method("Bar", "void Bar()")]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberAdded);
        Assert.Equal(ChangeClassification.Additive, change.Classification);
        Assert.Contains("Baz", change.Message);
    }

    [Fact]
    public void MemberAddedOnInterface_IsPotentiallyBreaking()
    {
        var oldSurface = Surface(Type("IFoo", kind: "interface",
            members: [Method("Bar", "void Bar()")]));
        var newSurface = Surface(Type("IFoo", kind: "interface",
            members: [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberAdded);
        Assert.Equal(ChangeClassification.PotentiallyBreaking, change.Classification);
    }

    [Fact]
    public void MemberRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));
        var newSurface = Surface(Type("Foo", members: [Method("Bar", "void Bar()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberRemoved);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Contains("Baz", change.Message);
    }

    [Fact]
    public void MemberSignatureChanged_PairsAndReportsBreaking()
    {
        var oldSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar(int x)")]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar(string x)")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.MemberSignatureChanged);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("void Bar(int x)", change.OldValue);
        Assert.Equal("void Bar(string x)", change.NewValue);
    }

    [Fact]
    public void VirtualRemoved_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()", isVirtual: true)]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()", isVirtual: false)]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.VirtualRemoved);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void AbstractMemberAdded_IsBreaking()
    {
        var oldSurface = Surface(Type("Foo", isAbstract: true, members:
            [Method("Bar", "void Bar()", isAbstract: false, isVirtual: true)]));
        var newSurface = Surface(Type("Foo", isAbstract: true, members:
            [Method("Bar", "void Bar()", isAbstract: true, isVirtual: true)]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.AbstractMemberAdded);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void EnumValueChanged_IsPotentiallyBreaking()
    {
        var oldSurface = Surface(Type("MyEnum", kind: "enum", members:
            [Field("One", enumValue: 1), Field("Two", enumValue: 2)]));
        var newSurface = Surface(Type("MyEnum", kind: "enum", members:
            [Field("One", enumValue: 1), Field("Two", enumValue: 3)]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(diff.TypeDiffs).Changes
            .Single(c => c.Kind == ChangeKind.EnumValueChanged);
        Assert.Equal(ChangeClassification.PotentiallyBreaking, change.Classification);
        Assert.Equal("2", change.OldValue);
        Assert.Equal("3", change.NewValue);
    }

    [Fact]
    public void CompilerGeneratedMembers_AreFiltered()
    {
        var oldSurface = Surface(Type("Foo", members:
        [
            Method("Bar", "void Bar()"),
            new ApiMember { Name = "<Clone>$", Kind = "method", Signature = "Foo <Clone>$()" }
        ]));
        var newSurface = Surface(Type("Foo", members:
            [Method("Bar", "void Bar()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        // Compiler-generated <Clone>$ should be filtered, so no diff
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void MultipleChangesOnOneType_AllCaptured()
    {
        var oldSurface = Surface(Type("Foo",
            isSealed: false,
            interfaces: ["IDisposable"],
            members: [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]));
        var newSurface = Surface(Type("Foo",
            isSealed: true,
            interfaces: ["IDisposable", "ICloneable"],
            members: [Method("Bar", "void Bar()")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var typeDiff = Assert.Single(diff.TypeDiffs);
        Assert.Equal(3, typeDiff.Changes.Count);

        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.SealedAdded);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.InterfaceAdded);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.MemberRemoved);
    }

    [Fact]
    public void AggregateCounts_AreCorrect()
    {
        var oldSurface = Surface(
            Type("Foo", members: [Method("Bar", "void Bar()")]),
            Type("Removed"));
        var newSurface = Surface(
            Type("Foo", members: [Method("Bar", "void Bar()"), Method("Baz", "void Baz()")]),
            Type("Added"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        // Removed type = 1 breaking, Added type = 1 additive, Baz added = 1 additive
        Assert.Equal(1, diff.TotalBreaking);
        Assert.Equal(2, diff.TotalAdditive);
        Assert.Equal(0, diff.TotalPotentiallyBreaking);
    }

    [Fact]
    public void TypesAreSortedByName()
    {
        var oldSurface = Surface(Type("Zebra"), Type("Alpha"));
        var newSurface = Surface();

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Equal(2, diff.TypeDiffs.Count);
        Assert.Equal("TestNamespace.Alpha", diff.TypeDiffs[0].TypeFullName);
        Assert.Equal("TestNamespace.Zebra", diff.TypeDiffs[1].TypeFullName);
    }

    [Fact]
    public void EmptySurfaces_ReturnsEmptyDiff()
    {
        var diff = ApiDiffAnalyzer.Compare(new ApiSurface(), new ApiSurface());

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void FieldMembers_MatchByKindAndName()
    {
        // Fields don't have signatures — matched by kind:name key
        var oldSurface = Surface(Type("Foo", members:
            [Field("MaxValue")]));
        var newSurface = Surface(Type("Foo", members:
            [Field("MaxValue")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void EventMember_AddedAndRemoved()
    {
        var oldSurface = Surface(Type("Foo", members: [Event("Click")]));
        var newSurface = Surface(Type("Foo", members: [Event("Tap")]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var typeDiff = Assert.Single(diff.TypeDiffs);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.MemberRemoved);
        Assert.Contains(typeDiff.Changes, c => c.Kind == ChangeKind.MemberAdded);
    }
}
