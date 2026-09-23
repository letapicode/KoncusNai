using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class SolutionBuildRoleTests
{
    [Fact]
    public void SolutionFilters_SeparateProductionAndDeveloperExecutableRoles()
    {
        string repoRoot = FindRepoRoot();
        string[] productionProjects = ReadFilterProjects(repoRoot, "DictateAnywhere.Production.slnf");
        string[] developerProjects = ReadFilterProjects(repoRoot, "DictateAnywhere.DeveloperTools.slnf");

        string[] expectedProductionProjects = Directory
            .GetFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(IsSourceProject)
            .Select(path => NormalizeRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] expectedDeveloperProjects = Directory
            .GetFiles(Path.Combine(repoRoot, "tools"), "*.csproj", SearchOption.AllDirectories)
            .Where(IsSourceProject)
            .Select(path => NormalizeRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expectedProductionProjects, productionProjects);
        Assert.Equal(expectedDeveloperProjects, developerProjects);
        Assert.Empty(productionProjects.Intersect(developerProjects, StringComparer.OrdinalIgnoreCase));

        Assert.Contains("src/DictateAnywhere.App/DictateAnywhere.App.csproj", productionProjects);
        Assert.Contains("src/DictateAnywhere.UiAccessHelper/DictateAnywhere.UiAccessHelper.csproj", productionProjects);
        Assert.DoesNotContain(productionProjects, path => path.StartsWith("tools/", StringComparison.OrdinalIgnoreCase));
        Assert.All(developerProjects, path => Assert.StartsWith("tools/", path, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] ReadFilterProjects(string repoRoot, string filterName)
    {
        string filterPath = Path.Combine(repoRoot, filterName);
        using FileStream stream = File.OpenRead(filterPath);
        using JsonDocument document = JsonDocument.Parse(stream);

        string solutionPath = document.RootElement.GetProperty("solution").GetProperty("path").GetString()!;
        Assert.Equal("DictateAnywhere.sln", solutionPath);

        string[] projects = document.RootElement
            .GetProperty("solution")
            .GetProperty("projects")
            .EnumerateArray()
            .Select(element => element.GetString()!.Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(projects);
        Assert.Equal(projects.Length, projects.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(projects, project => Assert.True(
            File.Exists(Path.Combine(repoRoot, project.Replace('/', Path.DirectorySeparatorChar))),
            $"Solution filter project does not exist: {project}"));
        return projects;
    }

    private static bool IsSourceProject(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRelativePath(string repoRoot, string path) =>
        Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DictateAnywhere.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
