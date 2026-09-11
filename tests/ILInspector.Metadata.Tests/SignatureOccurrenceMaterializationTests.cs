using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// A closed source-effect inventory, including transitive constructors, accessors,
/// initializers and collection construction. Class B raw reads must inherit an
/// exact-storage charge through the actual helper call chain. External framework
/// and MetadataPrimitives contracts are named imports, not re-proved internals.
/// </summary>
public sealed class SignatureOccurrenceMaterializationTests
{
    enum Cost { Scalar, ClassA, ClassB, FlagDependentKey, Budget, ImportedGuard }

    // A closed allow list of bound symbols, not a list of forbidden read names.
    // Source helper bodies remain in the checked inventory.
    static readonly Dictionary<string, Cost> Calls = new(StringComparer.Ordinal)
    {
        ["System.ArgumentNullException.ThrowIfNull"] = Cost.Scalar,
        ["System.ArgumentException.ThrowIfNullOrWhiteSpace"] = Cost.Scalar,
        ["System.ArgumentOutOfRangeException.ThrowIfNegative"] = Cost.Scalar,
        ["System.Math.Max"] = Cost.Scalar,
        ["System.Enum.GetValues"] = Cost.ClassA,
        ["ILInspector.Metadata.MetadataImageFormatClassifier.Classify"] = Cost.ImportedGuard,
        ["ILInspector.Metadata.SignatureBlobGuard.IsSafeAndCompleteToDecode"] = Cost.ImportedGuard,
        ["ILInspector.Metadata.TypeSpecGuard.TryEnter"] = Cost.ImportedGuard,
        ["ILInspector.Metadata.MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope"] = Cost.ImportedGuard,
        ["ILInspector.Metadata.MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain"] = Cost.ImportedGuard,
        ["ILInspector.Metadata.MetadataRelationshipTraversal.TryWalkExportedTypeImplementationChain"] = Cost.ImportedGuard,
        ["ILInspector.Metadata.MetadataTypeNameBudget.TryRead"] = Cost.ClassA,
        ["ILInspector.Metadata.MetadataTypeNameFailure.From"] = Cost.ClassA,
        ["ILInspector.Metadata.MetadataTypeNameFailure.Malformed"] = Cost.ClassA,
        ["System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader"] = Cost.ImportedGuard,
        ["System.Reflection.Metadata.MetadataReader.GetMethodDefinition"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetFieldDefinition"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetPropertyDefinition"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetTypeDefinition"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetTypeReference"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetTypeSpecification"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetExportedType"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetAssemblyReference"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetModuleReference"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetBlobReader"] = Cost.Scalar,
        ["System.Reflection.Metadata.MetadataReader.GetString"] = Cost.ClassB,
        ["System.Reflection.Metadata.MetadataReader.GetBlobBytes"] = Cost.FlagDependentKey,
        ["System.Reflection.Metadata.MethodDefinition.DecodeSignature"] = Cost.ImportedGuard,
        ["System.Reflection.Metadata.FieldDefinition.DecodeSignature"] = Cost.ImportedGuard,
        ["System.Reflection.Metadata.PropertyDefinition.DecodeSignature"] = Cost.ImportedGuard,
        ["System.Reflection.Metadata.TypeSpecification.DecodeSignature"] = Cost.ImportedGuard,
        ["System.Collections.Immutable.ImmutableArray.CreateBuilder"] = Cost.ClassA,
        ["System.Collections.Immutable.ImmutableArray<T>.Builder.Add"] = Cost.ClassA,
        ["System.Collections.Immutable.ImmutableArray<T>.Builder.AddRange"] = Cost.ClassA,
        ["System.Collections.Immutable.ImmutableArray<T>.Builder.ToImmutable"] = Cost.ClassA,
        ["System.Collections.Immutable.ImmutableArray<T>.Builder.MoveToImmutable"] = Cost.ClassA,
        ["System.HashCode.Add"] = Cost.ClassA,
        ["System.HashCode.ToHashCode"] = Cost.Scalar,
        ["System.Action<T>.Invoke"] = Cost.Budget,
        ["string.IsNullOrWhiteSpace"] = Cost.Scalar,
        ["string.IsNullOrEmpty"] = Cost.Scalar,
        ["string.ToLowerInvariant"] = Cost.ClassA,
        ["System.Convert.ToHexString"] = Cost.ClassA,
        ["System.Security.Cryptography.SHA1.HashData"] = Cost.ClassB,
        ["object..ctor"] = Cost.Scalar,
    };

    // Every non-scalar construction is tied to its source site. These entries
    // are derived from the reachable source, not from a census of executions.
    static readonly Dictionary<string, (Cost Class, int Count)> Materializations =
        SignatureOccurrenceMaterializationInventory.Sites.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split('|', 3))
            .ToDictionary(parts => parts[2], parts => (
                parts[0] switch { "A" => Cost.ClassA, "B" => Cost.ClassB, "K" => Cost.FlagDependentKey,
                    _ => throw new InvalidOperationException("Unclassified inventory class.") },
                int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)), StringComparer.Ordinal);

    static readonly HashSet<string> ScalarProperties = new(StringComparer.Ordinal)
    {
        "System.Reflection.Metadata.EntityHandle.Kind",
        "System.Reflection.Metadata.EntityHandle.IsNil",
        "System.Reflection.Metadata.StringHandle.IsNil",
        "System.Reflection.Metadata.BlobHandle.IsNil",
        "System.Reflection.Metadata.AssemblyReference.Flags",
        "System.Reflection.Metadata.AssemblyReference.Name",
        "System.Reflection.Metadata.AssemblyReference.Culture",
        "System.Reflection.Metadata.AssemblyReference.PublicKeyOrToken",
        "System.Reflection.Metadata.ModuleReference.Name",
        "System.Reflection.Metadata.TypeDefinition.Name",
        "System.Reflection.Metadata.TypeDefinition.Namespace",
        "System.Reflection.Metadata.TypeReference.Name",
        "System.Reflection.Metadata.TypeReference.Namespace",
        "System.Reflection.Metadata.ExportedType.Name",
        "System.Reflection.Metadata.ExportedType.Namespace",
        "System.Reflection.Metadata.TypeSpecification.Signature",
        "System.Reflection.Metadata.MethodDefinition.Signature",
        "System.Reflection.Metadata.FieldDefinition.Signature",
        "System.Reflection.Metadata.PropertyDefinition.Signature",
        "System.Reflection.Metadata.BlobReader.Length",
        "System.Reflection.Metadata.MethodSignature<TType>.ReturnType",
        "System.Reflection.Metadata.MethodSignature<TType>.ParameterTypes",
        "System.Collections.Immutable.ImmutableArray<T>.Length",
        "System.Collections.Immutable.ImmutableArray<T>.IsDefaultOrEmpty",
        "System.Collections.Immutable.ImmutableArray<T>.this[]",
        "System.Span<T>.Length",
        "System.ReadOnlySpan<T>.Length",
        "System.ReadOnlySpan<T>.this[]",
        "System.Span<T>.this[]",
        "System.Array.Length",
        "string.Length",
        "System.Exception.Message",
        "System.StringComparer.Ordinal",
        "ILInspector.Metadata.RelationshipTraversalRejection.ConsumedNodes",
        "ILInspector.Metadata.MetadataTypeNameFailure.RelationshipKind",
        "ILInspector.MetadataPrimitives.SignatureBlobGuardCount.Count",
        "ILInspector.MetadataPrimitives.SignatureBlobGuardCount.Total",
        "ILInspector.MetadataPrimitives.SignatureBlobGuardCount.Largest",
    };

    [Fact]
    public void DecoderEffectInventory_IsClosedAndIncludesTransitiveSourceBodies()
    {
        var compilation = Compilation();
        var violations = InventoryViolations(compilation).ToArray();
        Assert.True(violations.Length == 0, string.Join("\n",
            violations.GroupBy(value => value).Select(group => $"{group.Count()}|{group.Key}")));
    }

    [Fact]
    public void ClassBRawReads_AreDominatedByExactStorageChargesThroughHelpers()
    {
        var compilation = Compilation();
        Assert.True(!DominanceViolations(compilation).Any(), string.Join("\n", DominanceViolations(compilation)));
    }

    [Theory]
    [InlineData("budget.Node(); reader.GetString(default(StringHandle));")]
    [InlineData("budget.Node(); reader.GetUserString(default(UserStringHandle));")]
    public void NewMetadataReadInProvider_IsNotAdmittedByExistingMemberNames(string newText)
    {
        var compilation = Compilation(source => source.Replace(
            "budget.Node();\n        var name", newText + "\n        var name", StringComparison.Ordinal));
        Assert.NotEmpty(InventoryViolations(compilation));
    }

    [Fact]
    public void NewReadInsideChargeHelper_IsNotExempt()
    {
        var compilation = Compilation(source => source.Replace(
            "ArgumentOutOfRangeException.ThrowIfNegative(amount);",
            "ArgumentOutOfRangeException.ThrowIfNegative(amount); " +
            "System.Reflection.Metadata.MetadataReader r = null!; r.GetString(default);",
            StringComparison.Ordinal));
        Assert.NotEmpty(InventoryViolations(compilation));
    }

    [Fact]
    public void ConditionalCharge_DoesNotDominateModuleMaterialization()
    {
        var compilation = Compilation(source => source.Replace(
            "budget.Work(SignatureOccurrenceMetric.ModuleReferenceNameBytes,",
            "if (handle.IsNil) budget.Work(SignatureOccurrenceMetric.ModuleReferenceNameBytes,",
            StringComparison.Ordinal));
        Assert.NotEmpty(DominanceViolations(compilation));
    }

    [Fact]
    public void ChargeAfterModuleMaterialization_IsRejected()
    {
        var compilation = Compilation(source => source.Replace(
            """
            budget.Work(SignatureOccurrenceMetric.ModuleReferenceNameBytes,
                        reader.GetBlobReader(module.Name).Length);
                    string name = reader.GetString(module.Name);
            """,
            """
            string name = reader.GetString(module.Name);
                    budget.Work(SignatureOccurrenceMetric.ModuleReferenceNameBytes,
                        reader.GetBlobReader(module.Name).Length);
            """, StringComparison.Ordinal));
        Assert.NotEmpty(DominanceViolations(compilation));
    }

    [Fact]
    public void ChargingAnotherHandle_DoesNotAuthorizeModuleMaterialization()
    {
        var compilation = Compilation(source => source.Replace(
            "reader.GetBlobReader(module.Name).Length", "reader.GetBlobReader(default(StringHandle)).Length",
            StringComparison.Ordinal));
        Assert.NotEmpty(DominanceViolations(compilation));
    }

    [Fact]
    public void EveryProviderCallback_ChargesANodeFirst()
    {
        var source = Compilation().SyntaxTrees.Single(tree =>
            tree.FilePath.EndsWith("SignatureOccurrenceProvider.cs", StringComparison.Ordinal));
        var callbacks = source.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Modifiers.Any(SyntaxKind.PublicKeyword)).ToArray();
        Assert.Equal(14, callbacks.Length);
        Assert.All(callbacks, method =>
            Assert.Equal("budget.Node();", method.Body!.Statements[0].ToString()));
    }

    [Theory]
    [InlineData("constructor")]
    [InlineData("getter")]
    [InlineData("field")]
    [InlineData("array")]
    [InlineData("collection")]
    [InlineData("charge-helper")]
    public void NewlyIntroducedEffects_AreRejectedAcrossTheSourceClosure(string location)
    {
        var compilation = Compilation(source => location switch
        {
            "constructor" => source.Replace("hashCode = hash.ToHashCode();",
                "hashCode = hash.ToHashCode(); _ = @namespace.ToCharArray();", StringComparison.Ordinal),
            "getter" => source.Replace("public MetadataTypeDefinitionName Name { get; }",
                "public MetadataTypeDefinitionName Name { get { _ = new byte[16]; return field; } }",
                StringComparison.Ordinal),
            "field" => source.Replace("ISignatureTypeProvider<ImmutableArray<SignatureNamedTypeOccurrence>, object?>\n{",
                "ISignatureTypeProvider<ImmutableArray<SignatureNamedTypeOccurrence>, object?>\n{" +
                "\n    readonly string copiedName = image.GetMetadataReader().GetString(default(StringHandle));",
                StringComparison.Ordinal),
            "array" => source.Replace(
                "new SignatureOccurrenceMeasurement[Enum.GetValues<SignatureOccurrenceMetric>().Length]",
                "new SignatureOccurrenceMeasurement[1 + Enum.GetValues<SignatureOccurrenceMetric>().Length]",
                StringComparison.Ordinal),
            "collection" => source.Replace("return [];", "return [default];", StringComparison.Ordinal),
            "charge-helper" => source.Replace("ArgumentOutOfRangeException.ThrowIfNegative(amount);",
                "ArgumentOutOfRangeException.ThrowIfNegative(amount); _ = new byte[1];", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(location)),
        });
        var violations = InventoryViolations(compilation).ToArray();
        Assert.Contains(violations, violation => violation.StartsWith("Unclassified effect:", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicatingAnAlreadyClassifiedCopySite_ChangesTheInventory()
    {
        var compilation = Compilation(source => source.Replace(
            "result.AddRange(unmodifiedType);",
            "result.AddRange(unmodifiedType); result.AddRange(unmodifiedType);", StringComparison.Ordinal));
        Assert.Contains(InventoryViolations(compilation), violation =>
            violation.Contains("expected 1, found 2", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceDefinedScalarHelper_IsTraversedWithoutNeedingAnOpaqueExemption()
    {
        var compilation = Compilation(source => source.Replace(
            "MetadataTypeReferenceScope ReadModuleScope(MetadataReader reader, ModuleReferenceHandle handle)",
            "static bool NameIsEmpty(string value) => string.IsNullOrWhiteSpace(value);\n" +
            "    MetadataTypeReferenceScope ReadModuleScope(MetadataReader reader, ModuleReferenceHandle handle)",
            StringComparison.Ordinal).Replace(
            "if (string.IsNullOrWhiteSpace(name))", "if (NameIsEmpty(name))", StringComparison.Ordinal));
        Assert.Empty(InventoryViolations(compilation));
        Assert.Empty(DominanceViolations(compilation));
    }

    [Theory]
    [InlineData("culture-handle")]
    [InlineData("name-handle")]
    [InlineData("key-price")]
    [InlineData("key-flag")]
    public void ChangedHelperProvenance_FailsAtTheActualReadDespiteUnchangedEffectInventory(string mutation)
    {
        var compilation = Compilation(source => mutation switch
        {
            "culture-handle" => source.Replace("StringOrNull(reader, reference.Culture)",
                "StringOrNull(reader, reference.Name)", StringComparison.Ordinal),
            "name-handle" => source.Replace("reader.GetString(reference.Name)",
                "reader.GetString(reference.Culture)", StringComparison.Ordinal),
            "key-price" => source.Replace(
                "int keyLength = reader.GetBlobReader(reference.PublicKeyOrToken).Length;",
                "int keyLength = 8;", StringComparison.Ordinal),
            "key-flag" => source.Replace(
                "bool isPublicKey = (reference.Flags & AssemblyFlags.PublicKey) != 0;",
                "bool isPublicKey = reader.GetBlobReader(reference.PublicKeyOrToken).Length != 8;",
                StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        });
        Assert.Empty(InventoryViolations(compilation));
        Assert.Contains(DominanceViolations(compilation), violation =>
            violation.StartsWith("No dominating exact-storage charge", StringComparison.Ordinal));
    }

    [Fact]
    public void ASecondHelperEntry_CannotSpendTheSameChargeTwice()
    {
        var compilation = Compilation(source => source.Replace(
            "return new MetadataTypeReferenceScope.AssemblyReference(\n            AssemblyReferenceIdentity.Create(reader, reference, token));",
            "_ = AssemblyReferenceIdentity.Create(reader, reference, token);\n" +
            "        return new MetadataTypeReferenceScope.AssemblyReference(\n            AssemblyReferenceIdentity.Create(reader, reference, token));",
            StringComparison.Ordinal));
        Assert.Empty(InventoryViolations(compilation));
        Assert.Contains(DominanceViolations(compilation), violation =>
            violation.StartsWith("Charge reused", StringComparison.Ordinal));
    }

    [Fact]
    public void NewInitializerEntryIntoExistingMaterializer_HasNoInheritedChargeProof()
    {
        var compilation = Compilation(source => source.Replace(
            "static readonly System.Runtime.CompilerServices",
            "static readonly string? unpriced = StringOrNull(null!, default);\n" +
            "    static readonly System.Runtime.CompilerServices", StringComparison.Ordinal));
        Assert.Empty(InventoryViolations(compilation));
        Assert.Contains(DominanceViolations(compilation), violation =>
            violation.StartsWith("A new helper entry", StringComparison.Ordinal));
    }

    [Fact]
    public void LedgerMethodNameAlone_DoesNotEstablishEnforcement()
    {
        var compilation = Compilation(source => source.Replace(
            "Charge(metric, amount, ref _work, limits.Work,\n            SignatureOccurrenceRejectionReason.WorkBudget)",
            "_work += amount", StringComparison.Ordinal));
        Assert.Empty(InventoryViolations(compilation));
        Assert.Contains(DominanceViolations(compilation), violation =>
            violation.Contains("ledger kernel changed", StringComparison.Ordinal));
    }

    [Fact]
    public void ATokenWithoutItsExactLengthCap_IsNotClassA()
    {
        var compilation = Compilation(source => source.Replace(
            "reader.GetBlobReader(handle).Length != 8", "reader.GetBlobReader(handle).Length < 0",
            StringComparison.Ordinal));
        Assert.Empty(InventoryViolations(compilation));
        Assert.Contains(DominanceViolations(compilation), violation =>
            violation.Contains("exactly eight token bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void ImplicitPrimaryConstructor_FollowsItsSourceBaseConstructor()
    {
        var compilation = Compilation(source => source.Contains(
            "SignatureOccurrenceRejectionReason reason) : Exception", StringComparison.Ordinal)
            ? source.Replace("SignatureOccurrenceRejectionReason reason) : Exception",
                "SignatureOccurrenceRejectionReason reason) : OccurrenceAuditException", StringComparison.Ordinal)
                + "\ninternal class OccurrenceAuditException : Exception " +
                "{ protected OccurrenceAuditException() { _ = new byte[1]; } }\n"
            : source);
        Assert.Contains(InventoryViolations(compilation), violation =>
            violation.Contains("OccurrenceAuditException..ctor | ArrayCreation", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("boxing")]
    [InlineData("array-slice")]
    [InlineData("goto")]
    public void ImplicitAllocationsAndNewRepetitionScopes_AreNotScalarByOmission(string change)
    {
        var compilation = Compilation(source => source.Replace(
            "hashCode = hash.ToHashCode();",
            "hashCode = hash.ToHashCode(); " + (change switch
            {
                "boxing" => "_ = (object)hash;",
                "array-slice" => "var bytes = new byte[1]; _ = bytes[..];",
                "goto" => "Repeat: if (@namespace.Length == 0) goto Repeat;",
                _ => throw new ArgumentOutOfRangeException(nameof(change)),
            }), StringComparison.Ordinal));
        var violations = InventoryViolations(compilation).ToArray();
        Assert.Contains(violations, violation => violation.StartsWith("Unclassified effect:", StringComparison.Ordinal)
            && violation.Contains(change switch
            {
                "boxing" => "Conversion",
                "array-slice" => "ArrayElementReference",
                _ => "Branch",
            }, StringComparison.Ordinal));
    }

    static IEnumerable<string> InventoryViolations(CSharpCompilation compilation)
    {
        var audit = new SignatureOccurrenceSourceAudit(compilation, ClassifyEffect);
        var violations = audit.Run().ToList();
        var counts = audit.Effects.GroupBy(SignatureOccurrenceSourceAudit.Site)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var (site, contract) in Materializations)
        {
            if (counts.GetValueOrDefault(site) != contract.Count)
                violations.Add($"Materialization inventory changed: expected {contract.Count}, " +
                    $"found {counts.GetValueOrDefault(site)}: {site}");
        }
        return violations;
    }

    static string? ClassifyEffect(IOperation operation)
    {
        if (operation is IWithOperation { Type.IsReferenceType: true })
            return null;
        if (operation is IImplicitIndexerReferenceOperation { IndexerSymbol: IMethodSymbol slice }
            && slice.ContainingType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>"
            && slice.Name == "Slice")
            return nameof(Cost.Scalar);
        if (Materializations.TryGetValue(SignatureOccurrenceSourceAudit.Site(operation), out var materialization))
            return materialization.Class.ToString();
        if (operation is IInvocationOperation invocation)
        {
            Cost? cost = Calls.TryGetValue(Key(invocation.TargetMethod), out Cost imported) ? imported : null;
            return cost is Cost.Scalar or Cost.ImportedGuard or Cost.Budget ? cost.ToString() : null;
        }
        if (operation is IPropertyReferenceOperation property
            && ScalarProperties.Contains($"{property.Property.OriginalDefinition.ContainingType.ToDisplayString()}.{property.Property.Name}"))
            return nameof(Cost.Scalar);
        if (operation is IFieldReferenceOperation field && field.Field.Name == "Empty"
            && field.Field.ContainingType.SpecialType == SpecialType.System_String)
            return nameof(Cost.Scalar);
        if (operation is IFieldReferenceOperation measurement
            && measurement.Field.ContainingType.ToDisplayString()
                == "ILInspector.MetadataPrimitives.SignatureBlobGuardMeasurements"
            && measurement.Field.Name is "Sizes" or "LowerBounds")
            return nameof(Cost.Scalar);
        if (operation is IConversionOperation { OperatorMethod.ContainingType.Name: "Index" })
            return nameof(Cost.Scalar);
        if (operation is IConversionOperation conversion && conversion.OperatorMethod is { } method
            && method.ContainingNamespace.ToDisplayString() == "System.Reflection.Metadata"
            && method.ContainingType.Name is "EntityHandle" or "TypeDefinitionHandle"
                or "TypeReferenceHandle" or "TypeSpecificationHandle" or "AssemblyReferenceHandle"
                or "ModuleReferenceHandle" or "MethodDefinitionHandle" or "FieldDefinitionHandle"
                or "PropertyDefinitionHandle" or "ExportedTypeHandle")
            return nameof(Cost.Scalar);
        return null;
    }

    static IEnumerable<string> DominanceViolations(CSharpCompilation compilation)
    {
        var audit = new SignatureOccurrenceSourceAudit(compilation, ClassifyEffect);
        audit.Run();
        return new SignatureOccurrenceChargeAudit(compilation, audit.Bodies, audit.Calls).Run();
    }

    static string Key(IMethodSymbol method) =>
        $"{method.OriginalDefinition.ContainingType.ToDisplayString()}.{method.Name}";

    static CSharpCompilation Compilation(Func<string, string>? mutate = null)
    {
        string root = RepoRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src/ILInspector.Metadata"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var trees = files.Select(file => CSharpSyntaxTree.ParseText(
            mutate?.Invoke(File.ReadAllText(file)) ?? File.ReadAllText(file),
            new CSharpParseOptions(LanguageVersion.Preview),
            path: file)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText(
            "global using System; global using System.Collections.Generic; global using System.Linq;",
            new CSharpParseOptions(LanguageVersion.Preview)));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Concat(new[]
            {
                typeof(SignatureOccurrenceDecoder).Assembly.Location,
                typeof(SignatureBlobGuard).Assembly.Location,
            })
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create(
            "ILInspector.Metadata", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
