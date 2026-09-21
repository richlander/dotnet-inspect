using System.CommandLine;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;
using NuGetFetch;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed partial class DependsAssetCommandTests
{
    private static string NuspecFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
            "manifest.nuspec");

    private static string AssetsFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.AssetPath(
            "project.assets.json");

    private static string ProjectDirectoryFixture =>
        FixtureCatalog.RestoredProjectDependencyFacts.ProjectDirectory();
}
