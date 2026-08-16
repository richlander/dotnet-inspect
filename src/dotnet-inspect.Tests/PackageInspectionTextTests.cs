using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotnetInspector.Models;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;
using InertText;

namespace DotnetInspector.Tests;

public class PackageInspectionTextTests
{
    private const string Hazard = "HOSTILE\u202EMARKER";
    private const string Benign = "ordinary text";
    private static readonly IReadOnlyDictionary<Type, Type> PresentationMappings =
        new Dictionary<Type, Type>
        {
            [typeof(InspectionResult)] = typeof(PackageInspectionText),
            [typeof(PackageDeprecation)] = typeof(PackageDeprecationText),
            [typeof(PackageVulnerability)] = typeof(PackageVulnerabilityText),
            [typeof(RidPackageReference)] = typeof(RidPackageReferenceText),
            [typeof(DependencyGroup)] = typeof(PackageDependencyGroupText),
            [typeof(PackageDependency)] = typeof(PackageDependencyText),
            [typeof(PackageFile)] = typeof(PackageFileText),
            [typeof(PackageSourceFileInfo)] = typeof(PackageSourceFileText),
            [typeof(PackageSourceLinkIssue)] = typeof(PackageSourceLinkIssueText),
            [typeof(PackageSourceLinkFile)] = typeof(PackageSourceLinkFileText),
            [typeof(PackageSourceAvailability)] = typeof(PackageSourceAvailabilityText),
            [typeof(PackageSourceIntegrity)] = typeof(PackageSourceIntegrityText),
            [typeof(SignatureVerificationResult)] = typeof(PackageSignatureText),
            [typeof(AuditSignal)] = typeof(PackageAuditSignalText),
        };
    private static readonly HashSet<string> NonPresentedText =
    [
        $"{typeof(RidPackageReference).FullName}.{nameof(RidPackageReference.AvailableDisplay)}",
        $"{typeof(InspectionResult).FullName}.{nameof(InspectionResult.Tfm)}",
    ];

    [Fact]
    public void PresentationProjection_CoversEveryPackageModelTextProperty()
    {
        // AvailableDisplay is fixed tool text. Tfm is a selection helper ignored by
        // JSON; the rendered Highest TFM comes from the contained TargetFrameworks.
        foreach ((Type modelType, Type textType) in PresentationMappings)
        {
            foreach (PropertyInfo modelProperty in modelType.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                Type? expectedCurrency = CurrencyType(
                    modelProperty.PropertyType,
                    PresentationMappings);
                if (expectedCurrency is null
                    || NonPresentedText.Contains($"{modelType.FullName}.{modelProperty.Name}"))
                {
                    continue;
                }

                PropertyInfo? textProperty = textType.GetProperty(
                    modelProperty.Name,
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.True(
                    textProperty is not null,
                    $"{modelType.Name}.{modelProperty.Name} has no presentation currency.");
                Assert.Equal(expectedCurrency, NormalizeNullableCurrency(textProperty!.PropertyType));
            }
        }
    }

    [Fact]
    public void RequiredContainment_CoversEveryPackageTextSourceIndividually()
    {
        Dictionary<string, Action<InspectionResult>> cases = new()
        {
            [nameof(InspectionResult.PackageName)] = result => result.PackageName = Hazard,
            [nameof(InspectionResult.ManifestVersion)] = result => result.ManifestVersion = Hazard,
            [nameof(InspectionResult.Version)] = result => result.Version = Hazard,
            [nameof(InspectionResult.Source)] = result => result.Source = Hazard,
            [nameof(InspectionResult.Description)] = result =>
                result.Description = new InertString(TextPolicy.Prose, Hazard),
            [nameof(InspectionResult.Authors)] = result => result.Authors = Hazard,
            [nameof(InspectionResult.License)] = result => result.License = Hazard,
            [nameof(InspectionResult.LicenseUrl)] = result => result.LicenseUrl = Hazard,
            [nameof(InspectionResult.Repository)] = result => result.Repository = Hazard,
            [nameof(InspectionResult.RepositoryType)] = result => result.RepositoryType = Hazard,
            [nameof(InspectionResult.RepositoryCommit)] = result => result.RepositoryCommit = Hazard,
            [nameof(InspectionResult.Owners)] = result => result.Owners![0] = Hazard,
            [$"{nameof(InspectionResult.Deprecation)}.{nameof(PackageDeprecation.Reasons)}"] =
                result => result.Deprecation!.Reasons![0] = Hazard,
            [$"{nameof(InspectionResult.Deprecation)}.{nameof(PackageDeprecation.Message)}"] =
                result => result.Deprecation!.Message = Hazard,
            [$"{nameof(InspectionResult.Deprecation)}.{nameof(PackageDeprecation.AlternatePackageId)}"] =
                result => result.Deprecation!.AlternatePackageId = Hazard,
            [$"{nameof(InspectionResult.Vulnerabilities)}[].{nameof(PackageVulnerability.Severity)}"] =
                result => result.Vulnerabilities![0].Severity = Hazard,
            [$"{nameof(InspectionResult.Vulnerabilities)}[].{nameof(PackageVulnerability.CveId)}"] =
                result => result.Vulnerabilities![0].CveId = Hazard,
            [$"{nameof(InspectionResult.Vulnerabilities)}[].{nameof(PackageVulnerability.Summary)}"] =
                result => result.Vulnerabilities![0].Summary = Hazard,
            [$"{nameof(InspectionResult.Vulnerabilities)}[].{nameof(PackageVulnerability.AdvisoryUrl)}"] =
                result => result.Vulnerabilities![0].AdvisoryUrl = Hazard,
            [$"{nameof(InspectionResult.Vulnerabilities)}[].{nameof(PackageVulnerability.GhsaId)}"] =
                result => result.Vulnerabilities![0].GhsaId = Hazard,
            [nameof(InspectionResult.ReadmeFile)] = result => result.ReadmeFile = Hazard,
            [nameof(InspectionResult.PackageReadmeFile)] = result => result.PackageReadmeFile = Hazard,
            [nameof(InspectionResult.PackageTypes)] = result => result.PackageTypes![0] = Hazard,
            [nameof(InspectionResult.ContentDirectories)] = result => result.ContentDirectories![0] = Hazard,
            [nameof(InspectionResult.TargetFrameworks)] = result => result.TargetFrameworks![0] = Hazard,
            [nameof(InspectionResult.SupportedRids)] = result => result.SupportedRids![0] = Hazard,
            [nameof(InspectionResult.ToolFormat)] = result => result.ToolFormat = Hazard,
            [nameof(InspectionResult.ToolCommands)] = result => result.ToolCommands![0] = Hazard,
            [$"{nameof(InspectionResult.RuntimeIdentifierPackages)}[].{nameof(RidPackageReference.RuntimeIdentifier)}"] =
                result => result.RuntimeIdentifierPackages![0].RuntimeIdentifier = Hazard,
            [$"{nameof(InspectionResult.RuntimeIdentifierPackages)}[].{nameof(RidPackageReference.PackageId)}"] =
                result => result.RuntimeIdentifierPackages![0].PackageId = Hazard,
            [nameof(InspectionResult.RuntimeTargetRid)] = result => result.RuntimeTargetRid = Hazard,
            [nameof(InspectionResult.NativeFiles)] = result => result.NativeFiles![0] = Hazard,
            [nameof(InspectionResult.LibraryFiles)] = result => result.LibraryFiles![0] = Hazard,
            [$"{nameof(InspectionResult.DependencyGroups)}[].{nameof(DependencyGroup.TargetFramework)}"] =
                result => result.DependencyGroups![0].TargetFramework = Hazard,
            [$"{nameof(InspectionResult.DependencyGroups)}[].{nameof(DependencyGroup.Dependencies)}[].{nameof(PackageDependency.Id)}"] =
                result => result.DependencyGroups![0].Dependencies[0].Id = Hazard,
            [$"{nameof(InspectionResult.DependencyGroups)}[].{nameof(DependencyGroup.Dependencies)}[].{nameof(PackageDependency.Version)}"] =
                result => result.DependencyGroups![0].Dependencies[0].Version = Hazard,
            [$"{nameof(InspectionResult.RuntimeDependencies)}[].{nameof(PackageDependency.Id)}"] =
                result => result.RuntimeDependencies![0].Id = Hazard,
            [$"{nameof(InspectionResult.RuntimeDependencies)}[].{nameof(PackageDependency.Version)}"] =
                result => result.RuntimeDependencies![0].Version = Hazard,
            [$"{nameof(InspectionResult.Files)}[].{nameof(PackageFile.Path)}"] =
                result => result.Files![0] = result.Files[0] with { Path = Hazard },
            [$"{nameof(InspectionResult.PackageFiles)}[].{nameof(PackageFile.Path)}"] =
                result => result.PackageFiles![0] = result.PackageFiles[0] with { Path = Hazard },
            [$"{nameof(InspectionResult.SourceFiles)}[].{nameof(PackageSourceFileInfo.Library)}"] =
                result => result.SourceFiles![0] = result.SourceFiles[0] with { Library = Hazard },
            [$"{nameof(InspectionResult.SourceFiles)}[].{nameof(PackageSourceFileInfo.Type)}"] =
                result => result.SourceFiles![0] = result.SourceFiles[0] with { Type = Hazard },
            [$"{nameof(InspectionResult.SourceFiles)}[].{nameof(PackageSourceFileInfo.Url)}"] =
                result => result.SourceFiles![0] = result.SourceFiles[0] with { Url = Hazard },
            [$"{nameof(InspectionResult.SourceAvailability)}.{nameof(PackageSourceAvailability.MissingFiles)}[].{nameof(PackageSourceLinkFile.Library)}"] =
                result => result.SourceAvailability!.MissingFiles![0] =
                    result.SourceAvailability.MissingFiles[0] with { Library = Hazard },
            [$"{nameof(InspectionResult.SourceAvailability)}.{nameof(PackageSourceAvailability.MissingFiles)}[].{nameof(PackageSourceLinkFile.Path)}"] =
                result => result.SourceAvailability!.MissingFiles![0] =
                    result.SourceAvailability.MissingFiles[0] with { Path = Hazard },
            [$"{nameof(InspectionResult.SourceAvailability)}.{nameof(PackageSourceAvailability.UnavailableLibraries)}[].{nameof(PackageSourceLinkIssue.Library)}"] =
                result => result.SourceAvailability!.UnavailableLibraries![0] =
                    result.SourceAvailability.UnavailableLibraries[0] with { Library = Hazard },
            [$"{nameof(InspectionResult.SourceAvailability)}.{nameof(PackageSourceAvailability.UnavailableLibraries)}[].{nameof(PackageSourceLinkIssue.Reason)}"] =
                result => result.SourceAvailability!.UnavailableLibraries![0] =
                    result.SourceAvailability.UnavailableLibraries[0] with { Reason = Hazard },
            [$"{nameof(InspectionResult.SourceAvailability)}.{nameof(PackageSourceAvailability.FailedLibraries)}[].{nameof(PackageSourceLinkIssue.Library)}"] =
                result => result.SourceAvailability!.FailedLibraries![0] =
                    result.SourceAvailability.FailedLibraries[0] with { Library = Hazard },
            [$"{nameof(InspectionResult.SourceAvailability)}.{nameof(PackageSourceAvailability.FailedLibraries)}[].{nameof(PackageSourceLinkIssue.Reason)}"] =
                result => result.SourceAvailability!.FailedLibraries![0] =
                    result.SourceAvailability.FailedLibraries[0] with { Reason = Hazard },
            [$"{nameof(InspectionResult.SourceIntegrity)}.{nameof(PackageSourceIntegrity.MismatchedFiles)}[].{nameof(PackageSourceLinkFile.Library)}"] =
                result => result.SourceIntegrity!.MismatchedFiles![0] =
                    result.SourceIntegrity.MismatchedFiles[0] with { Library = Hazard },
            [$"{nameof(InspectionResult.SourceIntegrity)}.{nameof(PackageSourceIntegrity.MismatchedFiles)}[].{nameof(PackageSourceLinkFile.Path)}"] =
                result => result.SourceIntegrity!.MismatchedFiles![0] =
                    result.SourceIntegrity.MismatchedFiles[0] with { Path = Hazard },
            [$"{nameof(InspectionResult.SourceIntegrity)}.{nameof(PackageSourceIntegrity.UnavailableLibraries)}[].{nameof(PackageSourceLinkIssue.Library)}"] =
                result => result.SourceIntegrity!.UnavailableLibraries![0] =
                    result.SourceIntegrity.UnavailableLibraries[0] with { Library = Hazard },
            [$"{nameof(InspectionResult.SourceIntegrity)}.{nameof(PackageSourceIntegrity.UnavailableLibraries)}[].{nameof(PackageSourceLinkIssue.Reason)}"] =
                result => result.SourceIntegrity!.UnavailableLibraries![0] =
                    result.SourceIntegrity.UnavailableLibraries[0] with { Reason = Hazard },
            [$"{nameof(InspectionResult.SourceIntegrity)}.{nameof(PackageSourceIntegrity.FailedLibraries)}[].{nameof(PackageSourceLinkIssue.Library)}"] =
                result => result.SourceIntegrity!.FailedLibraries![0] =
                    result.SourceIntegrity.FailedLibraries[0] with { Library = Hazard },
            [$"{nameof(InspectionResult.SourceIntegrity)}.{nameof(PackageSourceIntegrity.FailedLibraries)}[].{nameof(PackageSourceLinkIssue.Reason)}"] =
                result => result.SourceIntegrity!.FailedLibraries![0] =
                    result.SourceIntegrity.FailedLibraries[0] with { Reason = Hazard },
            [$"{nameof(InspectionResult.SignatureResult)}.{nameof(SignatureVerificationResult.Publisher)}"] =
                result => result.SignatureResult = result.SignatureResult! with { Publisher = Hazard },
            [$"{nameof(InspectionResult.SignatureResult)}.{nameof(SignatureVerificationResult.Repository)}"] =
                result => result.SignatureResult = result.SignatureResult! with { Repository = Hazard },
            [$"{nameof(InspectionResult.SignatureResult)}.{nameof(SignatureVerificationResult.StatusMessage)}"] =
                result => result.SignatureResult = result.SignatureResult! with { StatusMessage = Hazard },
            [$"{nameof(InspectionResult.AuditSignals)}[].{nameof(AuditSignal.Area)}"] =
                result => result.AuditSignals![0] = result.AuditSignals[0] with { Area = Hazard },
            [$"{nameof(InspectionResult.AuditSignals)}[].{nameof(AuditSignal.Signal)}"] =
                result => result.AuditSignals![0] = result.AuditSignals[0] with { Signal = Hazard },
            [$"{nameof(InspectionResult.AuditSignals)}[].{nameof(AuditSignal.Value)}"] =
                result => result.AuditSignals![0] = result.AuditSignals[0] with { Value = Hazard },
            [$"{nameof(InspectionResult.AuditSignals)}[].{nameof(AuditSignal.Evidence)}"] =
                result => result.AuditSignals![0] = result.AuditSignals[0] with { Evidence = Hazard },
        };
        string[] expected = EnumeratePresentationSourcePaths(
                typeof(InspectionResult),
                prefix: "")
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actual = cases.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);

        foreach ((string path, Action<InspectionResult> makeHostile) in cases)
        {
            InspectionResult result = CompleteResult(Benign);
            makeHostile(result);
            var text = new PackageInspectionText(result);

            Assert.True(
                text.RequiredContainment,
                $"{path} did not contribute to RequiredContainment.");
            Assert.Equal(TextConcern.Format, text.Concerns);
            PackageTextConcernCase concernCase = Assert.Single(text.ConcernCases);
            string expectedLocation = path.Replace("[]", "[0]", StringComparison.Ordinal);
            Type? propertyType = typeof(InspectionResult)
                .GetProperty(expectedLocation)?
                .PropertyType;
            if (!expectedLocation.Contains('[')
                && ((propertyType?.IsGenericType == true
                        && propertyType.GetGenericTypeDefinition() == typeof(List<>))
                    || expectedLocation == $"{nameof(InspectionResult.Deprecation)}."
                        + nameof(PackageDeprecation.Reasons)))
            {
                expectedLocation += "[0]";
            }
            Assert.Equal(expectedLocation, concernCase.Location);
            Assert.Equal(TextConcern.Format, concernCase.Concerns);
        }
    }

    [Theory]
    [InlineData("ordinary text")]
    [InlineData("C:\\tmp\\package")]
    [InlineData("literal \\u202E text")]
    public void BackslashesDoNotContributeAPackageTextConcern(string value)
    {
        PackageInspectionText text = new(CompleteResult(value));

        Assert.Equal(TextConcern.None, text.Concerns);
        Assert.False(text.RequiredContainment);
        Assert.Empty(text.ConcernCases);
    }

    [Fact]
    public void ConcernCases_ListLocationsAndKindsWithoutArtifactContent()
    {
        const string secret = "DO-NOT-REPORT";
        var result = new InspectionResult
        {
            PackageName = $"package\u001B{secret}",
            Owners = [$"owner\u202E{secret}"],
            PackageFiles = [new PackageFile($"file\u2028{secret}", 42)],
        };

        var text = new PackageInspectionText(result);

        Assert.Equal(
            new[]
            {
                new PackageTextConcernCase("PackageName", TextConcern.Control),
                new PackageTextConcernCase("Owners[0]", TextConcern.Format),
                new PackageTextConcernCase("PackageFiles[0].Path", TextConcern.LineSeparator),
            },
            text.ConcernCases);
        Assert.All(
            text.ConcernCases,
            value => Assert.DoesNotContain(secret, value.Location, StringComparison.Ordinal));
    }

    [Fact]
    public void JsonProjection_ContainsEveryArtifactTextScalar()
    {
        InspectionResult result = CompleteResult(Hazard);
        PackageInspectionJson projection = PackageInspectionJson.Create(result);

        Assert.True(projection.RequiredContainment);

        string json = JsonSerializer.Serialize(
            projection,
            PackageInspectionJsonContext.Default.PackageInspectionJson);
        using JsonDocument document = JsonDocument.Parse(json);
        List<string> values = EnumerateStrings(document.RootElement).ToList();

        Assert.True(values.Count(value => value.Contains("HOSTILE", StringComparison.Ordinal)) >= 40);
        Assert.All(values, value => Assert.DoesNotContain("\u202E", value, StringComparison.Ordinal));
        Assert.DoesNotContain("required_containment", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonProjection_PreservesTheBenignInspectionResultContract()
    {
        InspectionResult result = CompleteResult("ordinary text");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        string previous = JsonSerializer.Serialize(result, options);
        string projected = JsonSerializer.Serialize(
            PackageInspectionJson.Create(result),
            PackageInspectionJsonContext.Default.PackageInspectionJson);
        JsonNode previousNode = JsonNode.Parse(previous)!;
        JsonNode projectedNode = JsonNode.Parse(projected)!;
        foreach (JsonNode? package in projectedNode[
            "runtime_identifier_packages"]!.AsArray())
        {
            Assert.True(package!.AsObject().Remove("available"));
        }

        Assert.True(JsonNode.DeepEquals(
            previousNode,
            projectedNode));
    }

    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    [InlineData(null, "unknown")]
    public void JsonProjection_PreservesRidPackageAvailability(
        bool? exists,
        string expected)
    {
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "linux-x64",
                    PackageId = "Example.Package.linux-x64",
                    Exists = exists,
                },
            ],
        };

        string json = JsonSerializer.Serialize(
            PackageInspectionJson.Create(result),
            PackageInspectionJsonContext.Default.PackageInspectionJson);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement package = document.RootElement
            .GetProperty("runtime_identifier_packages")[0];

        Assert.Equal(expected, package.GetProperty("available").GetString());
    }

    [Fact]
    public void BenignPresentationText_RetainsOriginalStrings()
    {
        string package = new("Example.Package".ToCharArray());
        string author = new("Example Author".ToCharArray());
        string path = new("lib/net10.0/Example.dll".ToCharArray());
        var result = new InspectionResult
        {
            PackageName = package,
            Authors = author,
            Files = [new PackageFile(path, 42)],
        };

        var text = new PackageInspectionText(result);

        Assert.Same(package, text.PackageName.ToString());
        Assert.Same(author, text.Authors?.ToString());
        Assert.Same(path, Assert.Single(text.Files!).Path.ToString());
        Assert.False(text.RequiredContainment);
    }

    [Fact]
    public void PackageFileText_IsLazyUntilTheAggregateRequiresIt()
    {
        var result = new InspectionResult
        {
            PackageFiles = [new PackageFile(Hazard, 42)],
        };
        var text = new PackageInspectionText(result);
        FieldInfo projectedFiles = typeof(PackageInspectionText).GetField(
            "_packageFiles",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Null(projectedFiles.GetValue(text));
        _ = text.PackageName;
        Assert.Null(projectedFiles.GetValue(text));

        Assert.True(text.RequiredContainment);
        Assert.NotNull(projectedFiles.GetValue(text));
    }

    [Fact]
    public void PackageFileFamily_ProjectsOnlySelectedRows()
    {
        var result = new InspectionResult
        {
            PackageFiles =
            [
                new PackageFile("Example.nuspec", 42),
                new PackageFile("lib/net10.0/Example.dll", 84),
            ],
        };
        var view = new InspectionResultView(result);
        FieldInfo textField = typeof(InspectionResultView).GetField(
            "_text",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        PackageFileRow row = Assert.Single(view.NuspecFiles!);
        var text = Assert.IsType<PackageInspectionText>(textField.GetValue(view));
        FieldInfo projectedFiles = typeof(PackageInspectionText).GetField(
            "_packageFiles",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.Equal("Example.nuspec", row.Path);
        Assert.Null(projectedFiles.GetValue(text));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void DeprecationSummary_EmptyOptionalTextPreservesNoDetailsBehavior(
        string? message,
        string? alternatePackageId)
    {
        var result = new InspectionResult
        {
            Deprecation = new PackageDeprecation
            {
                Message = message,
                AlternatePackageId = alternatePackageId,
            },
        };

        var text = new PackageInspectionText(result);

        Assert.Equal("Deprecated", text.Deprecation?.Summary.ToString());
    }

    [Fact]
    public void DeprecationSummary_EmptyMessageDoesNotAddASeparator()
    {
        var result = new InspectionResult
        {
            Deprecation = new PackageDeprecation
            {
                Reasons = ["Legacy"],
                Message = "",
            },
        };

        var text = new PackageInspectionText(result);

        Assert.Equal("Legacy", text.Deprecation?.Summary.ToString());
    }

    [Fact]
    public void SigningSection_EmptyPublisherRemainsAbsent()
    {
        var result = new InspectionResult
        {
            SignatureResult = new SignatureVerificationResult
            {
                AuthorVerified = true,
                Publisher = "",
            },
        };

        var signing = new InspectionResultView(result).SigningSectionData;

        Assert.NotNull(signing);
        Assert.Null(signing.Publisher);
    }

    private static Type? CurrencyType(Type modelType, IReadOnlyDictionary<Type, Type> mappings)
    {
        if (modelType == typeof(string))
            return typeof(InertString);
        if (modelType == typeof(InertString) || modelType == typeof(InertString?))
            return typeof(InertString);
        if (modelType == typeof(List<string>))
            return typeof(List<InertString>);
        if (mappings.TryGetValue(modelType, out Type? mapped))
            return mapped;
        if (modelType.IsGenericType && modelType.GetGenericTypeDefinition() == typeof(List<>)
            && mappings.TryGetValue(modelType.GetGenericArguments()[0], out Type? mappedElement))
        {
            return typeof(List<>).MakeGenericType(mappedElement);
        }

        return null;
    }

    private static Type NormalizeNullableCurrency(Type type)
        => Nullable.GetUnderlyingType(type) ?? type;

    private static IEnumerable<string> EnumeratePresentationSourcePaths(
        Type modelType,
        string prefix)
    {
        foreach (PropertyInfo property in modelType.GetProperties(
            BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.SetMethod is null
                || NonPresentedText.Contains($"{modelType.FullName}.{property.Name}"))
            {
                continue;
            }

            Type propertyType = property.PropertyType;
            string path = prefix + property.Name;
            if (CurrencyType(propertyType, PresentationMappings) is { } currency
                && (currency == typeof(InertString) || currency == typeof(List<InertString>)))
            {
                yield return path;
                continue;
            }

            if (PresentationMappings.ContainsKey(propertyType))
            {
                foreach (string nested in EnumeratePresentationSourcePaths(
                    propertyType,
                    path + "."))
                {
                    yield return nested;
                }
                continue;
            }

            if (propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(List<>)
                && PresentationMappings.ContainsKey(propertyType.GetGenericArguments()[0]))
            {
                foreach (string nested in EnumeratePresentationSourcePaths(
                    propertyType.GetGenericArguments()[0],
                    path + "[]."))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string value in EnumerateStrings(item))
                        yield return value;
                }
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    foreach (string value in EnumerateStrings(property.Value))
                        yield return value;
                }
                break;
        }
    }

    internal static InspectionResult CompleteResult(string value)
        => new()
        {
            PackageName = value,
            ManifestVersion = value,
            Version = value,
            Source = value,
            Description = new InertString(TextPolicy.Prose, value),
            Authors = value,
            License = value,
            LicenseUrl = value,
            Repository = value,
            RepositoryType = value,
            RepositoryCommit = value,
            Owners = [value],
            Deprecation = new PackageDeprecation
            {
                Reasons = [value],
                Message = value,
                AlternatePackageId = value,
            },
            Vulnerabilities =
            [
                new PackageVulnerability
                {
                    Severity = value,
                    CveId = value,
                    Summary = value,
                    AdvisoryUrl = value,
                    GhsaId = value,
                },
            ],
            ReadmeFile = value,
            PackageReadmeFile = value,
            PackageTypes = [value],
            ContentDirectories = [value],
            TargetFrameworks = [value],
            Tfm = value,
            SupportedRids = [value],
            ToolFormat = value,
            ToolCommands = [value],
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = value,
                    PackageId = value,
                },
            ],
            RuntimeTargetRid = value,
            NativeFiles = [value],
            LibraryFiles = [value],
            DependencyGroups =
            [
                new DependencyGroup
                {
                    TargetFramework = value,
                    Dependencies = [new PackageDependency { Id = value, Version = value }],
                    IsImplicitManifestGroup = true,
                },
            ],
            RuntimeDependencies = [new PackageDependency { Id = value, Version = value }],
            Files = [new PackageFile(value, 42)],
            PackageFiles = [new PackageFile(value, 42)],
            SourceFiles = [new PackageSourceFileInfo(value, value, value)],
            SourceAvailability = new PackageSourceAvailability(
                1,
                1,
                1,
                1,
                0,
                [new PackageSourceLinkFile(value, value)],
                [new PackageSourceLinkIssue(value, value)],
                [new PackageSourceLinkIssue(value, value)]),
            SourceIntegrity = new PackageSourceIntegrity(
                1,
                1,
                1,
                1,
                0,
                0,
                [new PackageSourceLinkFile(value, value)],
                [new PackageSourceLinkIssue(value, value)],
                [new PackageSourceLinkIssue(value, value)]),
            SignatureResult = new SignatureVerificationResult
            {
                Publisher = value,
                Repository = value,
                StatusMessage = value,
            },
            AuditSignals = [new AuditSignal(value, value, value, value)],
        };
}
