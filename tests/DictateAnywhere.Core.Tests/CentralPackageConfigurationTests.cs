using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class CentralPackageConfigurationTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.NET.Test.Sdk"] = "17.10.0",
            ["xunit"] = "2.6.6",
            ["xunit.runner.visualstudio"] = "2.5.8",
            ["coverlet.collector"] = "6.0.2",
            ["NAudio"] = "2.2.1",
            ["UglyToad.PdfPig"] = "1.7.0-custom-5",
            ["PDFtoImage"] = "5.4.0",
            ["Google.Apis.YouTube.v3"] = "1.75.0.4246",
            ["System.Security.Cryptography.ProtectedData"] = "8.0.0",
            ["Markdig"] = "1.3.2"
        };

    private static readonly string[] SharedTestPackages =
    [
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "xunit.runner.visualstudio",
        "coverlet.collector"
    ];

    private const string TestAssetContract =
        "runtime; build; native; contentfiles; analyzers; buildtransitive";

    [Fact]
    public void CentralPackageVersions_AreUniqueExactAndCoverEveryDirectReference()
    {
        string repoRoot = FindRepoRoot();
        XDocument central = XDocument.Load(Path.Combine(repoRoot, "Directory.Packages.props"));
        XElement[] packageVersions = central.Descendants("PackageVersion").ToArray();

        Assert.Equal("true", central.Descendants("ManagePackageVersionsCentrally").Single().Value);
        Assert.Equal("false", central.Descendants("CentralPackageTransitivePinningEnabled").Single().Value);
        Assert.Equal(ExpectedVersions.Count, packageVersions.Length);
        Assert.Equal(
            packageVersions.Length,
            packageVersions
                .Select(element => RequiredAttribute(element, "Include"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        foreach ((string package, string version) in ExpectedVersions)
        {
            XElement declaration = Assert.Single(packageVersions.Where(element =>
                string.Equals(RequiredAttribute(element, "Include"), package, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(version, RequiredAttribute(declaration, "Version"));
        }

        string[] projectPaths = FindTrackedProjectInputs(repoRoot).ToArray();
        Assert.Equal(29, projectPaths.Length);
        XElement[] references = projectPaths
            .Select(XDocument.Load)
            .SelectMany(document => document.Descendants("PackageReference"))
            .ToArray();

        Assert.All(references, reference =>
        {
            string package = RequiredAttribute(reference, "Include");
            Assert.True(ExpectedVersions.ContainsKey(package), $"PackageReference '{package}' has no central version.");
            Assert.Null(reference.Attribute("Version"));
            Assert.Null(reference.Attribute("VersionOverride"));
        });
    }

    [Fact]
    public void SharedTestConfiguration_OwnsOnlyTheCommonTestContract()
    {
        string repoRoot = FindRepoRoot();
        XDocument shared = XDocument.Load(Path.Combine(repoRoot, "tests", "Directory.Build.props"));

        XElement import = Assert.Single(shared.Root!.Elements("Import"));
        Assert.Equal(@"..\Directory.Build.props", RequiredAttribute(import, "Project"));
        Assert.Equal("false", shared.Descendants("IsPackable").Single().Value);
        Assert.Equal("true", shared.Descendants("IsTestProject").Single().Value);

        XElement[] references = shared.Descendants("PackageReference").ToArray();
        Assert.Equal(SharedTestPackages.Length, references.Length);
        Assert.Equal(
            SharedTestPackages.OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
            references.Select(reference => RequiredAttribute(reference, "Include"))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        foreach (string package in new[] { "xunit.runner.visualstudio", "coverlet.collector" })
        {
            XElement reference = Assert.Single(references.Where(element =>
                string.Equals(RequiredAttribute(element, "Include"), package, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal("all", reference.Element("PrivateAssets")?.Value);
            Assert.Equal(TestAssetContract, reference.Element("IncludeAssets")?.Value);
        }

        Assert.All(references, reference =>
        {
            Assert.Null(reference.Attribute("Version"));
            Assert.Null(reference.Attribute("VersionOverride"));
        });
        Assert.Empty(shared.Descendants("RootNamespace"));
        Assert.Empty(shared.Descendants("AssemblyName"));
        Assert.Empty(shared.Descendants("ProjectReference"));
    }

    [Fact]
    public void TestProjects_InheritSharedContractAndRetainProjectSpecificMetadata()
    {
        string repoRoot = FindRepoRoot();
        string[] testProjects = Directory
            .GetFiles(Path.Combine(repoRoot, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Where(IsTrackedProjectInput)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] solutionProjects = File.ReadLines(Path.Combine(repoRoot, "DictateAnywhere.sln"))
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(line => line.Split(',').ElementAtOrDefault(1)?.Trim().Trim('"'))
            .Where(path => path?.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) == true)
            .Select(path => Path.GetFullPath(Path.Combine(repoRoot, path!)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(12, testProjects.Length);
        Assert.Equal(FindTrackedProjectInputs(repoRoot), solutionProjects);
        foreach (string testProject in testProjects)
        {
            XDocument document = XDocument.Load(testProject);
            Assert.Empty(document.Descendants("IsPackable"));
            Assert.Empty(document.Descendants("IsTestProject"));
            Assert.Empty(document.Descendants("PackageReference").Where(reference =>
                SharedTestPackages.Contains(RequiredAttribute(reference, "Include"), StringComparer.OrdinalIgnoreCase)));
            Assert.Single(document.Descendants("RootNamespace"));
            Assert.Single(document.Descendants("AssemblyName"));
            Assert.NotEmpty(document.Descendants("ProjectReference"));
        }
    }

    [Fact]
    public void EveryTrackedProject_HasACompleteNuGetLock()
    {
        string repoRoot = FindRepoRoot();
        string[] projectPaths = FindTrackedProjectInputs(repoRoot).ToArray();
        Assert.Equal(29, projectPaths.Length);

        HashSet<string> resolvedPackages = new(StringComparer.OrdinalIgnoreCase);
        foreach (string projectPath in projectPaths)
        {
            string lockPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.lock.json");
            Assert.True(File.Exists(lockPath), $"Missing package lock for '{projectPath}'.");
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockPath));
            Assert.True(document.RootElement.GetProperty("version").GetInt32() >= 1);
            System.Text.Json.JsonElement dependencies = document.RootElement.GetProperty("dependencies");
            Assert.NotEmpty(dependencies.EnumerateObject());
            foreach (System.Text.Json.JsonProperty framework in dependencies.EnumerateObject())
            {
                foreach (System.Text.Json.JsonProperty dependency in framework.Value.EnumerateObject())
                {
                    string type = dependency.Value.GetProperty("type").GetString() ?? string.Empty;
                    if (string.Equals(type, "Project", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string resolved = dependency.Value.GetProperty("resolved").GetString() ?? string.Empty;
                    string contentHash = dependency.Value.GetProperty("contentHash").GetString() ?? string.Empty;
                    Assert.False(string.IsNullOrWhiteSpace(resolved));
                    Assert.False(string.IsNullOrWhiteSpace(contentHash));
                    resolvedPackages.Add($"{dependency.Name}/{resolved}");
                }
            }
        }

        Assert.Equal(48, resolvedPackages.Count);
    }

    [Fact]
    public void ProductionProjects_HaveSeparateLockedWinX64PublishGraphs()
    {
        string repoRoot = FindRepoRoot();
        string sourceRoot = Path.Combine(repoRoot, "src");
        string[] projectPaths = FindTrackedProjectInputs(repoRoot)
            .Where(path => path.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(13, projectPaths.Length);

        foreach (string projectPath in projectPaths)
        {
            string lockPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.win-x64.lock.json");
            Assert.True(File.Exists(lockPath), $"Missing win-x64 publish lock for '{projectPath}'.");
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockPath));
            System.Text.Json.JsonElement dependencies = document.RootElement.GetProperty("dependencies");
            Assert.Single(dependencies.EnumerateObject().Where(framework => framework.Name.EndsWith("/win-x64", StringComparison.OrdinalIgnoreCase)));
        }

        string installerScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "build-installer.ps1"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(installerScript, "NuGetLockFilePath=packages\\.win-x64\\.lock\\.json", System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(installerScript, "RestoreLockedMode=true", System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count);
    }

    [Fact]
    public void Ci_UsesLockedRestoreAndImmutableActionRevisions()
    {
        string repoRoot = FindRepoRoot();
        string workflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "ci.yml"));
        Assert.Contains("dotnet restore DictateAnywhere.sln --locked-mode", workflow, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s*uses:\s*[^@\s]+@(v\d+|main|master|latest)\s*(#.*)?$", workflow);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(workflow, @"(?m)^\s*uses:\s*[^@\s]+@[a-f0-9]{40}\s*(#.*)?$").Count);
    }

    [Fact]
    [Trait("Category", "ProcessIntegration")]
    public async Task SecurityCompliance_AcceptsCentralPackageConfiguration()
    {
        string repoRoot = FindRepoRoot();
        ProcessStartInfo startInfo = new("pwsh")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "security-compliance.ps1"));

        using Process process = new() { StartInfo = startInfo };
        Assert.True(process.Start(), "Failed to start the security compliance process.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanupTimeout.Token);
            Assert.Fail("Security compliance timed out after 30 seconds.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        Assert.True(
            process.ExitCode == 0,
            $"Security compliance failed with exit code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }

    private static IEnumerable<string> FindTrackedProjectInputs(string repoRoot) =>
        new[] { "src", "tools", "tests" }
            .SelectMany(directory => Directory.GetFiles(
                Path.Combine(repoRoot, directory),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(IsTrackedProjectInput)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static bool IsTrackedProjectInput(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}TestResults{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase);

    private static string RequiredAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value ?? throw new InvalidDataException($"{element.Name} is missing {name}.");

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
