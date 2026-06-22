using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class ObjectInitializerPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, locator: RuntimeLocator);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    static readonly Lazy<IReadOnlyDictionary<string, string>> s_runtimeAssemblies = new(() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase));

    static readonly AssemblyLocator RuntimeLocator = (name, trust) =>
        trust == AssemblyTrust.Platform && s_runtimeAssemblies.Value.TryGetValue(name, out var path)
            ? path
            : null;

    [Fact]
    public void ObjectInitializer_RaisesPropertyMembers()
    {
        var function = Raised(nameof(CfgSampleClass.MakePoint));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Y"], initializer.Members);
        // The creation is retained as a child so fidelity/unsafe scans still see it.
        Assert.Single(function.Descendants.OfType<NewObject>());
        // The lowered dup chain is gone.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
    }

    [Fact]
    public void ObjectInitializer_RaisesFieldMembers()
    {
        var initializer = Assert.Single(Raised(nameof(CfgSampleClass.MakePointWithField)).Descendants.OfType<ObjectInitializerExpression>());

        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Z"], initializer.Members);
    }

    [Fact]
    public void CollectionInitializer_RaisesAddCalls()
    {
        var function = Raised(nameof(CfgSampleClass.MakeList));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        Assert.Equal(3, initializer.Entries.Count);
        // No Add call survives as a standalone statement.
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void PlainConstruction_WithoutInitializer_StaysNewObject()
    {
        var function = Raised(nameof(CfgSampleClass.MakeEmpty));

        Assert.DoesNotContain(function.Descendants.OfType<ObjectInitializerExpression>(), _ => true);
        Assert.Single(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void Initializer_InArgumentPosition_IsRaised()
    {
        var output = Print(nameof(CfgSampleClass.MakeAndRead));

        Assert.Contains("new InitTarget { X = a }", output);
        Assert.DoesNotContain(".X = a;", output);
    }

    [Fact]
    public void NamedLocalObjectInitializer_RaisesIntoLocalDeclaration()
    {
        var function = Raised(nameof(CfgSampleClass.NamedPointInitializer));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Y"], initializer.Members);
        Assert.Empty(function.Descendants.OfType<StoreProperty>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("InitTarget target = new InitTarget { X = a, Y = b };", output);
        Assert.Contains("return target;", output);
    }

    [Fact]
    public void NamedLocalCollectionInitializer_RaisesIntoLocalDeclaration()
    {
        var function = Raised(nameof(CfgSampleClass.NamedListInitializer));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        Assert.Equal(3, initializer.Entries.Count);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("List<int> values = new List<int> { a, b, 42 };", output);
        Assert.Contains("return values;", output);
    }

    [Fact]
    public void DisplayClassLocalSetup_IsNotRaisedAsObjectInitializer()
    {
        var function = Raised(nameof(CfgSampleClass.ClosureWithLinq));

        Assert.DoesNotContain(function.Descendants.OfType<ObjectInitializerExpression>(),
            initializer => GeneratedCodeIdentity.IsDisplayClassName(initializer.Creation.Constructor.DeclaringType));
    }

    [Fact]
    public void InitializerWithExtraOutsideUse_IsNotFoldedIntoSingleExpression()
    {
        // The expression-position slice requires exactly one outside use of the
        // threaded receiver. A kept-alive local has two uses (KeepAlive + return),
        // so folding it into a single object-initializer expression would erase a
        // real use site.
        var function = Raised(nameof(CfgSampleClass.NamedPointInitializerKeptAlive));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".X = a;", output);
        Assert.Contains("GC.KeepAlive", output);
    }

    [Fact]
    public void InitializerWithDuplicateNamedMember_IsNotFoldedIntoInvalidCSharp()
    {
        var function = FunctionWithDuplicateMemberStores();

        new ObjectInitializerPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.Equal(2, function.Descendants.OfType<StoreProperty>().Count());
        function.CheckInvariant();
    }

    [Fact]
    public void ReceiverSlotClobberedBeforeEscape_IsNotFolded()
    {
        // The threaded receiver slot is re-stored with a *different* object between
        // the member-store run and its single downstream load:
        //
        //   S_3 = new T(); S_3.X = 1; S_3 = new T(); return S_3;
        //
        // The escape (`return S_3`) yields the SECOND object, unmutated. Folding the
        // run into `new T { X = 1 }` at that load would drop the re-store and return
        // the wrong object. Dup slots are unique per dup so real lowerings never reuse
        // a receiver slot, but carry slots (and hand-written IL) can — the alias-slot
        // gate must reject a clobbered slot, mirroring the named-local single-store guard.
        var function = FunctionWithClobberedReceiverSlot();

        new ObjectInitializerPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        // Both NewObject creations and the member store survive untouched.
        Assert.Equal(2, function.Descendants.OfType<NewObject>().Count());
        Assert.Single(function.Descendants.OfType<StoreProperty>());
        function.CheckInvariant();
    }

    [Fact]
    public void PrintRaised_RendersInitializers()
    {
        Assert.Contains("return new InitTarget { X = a, Y = b };", Print(nameof(CfgSampleClass.MakePoint)));
        Assert.Contains("return new InitTarget { X = a, Z = b };", Print(nameof(CfgSampleClass.MakePointWithField)));
        Assert.Contains("return new List<int> { a, b, 42 };", Print(nameof(CfgSampleClass.MakeList)));
    }

    [Fact]
    public void IndexerInitializer_RaisesToObjectInitializerWithIndexerMembers()
    {
        var function = Raised(nameof(CfgSampleClass.MakeDictionaryByIndexer));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        // An indexer member is an object-form entry ([k] = v uses `=`), not a collection Add.
        Assert.False(initializer.IsCollection);
        // Each entry carries its key(s) plus the value, and no member name.
        Assert.All(initializer.Entries, entry => Assert.Null(entry.Member));
        Assert.All(initializer.Entries, entry => Assert.Equal(2, entry.Arguments.Count));
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
    }

    [Fact]
    public void DictionaryAddInitializer_RaisesToCollectionInitializerWithMultiArgElements()
    {
        var function = Raised(nameof(CfgSampleClass.MakeDictionaryByAdd));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        // Each dictionary Add element keeps both arguments (key, value).
        Assert.All(initializer.Entries, entry => Assert.Equal(2, entry.Arguments.Count));
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void CollectionInitializerRequiresEnumerableReceiver()
    {
        var function = Raised(nameof(CfgSampleClass.MakeNonEnumerableAddLookalike));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Add(a);", output);
        Assert.DoesNotContain("new NonEnumerableAddTarget {", output);
    }

    [Fact]
    public void NestedObjectInitializer_RaisesMemberStoresIntoAnInitializerBlock()
    {
        var function = Raised(nameof(CfgSampleClass.MakeNestedObject));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        // The single top-level entry is the nested member; its value is the block.
        var entry = Assert.Single(initializer.Entries);
        Assert.Equal("Inner", entry.Member);
        var block = Assert.IsType<InitializerBlock>(Assert.Single(entry.Arguments));
        Assert.False(block.IsCollection);
        Assert.Equal(new string?[] { "X", "Y" }, block.Members);
        // The nested member reads are folded away — no residual stores/loads of Inner.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
    }

    [Fact]
    public void NestedCollectionInitializer_RaisesAddsIntoACollectionBlock()
    {
        var function = Raised(nameof(CfgSampleClass.MakeNestedCollection));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);  // top level assigns Items via `=`
        var entry = Assert.Single(initializer.Entries);
        Assert.Equal("Items", entry.Member);
        var block = Assert.IsType<InitializerBlock>(Assert.Single(entry.Arguments));
        Assert.True(block.IsCollection);
        Assert.Equal(2, block.Entries.Count);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void PrintRaised_RendersNestedInitializers()
    {
        Assert.Contains(
            "return new InitContainer { Inner = { X = a, Y = b } };",
            Print(nameof(CfgSampleClass.MakeNestedObject)));
        Assert.Contains(
            "return new InitContainer { Items = { a, b } };",
            Print(nameof(CfgSampleClass.MakeNestedCollection)));
    }

    [Fact]
    public void NestedReassignment_KeepsTheNewAndDoesNotCollapseToMutation()
    {
        // `Inner = new InitTarget { ... }` reconstructs Inner (a flat member store
        // whose value is its own sub-initializer); it must keep `new` and never
        // render as the in-place nested-mutation form `Inner = { ... }`. The outer
        // initializer only folds because seeds are raised inner-first.
        var output = Print(nameof(CfgSampleClass.MakeNestedReassign));

        Assert.Contains("return new InitContainer { Inner = new InitTarget { X = a, Y = b } };", output);
        Assert.DoesNotContain("Inner = { ", output);
    }

    [Fact]
    public void FlatAndNestedMembers_RaiseTogether()
    {
        var output = Print(nameof(CfgSampleClass.MakeFlatAndNested));

        Assert.Contains("return new InitContainer { Tag = c, Inner = { X = a, Y = b } };", output);
    }

    [Fact]
    public void TwoNestedMembers_RaiseAsSeparateBlocks()
    {
        var function = Raised(nameof(CfgSampleClass.MakeTwoNestedMembers));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(new string?[] { "Inner", "Items" }, initializer.Members);
        var blocks = initializer.Entries
            .Select(entry => Assert.IsType<InitializerBlock>(Assert.Single(entry.Arguments)))
            .ToList();
        Assert.False(blocks[0].IsCollection);  // Inner = { X = a }
        Assert.True(blocks[1].IsCollection);   // Items = { b }
        Assert.Contains("return new InitContainer { Inner = { X = a }, Items = { b } };", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void NamedLocalNestedMutation_StaysLowered()
    {
        // A nested mutation through a real local (used twice: KeepAlive + return) is
        // not the expression-position dup form, so it must not fold to an initializer.
        var function = Raised(nameof(CfgSampleClass.MakeNamedLocalNestedMutation));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Inner.X = a;", output);
        Assert.Contains("GC.KeepAlive", output);
    }

    static IrFunction FunctionWithDuplicateMemberStores()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true);
        var setter = new MethodRef(type, "set_X", TypeRef.CoreLib("System", "Void"), [intType], HasThis: true)
        {
            IsSpecialName = true,
        };

        var block = new Block();
        block.Add(new StoreStackSlot(256, new NewObject(ctor, [])));
        block.Add(new StoreStackSlot(257, new LoadStackSlot(256, type)));
        block.Add(new StoreProperty(setter, new LoadStackSlot(257, type), [], new Constant(1, intType)));
        block.Add(new StoreStackSlot(258, new LoadStackSlot(256, type)));
        block.Add(new StoreProperty(setter, new LoadStackSlot(258, type), [], new Constant(2, intType)));
        block.Add(new Return(new LoadStackSlot(256, type)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "DuplicateMember",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction FunctionWithClobberedReceiverSlot()
    {
        var type = TypeRef.Definition("Synthetic", "Samples", "InitTarget");
        var intType = TypeRef.CoreLib("System", "Int32");
        var ctor = new MethodRef(type, ".ctor", TypeRef.CoreLib("System", "Void"), [], HasThis: true);
        var setter = new MethodRef(type, "set_X", TypeRef.CoreLib("System", "Void"), [intType], HasThis: true)
        {
            IsSpecialName = true,
        };

        // A reusable carry slot (< DupSlotBase) holds the receiver, gets one member
        // store, then is re-stored with a second, unrelated object before the escape.
        const int slot = 3;
        var block = new Block();
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new StoreProperty(setter, new LoadStackSlot(slot, type), [], new Constant(1, intType)));
        block.Add(new StoreStackSlot(slot, new NewObject(ctor, [])));
        block.Add(new Return(new LoadStackSlot(slot, type)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "ClobberedReceiver",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(type, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
