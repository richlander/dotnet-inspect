using System.Text;
using InertText;

namespace DotnetInspector.Queries.Tests;

public sealed class AuthoredProjectDependencyFactsQueryTests
{
    [Fact]
    public void Execute_ProjectsLiteralSingleAndMultiTargetSyntax()
    {
        AuthoredProjectDependencyFacts facts = Available(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks> net8.0 ; net9.0 </TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Example.Common" Version="[1.0, 2.0)" />
              </ItemGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
                <PackageReference Include="Example.Specific">
                  <Version>3.0.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(
            ["net8.0", "net9.0"],
            facts.TargetFrameworks
                .Select(target => target.Identity.CanonicalFramework));
        Assert.Equal(2, facts.PackageDeclarations.Length);

        AuthoredProjectPackageDeclaration common =
            facts.PackageDeclarations.Single(
                declaration =>
                    declaration.CanonicalPackageId == "example.common");
        Assert.Equal("[1.0.0, 2.0.0)", common.CanonicalVersionConstraint);
        Assert.IsType<AuthoredProjectDependencyCondition.Unconditional>(
            common.Condition);

        AuthoredProjectPackageDeclaration specific =
            facts.PackageDeclarations.Single(
                declaration =>
                    declaration.CanonicalPackageId == "example.specific");
        Assert.Equal("[3.0.0, )", specific.CanonicalVersionConstraint);
        var condition = Assert.IsType<
            AuthoredProjectDependencyCondition.TargetFramework>(
                specific.Condition);
        Assert.Equal("net9.0", condition.Framework.CanonicalFramework);
    }

    [Fact]
    public void Execute_ProjectsAttributeAndElementVersionForms()
    {
        AuthoredProjectDependencyFacts attribute = Available(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package" Version="1.0" />
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFacts element = Available(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version>1.0.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(attribute.Identity, element.Identity);
        Assert.NotEqual(
            attribute.ContentProvenance,
            element.ContentProvenance);
        Assert.Equal(
            attribute.PackageDeclarations.Single().CanonicalVersionConstraint,
            element.PackageDeclarations.Single().CanonicalVersionConstraint);
    }

    [Fact]
    public void Execute_ValidEmptyProjectIsComplete()
    {
        AuthoredProjectDependencyFacts facts = Available(
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        Assert.Empty(facts.TargetFrameworks);
        Assert.Empty(facts.PackageDeclarations);
    }

    [Fact]
    public void Execute_SupportedConditionAcceptsOperandOrderAndQuoteStyle()
    {
        AuthoredProjectDependencyFacts facts = Available(
            """
            <Project>
              <ItemGroup Condition="&quot;net9.0&quot; == &quot;$(TargetFramework)&quot;">
                <PackageReference Include="Example.Package"
                                  Version="1.0"
                                  Condition="'$(TargetFramework)' == 'net9.0'" />
              </ItemGroup>
            </Project>
            """);

        var condition = Assert.IsType<
            AuthoredProjectDependencyCondition.TargetFramework>(
                facts.PackageDeclarations.Single().Condition);
        Assert.Equal("net9.0", condition.Framework.CanonicalFramework);
    }

    [Fact]
    public void Execute_UnrecognizedLiteralFrameworkRemainsComplete()
    {
        AuthoredProjectDependencyFacts facts = Available(
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>custom-target</TargetFramework>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)' == 'custom-target'">
                <PackageReference Include="Example.Package" Version="1.0" />
              </ItemGroup>
            </Project>
            """);

        AuthoredProjectTargetFramework target =
            Assert.Single(facts.TargetFrameworks);
        Assert.Equal(
            AuthoredProjectTargetFrameworkKind.Unrecognized,
            target.Identity.Kind);
        Assert.Null(target.Identity.CanonicalFramework);
        var condition = Assert.IsType<
            AuthoredProjectDependencyCondition.TargetFramework>(
                facts.PackageDeclarations.Single().Condition);
        Assert.Equal(target.Identity, condition.Framework);
    }

    [Fact]
    public void Execute_ForeignNamespaceCannotBecomeProjectSyntax()
    {
        AuthoredProjectDependencyFacts facts = Available(
            """
            <Project xmlns:x="https://example.test/not-msbuild">
              <x:PropertyGroup>
                <x:TargetFramework>net9.0</x:TargetFramework>
              </x:PropertyGroup>
              <x:ItemGroup>
                <x:PackageReference Include="Example.Package" Version="1.0" />
              </x:ItemGroup>
            </Project>
            """);

        Assert.Empty(facts.TargetFrameworks);
        Assert.Empty(facts.PackageDeclarations);
    }

    [Fact]
    public void Execute_ConditionedVersionRemainsUnresolved()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version Condition="'$(Configuration)' == 'Release'">1.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        AuthoredProjectPackageDeclaration declaration =
            Assert.Single(result.Value.PackageDeclarations);
        Assert.Null(declaration.CanonicalVersionConstraint);
        Assert.Equal(
            "1.0",
            declaration.SourceVersionConstraintSpelling?.ToString());
        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason.UnsupportedCondition);
    }

    [Theory]
    [InlineData("Version=\"\"")]
    [InlineData("<Version />")]
    public void Execute_EmptyVersionIsIncomplete(string versionSyntax)
    {
        string packageReference = versionSyntax[0] == '<'
            ? $"<PackageReference Include=\"Example.Package\">{versionSyntax}</PackageReference>"
            : $"<PackageReference Include=\"Example.Package\" {versionSyntax} />";
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            $$"""
            <Project>
              <ItemGroup>
                {{packageReference}}
              </ItemGroup>
            </Project>
            """);

        AuthoredProjectPackageDeclaration declaration =
            Assert.Single(result.Value.PackageDeclarations);
        Assert.Null(declaration.CanonicalVersionConstraint);
        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason
                .InvalidVersionConstraint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Execute_EmptyVersionConditionIsUnconditional(string condition)
    {
        AuthoredProjectDependencyFacts facts = Available(
            $$"""
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version Condition="{{condition}}">1.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(
            "[1.0.0, )",
            facts.PackageDeclarations.Single()
                .CanonicalVersionConstraint);
    }

    [Fact]
    public void Execute_AmbiguousSemanticAttributesRemainUnresolved()
    {
        AuthoredProjectDependencyFactsResult.Incomplete version = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package"
                                  Version="1.0"
                                  version="2.0" />
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete include = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.One"
                                  include="Example.Two"
                                  Version="1.0" />
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete condition = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package"
                                  Version="1.0"
                                  Condition="'$(TargetFramework)' == 'net9.0'"
                                  condition="'$(TargetFramework)' == 'net8.0'" />
              </ItemGroup>
            </Project>
            """);

        Assert.Null(
            version.Value.PackageDeclarations.Single()
                .CanonicalVersionConstraint);
        AuthoredProjectPackageDeclaration includeDeclaration =
            Assert.Single(include.Value.PackageDeclarations);
        Assert.Null(includeDeclaration.CanonicalPackageId);
        Assert.Null(includeDeclaration.SourcePackageIdSpelling);
        Assert.IsType<AuthoredProjectDependencyCondition.Unresolved>(
            condition.Value.PackageDeclarations.Single().Condition);
        AssertReasons(
            version,
            AuthoredProjectDependencyLimitationReason
                .ConflictingVersionForms);
        AssertReasons(
            include,
            AuthoredProjectDependencyLimitationReason
                .UnsupportedPackageReferenceShape);
        AssertReasons(
            condition,
            AuthoredProjectDependencyLimitationReason.UnsupportedCondition);
    }

    [Fact]
    public void Execute_AmbiguousAttributeOrderDoesNotChooseAWinner()
    {
        AuthoredProjectDependencyFactsResult.Incomplete first = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package"
                                  include="Example.Other"
                                  Version="1.0"
                                  version="2.0" />
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete second = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference version="2.0"
                                  Version="1.0"
                                  include="Example.Other"
                                  Include="Example.Package" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(first.Value.Identity, second.Value.Identity);
    }

    [Fact]
    public void Execute_ExcludeIsIncompleteAndChangesSemanticIdentity()
    {
        AuthoredProjectDependencyFacts plain = Available(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package" Version="1.0" />
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete excluded = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package"
                                  Exclude="Example.Package"
                                  Version="1.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.NotEqual(plain.Identity, excluded.Value.Identity);
        AssertReasons(
            excluded,
            AuthoredProjectDependencyLimitationReason.PackageItemOperation);
    }

    [Fact]
    public void Execute_UnsupportedAncestryRetainsFactsAsIncomplete()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <Choose>
                <When Condition="'$(Configuration)' == 'Release'">
                  <PropertyGroup>
                    <TargetFramework>net9.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Example.Package" Version="1.0" />
                  </ItemGroup>
                </When>
              </Choose>
            </Project>
            """);

        Assert.Equal(
            "net9.0",
            result.Value.TargetFrameworks.Single()
                .Identity.CanonicalFramework);
        Assert.IsType<AuthoredProjectDependencyCondition.Unresolved>(
            result.Value.PackageDeclarations.Single().Condition);
        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason
                .UnsupportedTargetDeclaration,
            AuthoredProjectDependencyLimitationReason
                .UnsupportedPackageReferenceShape);
    }

    [Fact]
    public void Execute_NestedVersionMarkupCannotBecomeLiteralConstraint()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project xmlns:x="https://example.test/not-msbuild">
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version><x:Value>1.0</x:Value></Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        AuthoredProjectPackageDeclaration declaration =
            Assert.Single(result.Value.PackageDeclarations);
        Assert.Null(declaration.CanonicalVersionConstraint);
        Assert.Null(declaration.SourceVersionConstraintSpelling);
        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason
                .UnsupportedPackageReferenceShape);
    }

    [Fact]
    public void Execute_VersionAlternativesRemainInSemanticIdentity()
    {
        AuthoredProjectDependencyFactsResult.Incomplete two = Incomplete(
            VersionAlternativesProject("2.0"));
        AuthoredProjectDependencyFactsResult.Incomplete three = Incomplete(
            VersionAlternativesProject("3.0"));

        Assert.Null(
            two.Value.PackageDeclarations.Single()
                .CanonicalVersionConstraint);
        Assert.NotEqual(two.Value.Identity, three.Value.Identity);
        AssertReasons(
            two,
            AuthoredProjectDependencyLimitationReason
                .ConflictingVersionForms,
            AuthoredProjectDependencyLimitationReason.UnsupportedCondition);
    }

    [Fact]
    public void Execute_VersionAlternativeOrderIsNotSemantic()
    {
        AuthoredProjectDependencyFactsResult.Incomplete first = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version>1.0</Version>
                  <Version>2.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete second = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version>2.0</Version>
                  <Version>1.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(first.Value.Identity, second.Value.Identity);
    }

    [Fact]
    public void Execute_UnresolvedConditionIdentityPreservesFieldBoundaries()
    {
        AuthoredProjectDependencyFactsResult.Incomplete one = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package"
                                  Version="1.0"
                                  Condition="A&#10;B" />
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete two = Incomplete(
            """
            <Project>
              <ItemGroup Condition="A">
                <PackageReference Include="Example.Package"
                                  Version="1.0"
                                  Condition="B" />
              </ItemGroup>
            </Project>
            """);

        var oneCondition = Assert.IsType<
            AuthoredProjectDependencyCondition.Unresolved>(
                one.Value.PackageDeclarations.Single().Condition);
        var twoCondition = Assert.IsType<
            AuthoredProjectDependencyCondition.Unresolved>(
                two.Value.PackageDeclarations.Single().Condition);
        Assert.NotEqual(
            oneCondition.OpaqueIdentity,
            twoCondition.OpaqueIdentity);
    }

    [Fact]
    public void Execute_AllUnsupportedConditionsContributeLimitations()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <ItemGroup Condition="'@(Candidates)' == 'x'">
                <PackageReference Include="Example.Package"
                                  Version="1.0"
                                  Condition="false" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(
            2,
            result.Limitations.Single(limitation =>
                    limitation.Reason
                        == AuthoredProjectDependencyLimitationReason
                            .UnsupportedCondition)
                .Count);
        Assert.Equal(
            1,
            result.Limitations.Single(limitation =>
                    limitation.Reason
                        == AuthoredProjectDependencyLimitationReason
                            .ItemOrMetadataExpression)
                .Count);
    }

    [Fact]
    public void Execute_UnsupportedAncestryStillClassifiesEveryCondition()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <Choose>
                <When Condition="false">
                  <ItemGroup Condition="'@(Candidates)' == 'x'">
                    <PackageReference Include="Example.Package"
                                      Version="1.0"
                                      Condition="'%(Candidate.Identity)' == 'x'" />
                  </ItemGroup>
                </When>
              </Choose>
            </Project>
            """);

        Assert.Equal(
            3,
            result.Limitations.Single(limitation =>
                    limitation.Reason
                        == AuthoredProjectDependencyLimitationReason
                            .UnsupportedCondition)
                .Count);
        Assert.Equal(
            2,
            result.Limitations.Single(limitation =>
                    limitation.Reason
                        == AuthoredProjectDependencyLimitationReason
                            .ItemOrMetadataExpression)
                .Count);
    }

    [Fact]
    public void Execute_SharedAncestorConditionCountsOnce()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <ItemGroup Condition="'$(Configuration)' == 'Release'">
                <PackageReference Include="Example.One" Version="1.0" />
                <PackageReference Include="Example.Two" Version="2.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(
            1,
            result.Limitations.Single(limitation =>
                    limitation.Reason
                        == AuthoredProjectDependencyLimitationReason
                            .UnsupportedCondition)
                .Count);
        Assert.Equal(
            1,
            result.Limitations.Single(limitation =>
                    limitation.Reason
                        == AuthoredProjectDependencyLimitationReason
                            .PropertyIndirection)
                .Count);
    }

    [Fact]
    public void Execute_UnresolvedNestedShapeIgnoresCommentsAndAttributeOrder()
    {
        AuthoredProjectDependencyFactsResult.Incomplete first = Incomplete(
            """
            <Project xmlns:x="https://example.test/shape">
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version>
                    <x:Value A="1" B="2">1<!-- Inline comment. -->.0</x:Value>
                    <!-- Non-semantic comment. -->
                  </Version>
                  <VersionOverride>
                    <x:Value A="1" B="2">2.0</x:Value>
                  </VersionOverride>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete second = Incomplete(
            """
            <Project xmlns:x="https://example.test/shape">
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version>
                    <x:Value B="2" A="1">1.0</x:Value>
                  </Version>
                  <VersionOverride>
                    <x:Value B="2" A="1">2.0</x:Value>
                  </VersionOverride>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(first.Value.Identity, second.Value.Identity);
        Assert.NotEqual(
            first.Value.ContentProvenance,
            second.Value.ContentProvenance);
    }

    [Fact]
    public void Execute_AmbiguousVersionConditionOrderIsNotSemantic()
    {
        AuthoredProjectDependencyFactsResult.Incomplete first = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version Condition="A" condition="B">1.0</Version>
                  <VersionOverride Condition="C" condition="D">2.0</VersionOverride>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFactsResult.Incomplete second = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package">
                  <Version condition="B" Condition="A">1.0</Version>
                  <VersionOverride condition="D" Condition="C">2.0</VersionOverride>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(first.Value.Identity, second.Value.Identity);
    }

    [Fact]
    public void Execute_UnsupportedSyntaxPreservesUsableDeclarationsAsIncomplete()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="Directory.Build.props" />
              <PropertyGroup>
                <TargetFramework>$(DefaultTargetFramework)</TargetFramework>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Example.Managed" Version="2.0.0" />
                <PackageReference Include="Example.Valid" Version="1.0.0" />
                <PackageReference Include="Example.Managed" />
                <PackageReference Include="Example.Conditional"
                                  Version="3.0.0"
                                  Condition="'$(Configuration)' == 'Release'" />
                <PackageReference Update="Example.Imported" Version="4.0.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(3, result.Value.PackageDeclarations.Length);
        Assert.Contains(
            result.Value.PackageDeclarations,
            declaration =>
                declaration.CanonicalPackageId == "example.valid"
                && declaration.CanonicalVersionConstraint
                    == "[1.0.0, )");
        Assert.Contains(
            result.Value.PackageDeclarations,
            declaration =>
                declaration.CanonicalPackageId == "example.managed"
                && declaration.CanonicalVersionConstraint is null);
        Assert.IsType<AuthoredProjectDependencyCondition.Unresolved>(
            result.Value.PackageDeclarations.Single(
                    declaration =>
                        declaration.CanonicalPackageId
                            == "example.conditional")
                .Condition);

        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason.ExplicitImport,
            AuthoredProjectDependencyLimitationReason.PropertyIndirection,
            AuthoredProjectDependencyLimitationReason.CentralPackageManagement,
            AuthoredProjectDependencyLimitationReason.MissingVersionConstraint,
            AuthoredProjectDependencyLimitationReason.UnsupportedCondition,
            AuthoredProjectDependencyLimitationReason.PackageItemOperation);
    }

    [Fact]
    public void Execute_GlobalPackageReferenceIsCentralManagementEvidence()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <ItemGroup>
                <GlobalPackageReference Include="Example.Package"
                                        Version="1.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.Empty(result.Value.PackageDeclarations);
        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason
                .CentralPackageManagement);
    }

    [Theory]
    [InlineData("<ManagePackageVersionsCentrally />")]
    [InlineData("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>")]
    [InlineData("<ManagePackageVersionsCentrally><Value>false</Value></ManagePackageVersionsCentrally>")]
    public void Execute_CentralManagementPropertyPresenceIsIncomplete(
        string property)
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            $$"""
            <Project>
              <PropertyGroup>
                {{property}}
              </PropertyGroup>
            </Project>
            """);

        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason
                .CentralPackageManagement);
    }

    [Fact]
    public void Execute_ItemAndMetadataExpressionsAreNotLiteralFrameworks()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>@(Frameworks)</TargetFramework>
              </PropertyGroup>
              <ItemGroup Condition="'$(TargetFramework)' == '%(Candidate.Identity)'">
                <PackageReference Include="Example.Package" Version="1.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(
            AuthoredProjectTargetFrameworkKind.Unresolved,
            result.Value.TargetFrameworks.Single().Identity.Kind);
        Assert.IsType<AuthoredProjectDependencyCondition.Unresolved>(
            result.Value.PackageDeclarations.Single().Condition);
        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason
                .ItemOrMetadataExpression,
            AuthoredProjectDependencyLimitationReason.UnsupportedCondition);
    }

    [Fact]
    public void Execute_TargetFrameworkExpressionSemicolonsDoNotInventTargets()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <PropertyGroup>
                <TargetFrameworks>$(Targets.Replace(';net8.0;', ';net9.0;'))</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);

        AuthoredProjectTargetFramework target =
            Assert.Single(result.Value.TargetFrameworks);
        Assert.Equal(
            AuthoredProjectTargetFrameworkKind.Unresolved,
            target.Identity.Kind);
        Assert.Null(target.Identity.CanonicalFramework);
        Assert.DoesNotContain(
            result.Value.TargetFrameworks,
            candidate => candidate.Identity.CanonicalFramework is
                "net8.0" or "net9.0");
    }

    [Fact]
    public void Execute_TargetConditionsContributeToSemanticIdentity()
    {
        AuthoredProjectDependencyFactsResult.Incomplete first = Incomplete(
            ConditionalTargetsProject("Debug", "Release"));
        AuthoredProjectDependencyFactsResult.Incomplete second = Incomplete(
            ConditionalTargetsProject("Release", "Debug"));

        Assert.NotEqual(first.Value.Identity, second.Value.Identity);
    }

    [Fact]
    public void Execute_IncludeLessPackageOperationsContributeToIdentity()
    {
        AuthoredProjectDependencyFactsResult.Incomplete first = Incomplete(
            IncludeLessUpdateProject("1.0"));
        AuthoredProjectDependencyFactsResult.Incomplete second = Incomplete(
            IncludeLessUpdateProject("2.0"));

        Assert.Single(first.Value.PackageDeclarations);
        Assert.Single(first.Value.UnresolvedDependencySyntax);
        Assert.NotEqual(first.Value.Identity, second.Value.Identity);
    }

    [Fact]
    public void Execute_NuGetEquivalentPrereleaseConstraintsDoNotConflict()
    {
        AuthoredProjectDependencyFacts facts = Available(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package"
                                  Version="1.0.0-alpha" />
                <PackageReference Include="example.package"
                                  Version="1.0.0-ALPHA" />
              </ItemGroup>
            </Project>
            """);

        AuthoredProjectPackageDeclaration declaration =
            Assert.Single(facts.PackageDeclarations);
        Assert.Equal(2, declaration.SourceOccurrenceCount);
        Assert.Equal(
            "[1.0.0-alpha, )",
            declaration.CanonicalVersionConstraint);
    }

    [Fact]
    public void Execute_ConflictingDeclarationsAreIncomplete()
    {
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package" Version="1.0.0" />
                <PackageReference Include="example.package" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(2, result.Value.PackageDeclarations.Length);
        AssertReasons(
            result,
            AuthoredProjectDependencyLimitationReason
                .ConflictingPackageDeclaration);
    }

    [Fact]
    public void Execute_EquivalentOccurrencesCollapseWithExactCount()
    {
        AuthoredProjectDependencyFacts facts = Available(
            """
            <Project>
              <ItemGroup>
                <PackageReference Include="Example.Package" Version="1.0" />
                <PackageReference Include="example.package" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        AuthoredProjectPackageDeclaration declaration =
            Assert.Single(facts.PackageDeclarations);
        Assert.Equal(2, declaration.SourceOccurrenceCount);
        Assert.Equal("example.package", declaration.CanonicalPackageId);
        Assert.Equal("[1.0.0, )", declaration.CanonicalVersionConstraint);
    }

    [Fact]
    public void Execute_ContentAndSemanticIdentityRemainDistinct()
    {
        AuthoredProjectDependencyFacts first = Available(
            """
            <Project>
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Example.Package" Version="1.0" />
              </ItemGroup>
            </Project>
            """);
        AuthoredProjectDependencyFacts second = Available(
            """
            <Project>
              <!-- Formatting and package-id casing are not semantic. -->
              <ItemGroup>
                <PackageReference Version="1.0.0" Include="example.package"/>
              </ItemGroup>
              <PropertyGroup><TargetFramework> net9.0 </TargetFramework></PropertyGroup>
            </Project>
            """);

        Assert.Equal(first.Identity, second.Identity);
        Assert.NotEqual(first.ContentProvenance, second.ContentProvenance);
    }

    [Fact]
    public void Execute_LegacyNamespaceIsSupported()
    {
        AuthoredProjectDependencyFacts facts = Available(
            """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Example.Package" Version="[1.0]" />
              </ItemGroup>
            </Project>
            """);

        Assert.Equal(
            "net48",
            facts.TargetFrameworks.Single().Identity.CanonicalFramework);
        Assert.Single(facts.PackageDeclarations);
    }

    [Fact]
    public void Execute_RejectsMalformedDtdAndUnsupportedRoots()
    {
        AuthoredProjectDependencyFactsResult.Failed malformed = Failed(
            "<Project>");
        AuthoredProjectDependencyFactsResult.Failed dtd = Failed(
            """
            <!DOCTYPE Project [<!ENTITY x "value">]>
            <Project>&x;</Project>
            """);
        AuthoredProjectDependencyFactsResult.Failed root = Failed(
            "<Solution />");
        AuthoredProjectDependencyFactsResult.Failed namespacedRoot = Failed(
            "<Project xmlns=\"https://example.test/project\" />");

        Assert.Equal(
            AuthoredProjectDependencyFactsFailureReason.MalformedXml,
            malformed.Failure.Reason);
        Assert.Equal(
            AuthoredProjectDependencyFactsFailureReason.MalformedXml,
            dtd.Failure.Reason);
        Assert.Equal(
            AuthoredProjectDependencyFactsFailureReason
                .UnsupportedDocumentShape,
            root.Failure.Reason);
        Assert.Equal(
            AuthoredProjectDependencyFactsFailureReason
                .UnsupportedDocumentShape,
            namespacedRoot.Failure.Reason);
    }

    [Fact]
    public void Execute_HostileTextIsContainedAtConstruction()
    {
        const string Hostile = "Example.\u202EPackage";
        AuthoredProjectDependencyFactsResult.Incomplete result = Incomplete(
            $$"""
            <Project>
              <ItemGroup>
                <PackageReference Include="{{Hostile}}" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        InertString source =
            Assert.IsType<InertString>(
                result.Value.PackageDeclarations.Single()
                    .SourcePackageIdSpelling);
        Assert.True(
            InertString.IsPermitted(TextPolicy.Field, source.ToString()));
        Assert.DoesNotContain('\u202E', source.ToString());
        Assert.Contains(@"\u202E", source.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Hostile,
            string.Join(
                "\n",
                result.Limitations.Select(limitation =>
                    limitation.Reason.ToString())),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_EnforcesProjectByteLimit()
    {
        var bytes = new byte[
            AuthoredProjectDependencyFactsQuery.MaxProjectBytes + 1];

        AssertLimitFailure(
            AuthoredProjectDependencyFactsQuery.Execute(bytes));
    }

    [Fact]
    public void Execute_EnforcesScalarLimit()
    {
        string value = new(
            'a',
            AuthoredProjectDependencyFactsQuery.MaxScalarCharacters + 1);

        AssertLimitFailure(
            Execute(
                $"<Project><PropertyGroup><TargetFramework>{value}</TargetFramework></PropertyGroup></Project>"));
    }

    [Fact]
    public void Execute_EnforcesTargetOccurrenceLimit()
    {
        string properties = string.Concat(
            Enumerable.Repeat(
                "<TargetFramework>net9.0</TargetFramework>",
                AuthoredProjectDependencyFactsQuery
                    .MaxTargetFrameworkOccurrences + 1));

        AssertLimitFailure(
            Execute(
                $"<Project><PropertyGroup>{properties}</PropertyGroup></Project>"));
    }

    [Fact]
    public void Execute_EnforcesTargetOccurrenceLimitWithinOneProperty()
    {
        string targets = string.Join(
            ';',
            Enumerable.Repeat(
                "net9.0",
                AuthoredProjectDependencyFactsQuery
                    .MaxTargetFrameworkOccurrences + 1));

        AssertLimitFailure(
            Execute(
                $"<Project><PropertyGroup><TargetFrameworks>{targets}</TargetFrameworks></PropertyGroup></Project>"));
    }

    [Fact]
    public void Execute_EnforcesPackageReferenceOccurrenceLimit()
    {
        string references = string.Concat(
            Enumerable.Repeat(
                "<PackageReference Include=\"Example.Package\" Version=\"1.0\" />",
                AuthoredProjectDependencyFactsQuery
                    .MaxPackageReferenceOccurrences + 1));

        AssertLimitFailure(
            Execute($"<Project><ItemGroup>{references}</ItemGroup></Project>"));
    }

    [Fact]
    public void Execute_EnforcesLimitationOccurrenceLimit()
    {
        string imports = string.Concat(
            Enumerable.Repeat(
                "<Import Project=\"Imported.props\" />",
                AuthoredProjectDependencyFactsQuery
                    .MaxLimitationOccurrences + 1));

        AssertLimitFailure(Execute($"<Project>{imports}</Project>"));
    }

    [Fact]
    public void Execute_EnforcesDecodedCharacterLimit()
    {
        string comment = new(
            'a',
            AuthoredProjectDependencyFactsQuery.MaxProjectCharacters + 1);

        AssertLimitFailure(
            Execute($"<Project><!--{comment}--></Project>"));
    }

    [Fact]
    public void Execute_EnforcesXmlElementDepthLimit()
    {
        string opening = string.Concat(
            Enumerable.Repeat(
                "<Nested>",
                AuthoredProjectDependencyFactsQuery.MaxXmlElementDepth));
        string closing = string.Concat(
            Enumerable.Repeat(
                "</Nested>",
                AuthoredProjectDependencyFactsQuery.MaxXmlElementDepth));

        AssertLimitFailure(
            Execute($"<Project>{opening}{closing}</Project>"));
    }

    private static string ConditionalTargetsProject(
        string net8Configuration,
        string net9Configuration) =>
        $$"""
        <Project>
          <PropertyGroup>
            <TargetFramework Condition="'$(Configuration)' == '{{net8Configuration}}'">net8.0</TargetFramework>
            <TargetFramework Condition="'$(Configuration)' == '{{net9Configuration}}'">net9.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private static string IncludeLessUpdateProject(string version) =>
        $$"""
        <Project>
          <ItemGroup>
            <PackageReference Include="Example.Direct" Version="1.0" />
            <PackageReference Update="Example.Imported" Version="{{version}}" />
          </ItemGroup>
        </Project>
        """;

    private static string VersionAlternativesProject(string conditionedVersion) =>
        $$"""
        <Project>
          <ItemGroup>
            <PackageReference Include="Example.Package" Version="1.0">
              <Version Condition="'$(Configuration)' == 'Release'">{{conditionedVersion}}</Version>
            </PackageReference>
          </ItemGroup>
        </Project>
        """;

    private static AuthoredProjectDependencyFacts Available(string xml) =>
        Assert.IsType<AuthoredProjectDependencyFactsResult.Available>(
                Execute(xml))
            .Value;

    private static AuthoredProjectDependencyFactsResult.Incomplete Incomplete(
        string xml) =>
        Assert.IsType<AuthoredProjectDependencyFactsResult.Incomplete>(
            Execute(xml));

    private static AuthoredProjectDependencyFactsResult.Failed Failed(
        string xml) =>
        Assert.IsType<AuthoredProjectDependencyFactsResult.Failed>(
            Execute(xml));

    private static AuthoredProjectDependencyFactsResult Execute(string xml) =>
        AuthoredProjectDependencyFactsQuery.Execute(
            Encoding.UTF8.GetBytes(xml));

    private static void AssertLimitFailure(
        AuthoredProjectDependencyFactsResult result)
    {
        var failure =
            Assert.IsType<AuthoredProjectDependencyFactsResult.Failed>(result);
        Assert.Equal(
            AuthoredProjectDependencyFactsFailureReason.ConfiguredLimitExceeded,
            failure.Failure.Reason);
    }

    private static void AssertReasons(
        AuthoredProjectDependencyFactsResult.Incomplete result,
        params AuthoredProjectDependencyLimitationReason[] reasons)
    {
        foreach (AuthoredProjectDependencyLimitationReason reason in reasons)
        {
            Assert.Contains(
                result.Limitations,
                limitation => limitation.Reason == reason);
        }
    }
}
