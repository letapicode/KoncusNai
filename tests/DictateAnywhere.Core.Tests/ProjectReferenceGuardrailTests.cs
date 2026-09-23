using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class ProjectReferenceGuardrailTests
{
    [Fact]
    public void ProductionProjects_MatchProjectReferenceGuardrailPolicy()
    {
        SolutionProjectGraph graph = SolutionProjectGraph.Load();
        GuardrailPolicy policy = GuardrailPolicy.Load(graph.RepoRoot);

        List<string> violations = [];

        foreach (GuardrailProject guardrail in policy.Guardrails)
        {
            if (!graph.Projects.TryGetValue(guardrail.Project, out SolutionProject? project))
            {
                violations.Add($"Policy references unknown project '{guardrail.Project}'.");
                continue;
            }

            HashSet<string> allowed = new(guardrail.AllowedProjectReferences, StringComparer.Ordinal);
            foreach (string actualReference in project.References)
            {
                if (!allowed.Contains(actualReference))
                {
                    violations.Add(
                        $"Project '{guardrail.Project}' has disallowed project reference '{actualReference}'.");
                }
            }
        }

        foreach (string projectName in graph.Projects.Keys)
        {
            if (!policy.Guardrails.Any(guardrail => string.Equals(guardrail.Project, projectName, StringComparison.Ordinal)))
            {
                violations.Add($"Project '{projectName}' is missing from the guardrail policy.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ProductionProjectGraph_IsAcyclic()
    {
        SolutionProjectGraph graph = SolutionProjectGraph.Load();

        List<string> cycles = graph.FindCycles();

        Assert.True(cycles.Count == 0, string.Join(Environment.NewLine, cycles));
    }

    [Fact]
    public void SwappableRuntimeModules_OnlyDependOnCoreContractsAndPlatformAdapters()
    {
        SolutionProjectGraph graph = SolutionProjectGraph.Load();

        Assert.Equal(
            ["DictateAnywhere.Core", "DictateAnywhere.Platform.Windows"],
            graph.Projects["DictateAnywhere.Audio"].References);
        Assert.Equal(
            ["DictateAnywhere.Core", "DictateAnywhere.Platform.Windows"],
            graph.Projects["DictateAnywhere.Hotkeys"].References);
        Assert.Equal(
            ["DictateAnywhere.Core"],
            graph.Projects["DictateAnywhere.Inference"].References);
        Assert.Equal(
            ["DictateAnywhere.Core", "DictateAnywhere.Platform.Windows"],
            graph.Projects["DictateAnywhere.Insertion"].References);
    }

    [Fact]
    public void ExecutableProjectRoles_AreExplicit()
    {
        SolutionProjectGraph graph = SolutionProjectGraph.Load();
        GuardrailPolicy policy = GuardrailPolicy.Load(graph.RepoRoot);
        Dictionary<string, string> roles = policy.Guardrails.ToDictionary(
            guardrail => guardrail.Project,
            guardrail => guardrail.Role,
            StringComparer.Ordinal);

        Assert.Equal("composition-root", roles["DictateAnywhere.App"]);
        Assert.Equal("host", roles["DictateAnywhere.UiAccessHelper"]);
        Assert.Equal("tool", roles["DictateAnywhere.ModelBenchmark"]);
        Assert.Equal("tool", roles["DictateAnywhere.TtsCli"]);
        Assert.Equal("tool", roles["DictateAnywhere.Spikes"]);
        Assert.Equal("tool", roles["DictateAnywhere.VoicePreviewGenerator"]);
    }

    private sealed record GuardrailPolicy(IReadOnlyList<GuardrailProject> Guardrails)
    {
        public static GuardrailPolicy Load(string repoRoot)
        {
            string policyPath = Path.Combine(repoRoot, "docs", "architecture", "project-reference-guardrails.json");
            using FileStream stream = File.OpenRead(policyPath);
            PolicyDocument? document = JsonSerializer.Deserialize<PolicyDocument>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            Assert.NotNull(document);
            Assert.NotNull(document!.Guardrails);

            return new GuardrailPolicy(document.Guardrails);
        }
    }

    private sealed record GuardrailProject(string Project, string Role, IReadOnlyList<string> AllowedProjectReferences);

    private sealed record PolicyDocument(IReadOnlyList<GuardrailProject> Guardrails);

    private sealed record SolutionProject(string Name, string Path, IReadOnlyList<string> References);

    private sealed class SolutionProjectGraph
    {
        private SolutionProjectGraph(string repoRoot, IReadOnlyDictionary<string, SolutionProject> projects)
        {
            RepoRoot = repoRoot;
            Projects = projects;
        }

        public string RepoRoot { get; }

        public IReadOnlyDictionary<string, SolutionProject> Projects { get; }

        public static SolutionProjectGraph Load()
        {
            string repoRoot = FindRepoRoot();
            string[] projectPaths = new[] { "src", "tools" }
                .SelectMany(root => Directory.GetFiles(
                    Path.Combine(repoRoot, root),
                    "*.csproj",
                    SearchOption.AllDirectories))
                .Where(path =>
                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Dictionary<string, string> namesByPath = new(StringComparer.OrdinalIgnoreCase);
            foreach (string projectPath in projectPaths)
            {
                string projectName = GetProjectName(projectPath);
                namesByPath[Path.GetFullPath(projectPath)] = projectName;
            }

            Dictionary<string, SolutionProject> projects = new(StringComparer.Ordinal);
            foreach (string projectPath in projectPaths)
            {
                string fullPath = Path.GetFullPath(projectPath);
                string projectName = namesByPath[fullPath];
                string projectDirectory = Path.GetDirectoryName(fullPath)!;
                List<string> references = [];

                System.Xml.Linq.XDocument document = System.Xml.Linq.XDocument.Load(fullPath);
                foreach (System.Xml.Linq.XElement referenceElement in document.Descendants("ProjectReference"))
                {
                    string? includePath = referenceElement.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(includePath))
                    {
                        continue;
                    }

                    string referencedPath = Path.GetFullPath(Path.Combine(projectDirectory, includePath));
                    Assert.True(namesByPath.TryGetValue(referencedPath, out string? referencedName),
                        $"Project '{projectName}' references unknown project path '{includePath}'.");
                    references.Add(referencedName!);
                }

                references.Sort(StringComparer.Ordinal);
                projects[projectName] = new SolutionProject(projectName, fullPath, references);
            }

            return new SolutionProjectGraph(repoRoot, projects);
        }

        public List<string> FindCycles()
        {
            Dictionary<string, int> state = new(StringComparer.Ordinal);
            List<string> stack = [];
            List<string> cycles = [];

            foreach (string projectName in Projects.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                Visit(projectName, state, stack, cycles);
            }

            return cycles;
        }

        private void Visit(
            string projectName,
            Dictionary<string, int> state,
            List<string> stack,
            List<string> cycles)
        {
            if (state.TryGetValue(projectName, out int currentState))
            {
                if (currentState == 1)
                {
                    int cycleStart = stack.IndexOf(projectName);
                    if (cycleStart >= 0)
                    {
                        List<string> cycleNodes = stack.Skip(cycleStart).Append(projectName).ToList();
                        cycles.Add(string.Join(" -> ", cycleNodes));
                    }
                }

                return;
            }

            state[projectName] = 1;
            stack.Add(projectName);

            foreach (string reference in Projects[projectName].References)
            {
                Visit(reference, state, stack, cycles);
            }

            stack.RemoveAt(stack.Count - 1);
            state[projectName] = 2;
        }

        private static string GetProjectName(string projectPath)
        {
            System.Xml.Linq.XDocument document = System.Xml.Linq.XDocument.Load(projectPath);
            string? assemblyName = document.Descendants("AssemblyName").Select(node => node.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return string.IsNullOrWhiteSpace(assemblyName)
                ? Path.GetFileNameWithoutExtension(projectPath)
                : assemblyName;
        }

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

            throw new InvalidOperationException("Unable to locate repository root from test base directory.");
        }
    }
}
