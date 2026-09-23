using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class PublicApiBaselineTests
{
    [Fact]
    public void CompiledPublicApiAndDependencies_MatchCanonicalBaseline()
    {
        string repoRoot = PublicApiContract.FindRepoRoot();
        string baselinePath = Path.Combine(repoRoot, "docs", "architecture", "public-api-baseline.json");
        string? outputPath = Environment.GetEnvironmentVariable("NOTYPE_API_BASELINE_OUTPUT");
        ApiBaseline expected = PublicApiContract.ReadBaseline(baselinePath, allowEmptySignatures: !string.IsNullOrWhiteSpace(outputPath));
        ApiBaseline actual = PublicApiContract.Capture(repoRoot, expected);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            PublicApiContract.WriteCandidate(repoRoot, outputPath, actual);
            return;
        }

        IReadOnlyList<string> differences = PublicApiContract.Compare(expected, actual);
        Assert.True(differences.Count == 0, "Public API/dependency contract changed:" + Environment.NewLine + string.Join(Environment.NewLine, differences.Take(50)));
    }
}

public sealed class PublicApiContractSelfTests
{
    [Fact]
    public void WindowsDesktopResolver_UsesTheCurrentRuntimeMajor()
    {
        string scratch = Path.Combine(PublicApiContract.FindRepoRoot(), "artifacts", "api-self-test-" + Guid.NewGuid().ToString("N"));
        string frameworkRoot = Path.Combine(scratch, "Microsoft.WindowsDesktop.App");
        try
        {
            foreach (string version in new[] { "8.0.24", "9.0.1", "10.0.10" })
            {
                string directory = Path.Combine(frameworkRoot, version);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "PresentationCore.dll"), []);
            }

            string? selected = PublicApiContract.FindWindowsDesktopAssembly([frameworkRoot], "PresentationCore.dll", 8);
            Assert.Equal(Path.Combine(frameworkRoot, "8.0.24", "PresentationCore.dll"), selected);
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void SignatureFormatter_CoversComplexPublicContracts()
    {
        string[] signatures = PublicApiContract.SnapshotTypes([typeof(ApiFixture<>), typeof(ApiFixture<>.ProtectedNested), typeof(IExplicitFixture), typeof(ExplicitFixture)]);
        Assert.Contains(signatures, value => value.StartsWith("T|public|class|abstract ", StringComparison.Ordinal) && value.Contains("ApiFixture", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("generic=T:class&System.IDisposable&new()", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("M|", StringComparison.Ordinal) && value.Contains("ref System.Int32 value", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("out System.String? text", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("in System.Int32 input", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("optional=Second(2)", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("required", StringComparison.Ordinal) && value.Contains("init", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("RequiredName", StringComparison.Ordinal) && value.Contains("get:public virtual", StringComparison.Ordinal) && value.Contains("init:public virtual", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.Contains("tupleNames=Count,Text", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.StartsWith("T|protected-internal|", StringComparison.Ordinal) && value.Contains("ProtectedNested", StringComparison.Ordinal));
        Assert.Contains(signatures, value => value.StartsWith("M|", StringComparison.Ordinal) && value.Contains("IExplicitFixture|public|", StringComparison.Ordinal) && value.EndsWith("Run()->System.Void", StringComparison.Ordinal));
        Assert.DoesNotContain(signatures, value => value.Contains("ExplicitFixture|private|", StringComparison.Ordinal));
        Assert.DoesNotContain(signatures, value => value.Contains("get_RequiredName", StringComparison.Ordinal));
    }

    [Fact]
    public void BaselineReader_RejectsMalformedJson()
    {
        string repoRoot = PublicApiContract.FindRepoRoot();
        string scratch = Path.Combine(repoRoot, "artifacts", "api-self-test-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(scratch, "malformed.json");
        Directory.CreateDirectory(scratch);
        try
        {
            File.WriteAllText(path, "{ not-json", new UTF8Encoding(false));
            InvalidDataException error = Assert.Throws<InvalidDataException>(() => PublicApiContract.ReadBaseline(path));
            Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void Comparison_ReportsAssemblyTypeAndMemberChanges()
    {
        ApiBaseline baseline = FixtureBaseline(["T|public|class|Example.Widget", "M|Example.Widget|public|Run()->System.Void"]);
        ApiBaseline changed = FixtureBaseline(["T|public|class|Example.Widget", "M|Example.Widget|public|Run(System.Int32 value)->System.Void"]);
        IReadOnlyList<string> differences = PublicApiContract.Compare(baseline, changed);
        Assert.Contains(differences, value => value.Contains("removed API", StringComparison.Ordinal) && value.Contains("Run()", StringComparison.Ordinal));
        Assert.Contains(differences, value => value.Contains("added API", StringComparison.Ordinal) && value.Contains("Run(System.Int32 value)", StringComparison.Ordinal));
    }

    [Fact]
    public void BaselineValidation_RejectsEmptyAndCaseCollidingContracts()
    {
        ApiBaseline empty = new() { SchemaVersion = 1, Configuration = "Release", TargetFramework = "net8.0-windows", Assemblies = [] };
        Assert.Throws<InvalidDataException>(() => PublicApiContract.Validate(empty));

        ApiBaseline duplicate = FixtureBaseline(["T|public|class|Example.Widget", "t|PUBLIC|CLASS|example.widget"]);
        Assert.Throws<InvalidDataException>(() => PublicApiContract.Validate(duplicate));

        ApiBaseline wrongConfiguration = FixtureBaseline(["T|public|class|Example.Widget"]);
        wrongConfiguration.Configuration = "Debug";
        Assert.Throws<InvalidDataException>(() => PublicApiContract.Validate(wrongConfiguration));

        ApiBaseline missingApi = FixtureBaseline([]);
        Assert.Throws<InvalidDataException>(() => PublicApiContract.Validate(missingApi));
    }

    [Fact]
    public void DependencyValidation_RejectsCyclesProductionToToolAndCompiledMismatch()
    {
        Dictionary<string, DependencyFixture> cycle = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new("reusable-production-library", ["B"], ["B"]),
            ["B"] = new("reusable-production-library", ["A"], ["A"])
        };
        Assert.Contains(PublicApiContract.ValidateDependencyFixture(cycle), value => value.Contains("cycle", StringComparison.OrdinalIgnoreCase));

        Dictionary<string, DependencyFixture> invalid = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Product"] = new("reusable-production-library", ["Tool"], ["Tool", "Undeclared"]),
            ["Tool"] = new("developer-tool", [], []),
            ["Undeclared"] = new("reusable-production-library", [], [])
        };
        string[] failures = PublicApiContract.ValidateDependencyFixture(invalid).ToArray();
        Assert.Contains(failures, value => value.Contains("developer tool", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("Undeclared", StringComparison.Ordinal));
    }

    [Fact]
    public void Snapshot_IsOrdinalAndCultureInvariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            string[] first = PublicApiContract.SnapshotTypes([typeof(ApiFixture<>)]);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string[] second = PublicApiContract.SnapshotTypes([typeof(ApiFixture<>)]);
            Assert.Equal(first, second);
            Assert.Equal(first.OrderBy(value => value, StringComparer.Ordinal), first);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static ApiBaseline FixtureBaseline(string[] signatures) => new()
    {
        SchemaVersion = 1,
        Configuration = "Release",
        TargetFramework = "net8.0-windows",
        Assemblies =
        [
            new ApiAssemblyContract
            {
                Name = "Fixture",
                ProjectPath = "src/Fixture/Fixture.csproj",
                Role = "reusable-production-library",
                Owner = "fixture",
                ApiDisposition = "compiled-baseline",
                Consumers = ["Consumer"],
                Friends = [],
                DeclaredProjectReferences = [],
                DirectPackages = [],
                CompiledReferences = [],
                Signatures = signatures
            }
        ]
    };

    public enum FixtureEnum { First = -1, Second = 2 }

    public abstract class ApiFixture<T> where T : class, IDisposable, new()
    {
        public required virtual string RequiredName { get; init; }
        public (int Count, string? Text) Pair { get; init; }
        public virtual T? Transform(ref int value, out string? text, in int input, FixtureEnum optional = FixtureEnum.Second)
        {
            text = null;
            return null;
        }

        protected internal class ProtectedNested;
    }

    public interface IExplicitFixture { void Run(); }

    public sealed class ExplicitFixture : IExplicitFixture
    {
        void IExplicitFixture.Run() { }
    }
}

internal static class PublicApiContract
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DictateAnywhere.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    public static ApiBaseline ReadBaseline(string path, bool allowEmptySignatures = false)
    {
        try
        {
            ApiBaseline? baseline = JsonSerializer.Deserialize<ApiBaseline>(File.ReadAllText(path), JsonOptions);
            if (baseline is null) throw new InvalidDataException("Public API baseline is empty.");
            Validate(baseline, allowEmptySignatures);
            return baseline;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Public API baseline is malformed: {exception.Message}", exception);
        }
    }

    public static void Validate(ApiBaseline baseline, bool allowEmptySignatures = false)
    {
        if (baseline.SchemaVersion != 1) throw new InvalidDataException("Public API baseline schemaVersion must be 1.");
        if (!string.Equals(baseline.Configuration, "Release", StringComparison.Ordinal)) throw new InvalidDataException("Public API baseline must use Release.");
        if (!string.Equals(baseline.TargetFramework, "net8.0-windows", StringComparison.Ordinal)) throw new InvalidDataException("Public API baseline target framework changed.");
        if (baseline.Assemblies.Length == 0) throw new InvalidDataException("Public API baseline has zero assemblies.");
        EnsureUnique(baseline.Assemblies.Select(value => value.Name), "assembly", requireSorted: !allowEmptySignatures);
        EnsureUnique(baseline.Assemblies.Select(value => value.ProjectPath.Replace('\\', '/')), "project path", requireSorted: false);

        foreach (ApiAssemblyContract assembly in baseline.Assemblies)
        {
            if (string.IsNullOrWhiteSpace(assembly.Name) || string.IsNullOrWhiteSpace(assembly.ProjectPath) ||
                string.IsNullOrWhiteSpace(assembly.Role) || string.IsNullOrWhiteSpace(assembly.Owner))
            {
                throw new InvalidDataException("Every API assembly needs name, projectPath, role, and owner.");
            }

            if (assembly.ApiDisposition is not ("compiled-baseline" or "host-contract-only" or "tool-contract-only"))
                throw new InvalidDataException($"Assembly '{assembly.Name}' has invalid apiDisposition.");
            if (!allowEmptySignatures && assembly.ApiDisposition == "compiled-baseline" && assembly.Signatures.Length == 0)
                throw new InvalidDataException($"Assembly '{assembly.Name}' has zero public API signatures.");
            if (assembly.ApiDisposition != "compiled-baseline" && assembly.Signatures.Length != 0)
                throw new InvalidDataException($"Executable '{assembly.Name}' must not own a reusable API baseline.");
            EnsureUnique(assembly.Signatures, $"{assembly.Name} API signature");
            EnsureUnique(assembly.CompiledReferences, $"{assembly.Name} compiled reference");
            EnsureUnique(assembly.DeclaredProjectReferences, $"{assembly.Name} declared reference");
            EnsureUnique(assembly.DirectPackages, $"{assembly.Name} package");
            EnsureUnique(assembly.Consumers, $"{assembly.Name} consumer");
            EnsureUnique(assembly.Friends, $"{assembly.Name} friend");
        }
    }

    private static void EnsureUnique(IEnumerable<string> values, string label, bool requireSorted = true)
    {
        string[] items = values.ToArray();
        if (items.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"{label} contains an empty value.");
        if (items.Length != items.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidDataException($"{label} contains a duplicate or case-colliding value.");
        if (requireSorted && !items.SequenceEqual(items.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException($"{label} values are not ordinally sorted.");
    }

    public static ApiBaseline Capture(string repoRoot, ApiBaseline policy)
    {
        Dictionary<string, string> projectNames = FindProjects(repoRoot).ToDictionary(path => NormalizePath(repoRoot, path), GetAssemblyName, StringComparer.OrdinalIgnoreCase);
        string[] contractProjects = projectNames.Keys
            .Where(path => path.StartsWith("src/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("tools/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] policyProjects = policy.Assemblies.Select(value => value.ProjectPath.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string[] missingProjects = contractProjects.Except(policyProjects, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] staleProjects = policyProjects.Except(contractProjects, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missingProjects.Length != 0 || staleProjects.Length != 0)
        {
            throw new InvalidDataException($"API policy project inventory differs from the 17 tracked src/tools projects. Missing: [{string.Join(", ", missingProjects)}]. Stale: [{string.Join(", ", staleProjects)}].");
        }
        Dictionary<string, List<string>> consumers = projectNames.Values.ToDictionary(name => name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach ((string projectPath, string consumerName) in projectNames)
        {
            foreach (string reference in ReadProjectReferences(repoRoot, projectPath, projectNames))
            {
                if (consumers.TryGetValue(reference, out List<string>? list)) list.Add(consumerName);
            }
        }

        Func<AssemblyLoadContext, AssemblyName, Assembly?> resolver = (_, requested) => ResolveAssembly(repoRoot, requested);
        AssemblyLoadContext.Default.Resolving += resolver;
        ApiAssemblyContract[] captured;
        try
        {
            captured = policy.Assemblies
                .OrderBy(value => value.Name, StringComparer.Ordinal)
                .Select(entry => CaptureAssembly(repoRoot, entry, projectNames, consumers))
                .ToArray();
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= resolver;
        }
        ApiBaseline result = new() { SchemaVersion = 1, Configuration = "Release", TargetFramework = "net8.0-windows", Assemblies = captured };
        Validate(result);
        ValidateRepositoryDependencies(result);
        return result;
    }

    private static ApiAssemblyContract CaptureAssembly(
        string repoRoot,
        ApiAssemblyContract policy,
        IReadOnlyDictionary<string, string> projectNames,
        IReadOnlyDictionary<string, List<string>> consumers)
    {
        string normalizedProject = policy.ProjectPath.Replace('\\', '/');
        string projectFullPath = Path.Combine(repoRoot, normalizedProject.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(projectFullPath)) throw new InvalidDataException($"API project is missing: {normalizedProject}");
        string assemblyName = GetAssemblyName(projectFullPath);
        if (!string.Equals(assemblyName, policy.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"Project '{normalizedProject}' builds '{assemblyName}', expected '{policy.Name}'.");
        string assemblyPath = Path.Combine(Path.GetDirectoryName(projectFullPath)!, "bin", "Release", "net8.0-windows", assemblyName + ".dll");
        if (!File.Exists(assemblyPath)) throw new InvalidDataException($"Release assembly is missing: {normalizedProject}. Run the Release build first.");

        string[] signatures = policy.ApiDisposition == "compiled-baseline" ? SnapshotAssembly(Assembly.LoadFrom(assemblyPath)) : [];
        string[] friends = ReadFriends(projectFullPath);
        return new ApiAssemblyContract
        {
            Name = policy.Name,
            ProjectPath = normalizedProject,
            Role = policy.Role,
            Owner = policy.Owner,
            ApiDisposition = policy.ApiDisposition,
            Consumers = consumers.GetValueOrDefault(policy.Name, []).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Friends = friends,
            DeclaredProjectReferences = ReadProjectReferences(repoRoot, normalizedProject, projectNames),
            DirectPackages = ReadDirectPackages(projectFullPath),
            CompiledReferences = ReadCompiledReferences(assemblyPath),
            Signatures = signatures
        };
    }

    private static Assembly? ResolveAssembly(string repoRoot, AssemblyName requested)
    {
        string fileName = requested.Name + ".dll";
        string[] repositoryMatches = new[] { "src", "tools" }
            .SelectMany(root => Directory.GetFiles(Path.Combine(repoRoot, root), fileName, SearchOption.AllDirectories))
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}net8.0-windows{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (repositoryMatches.Length > 0) return AssemblyLoadContext.Default.LoadFromAssemblyPath(repositoryMatches[0]);

        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        DirectoryInfo? sharedRoot = Directory.GetParent(runtimeDirectory)?.Parent;
        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string programFilesDotnet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet");
        string? frameworkPath = FindWindowsDesktopAssembly(
            [
                Path.Combine(sharedRoot?.FullName ?? string.Empty, "Microsoft.WindowsDesktop.App"),
                Path.Combine(dotnetRoot ?? string.Empty, "shared", "Microsoft.WindowsDesktop.App"),
                Path.Combine(programFilesDotnet, "shared", "Microsoft.WindowsDesktop.App")
            ],
            fileName,
            Environment.Version.Major);
        return frameworkPath is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(frameworkPath);
    }

    internal static string? FindWindowsDesktopAssembly(IEnumerable<string> frameworkRoots, string fileName, int runtimeMajor) => frameworkRoots
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .SelectMany(Directory.GetDirectories)
        .Select(path => new { Path = path, Version = Version.TryParse(Path.GetFileName(path), out Version? version) ? version : null })
        .Where(entry => entry.Version?.Major == runtimeMajor)
        .OrderByDescending(entry => entry.Version)
        .Select(entry => Path.Combine(entry.Path, fileName))
        .FirstOrDefault(File.Exists);

    private static string[] ReadFriends(string projectPath)
    {
        string? directory = Path.GetDirectoryName(projectPath);
        if (directory is null) return [];
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => System.Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(path),
                "InternalsVisibleTo\\(\\\"(?<name>[^\\\",]+)",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant).Select(match => match.Groups["name"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] FindProjects(string repoRoot) => [.. new[] { "src", "tools", "tests" }
        .SelectMany(root => Directory.GetFiles(Path.Combine(repoRoot, root), "*.csproj", SearchOption.AllDirectories))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains($"{Path.DirectorySeparatorChar}TestResults{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                       !path.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase))
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

    private static string NormalizePath(string repoRoot, string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

    private static string GetAssemblyName(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        return document.Descendants("AssemblyName").Select(value => value.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Path.GetFileNameWithoutExtension(projectPath);
    }

    private static string[] ReadProjectReferences(string repoRoot, string projectPath, IReadOnlyDictionary<string, string> projectNames)
    {
        string fullPath = Path.IsPathRooted(projectPath) ? projectPath : Path.Combine(repoRoot, projectPath.Replace('/', Path.DirectorySeparatorChar));
        XDocument document = XDocument.Load(fullPath);
        return document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizePath(repoRoot, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath)!, value!))))
            .Select(path => projectNames.TryGetValue(path, out string? name) ? name : throw new InvalidDataException($"Project reference has no tracked project: {path}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ReadDirectPackages(string projectPath) => XDocument.Load(projectPath).Descendants("PackageReference")
        .Select(element => element.Attribute("Include")?.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static string[] ReadCompiledReferences(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader reader = new(stream);
        if (!reader.HasMetadata) throw new InvalidDataException($"Compiled assembly has no metadata: {assemblyPath}");
        MetadataReader metadata = reader.GetMetadataReader();
        return metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SnapshotAssembly(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            throw new InvalidDataException($"Could not load API types for '{assembly.GetName().Name}': {string.Join(" | ", exception.LoaderExceptions.Where(value => value is not null).Select(value => value!.Message))}", exception);
        }
        return SnapshotTypes(types.Where(IsContractType));
    }

    public static string[] SnapshotTypes(IEnumerable<Type> types) => types
        .Where(IsContractType)
        .SelectMany(SnapshotType)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static bool IsContractType(Type type)
    {
        if (type.IsDefined(typeof(CompilerGeneratedAttribute), false) || type.Name.Contains('<', StringComparison.Ordinal)) return false;
        if (type.IsPublic) return true;
        if (!(type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem)) return false;
        return type.DeclaringType is not null && IsContractType(type.DeclaringType);
    }

    private static IEnumerable<string> SnapshotType(Type type)
    {
        yield return $"T|{TypeAccess(type)}|{TypeKind(type)}|{FormatTypeModifiers(type)}{FormatType(type)}|base={FormatOptionalType(type.BaseType)}|interfaces={string.Join(',', type.GetInterfaces().Select(value => FormatType(value)).OrderBy(value => value, StringComparer.Ordinal))}|generic={FormatGenericParameters(type.GetGenericArguments().Where(value => value.DeclaringMethod is null))}";
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        NullabilityInfoContext nullability = new();
        HashSet<MethodInfo> accessors = type.GetProperties(flags).SelectMany(property => property.GetAccessors(true))
            .Concat(type.GetEvents(flags).SelectMany(@event => new[] { @event.AddMethod, @event.RemoveMethod, @event.RaiseMethod }.OfType<MethodInfo>()))
            .ToHashSet();

        foreach (ConstructorInfo constructor in type.GetConstructors(flags).Where(IsVisibleMember))
            yield return $"C|{FormatType(type)}|{MemberAccess(constructor)}|{FormatMethodModifiers(constructor)}.ctor({FormatParameters(constructor.GetParameters(), nullability)})";
        foreach (MethodInfo method in type.GetMethods(flags).Where(IsVisibleMember).Where(method => !accessors.Contains(method)))
            yield return $"M|{FormatType(type)}|{MemberAccess(method)}|{FormatMethodModifiers(method)}{method.Name}{FormatMethodGeneric(method)}({FormatParameters(method.GetParameters(), nullability)})->{FormatType(method.ReturnType, nullability.Create(method.ReturnParameter))}{FormatTupleNames(method.ReturnParameter.GetCustomAttributesData())}";
        foreach (PropertyInfo property in type.GetProperties(flags).Where(property => property.GetAccessors(true).Any(IsVisibleMember)))
        {
            MethodInfo? getter = property.GetMethod is not null && IsVisibleMember(property.GetMethod) ? property.GetMethod : null;
            MethodInfo? setter = property.SetMethod is not null && IsVisibleMember(property.SetMethod) ? property.SetMethod : null;
            string getText = getter is null ? "" : $"get:{FormatAccessor(getter)}";
            string setText = setter is null ? "" : $"{(IsInitOnly(setter) ? "init" : "set")}:{FormatAccessor(setter)}";
            string required = property.IsDefined(typeof(RequiredMemberAttribute), false) ? "required " : "";
            yield return $"P|{FormatType(type)}|{required}{property.Name}[{FormatParameters(property.GetIndexParameters(), nullability)}]:{FormatType(property.PropertyType, nullability.Create(property))}{FormatTupleNames(property.GetCustomAttributesData())}|{getText}|{setText}";
        }
        foreach (EventInfo @event in type.GetEvents(flags).Where(@event => new[] { @event.AddMethod, @event.RemoveMethod }.Any(method => method is not null && IsVisibleMember(method))))
            yield return $"E|{FormatType(type)}|{@event.Name}:{FormatType(@event.EventHandlerType!)}|add:{FormatAccessor(@event.AddMethod!)}|remove:{FormatAccessor(@event.RemoveMethod!)}";
        foreach (FieldInfo field in type.GetFields(flags).Where(IsVisibleMember))
            yield return $"F|{FormatType(type)}|{MemberAccess(field)}|{(field.IsDefined(typeof(RequiredMemberAttribute), false) ? "required " : "")}{(field.IsStatic ? "static " : "")}{(field.IsInitOnly ? "readonly " : "")}{(field.IsLiteral ? "const " : "")}{field.Name}:{FormatType(field.FieldType, nullability.Create(field))}{FormatTupleNames(field.GetCustomAttributesData())}{(field.IsLiteral ? "=" + FormatLiteral(field.GetRawConstantValue(), field.FieldType) : "")}";
    }

    private static bool IsVisibleMember(MethodBase member) => member.IsPublic || member.IsFamily || member.IsFamilyOrAssembly;
    private static bool IsVisibleMember(FieldInfo member) => member.IsPublic || member.IsFamily || member.IsFamilyOrAssembly;
    private static string TypeAccess(Type type) => type.IsPublic || type.IsNestedPublic ? "public" : type.IsNestedFamily ? "protected" : "protected-internal";
    private static string MemberAccess(MethodBase member) => member.IsPublic ? "public" : member.IsFamily ? "protected" : "protected-internal";
    private static string MemberAccess(FieldInfo member) => member.IsPublic ? "public" : member.IsFamily ? "protected" : "protected-internal";
    private static string TypeKind(Type type) => type.IsEnum ? "enum" : typeof(MulticastDelegate).IsAssignableFrom(type.BaseType) ? "delegate" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
    private static string FormatTypeModifiers(Type type)
    {
        List<string> values = [];
        if (type.IsAbstract && type.IsSealed) values.Add("static");
        else
        {
            if (type.IsAbstract && !type.IsInterface) values.Add("abstract");
            if (type.IsSealed && !type.IsValueType) values.Add("sealed");
        }
        if (type.IsByRefLike) values.Add("ref-like");
        if (type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute), false)) values.Add("readonly");
        return values.Count == 0 ? "" : string.Join(' ', values) + " ";
    }
    private static string FormatOptionalType(Type? type) => type is null ? "-" : FormatType(type);

    private static string FormatType(Type type, NullabilityInfo? nullable = null)
    {
        if (type.IsByRef) return FormatType(type.GetElementType()!, nullable?.ElementType) + "&";
        if (type.IsPointer) return FormatType(type.GetElementType()!, nullable?.ElementType) + "*";
        if (type.IsArray) return FormatType(type.GetElementType()!, nullable?.ElementType) + "[" + new string(',', type.GetArrayRank() - 1) + "]" + NullableSuffix(type, nullable);
        if (type.IsGenericParameter) return type.Name + NullableSuffix(type, nullable);
        NullabilityInfo[] nullableArguments = nullable?.GenericTypeArguments ?? [];
        Type[] genericArguments = type.IsGenericType ? type.GetGenericArguments() : [];
        int inheritedArgumentCount = type.IsNested && type.DeclaringType?.IsGenericType == true
            ? type.DeclaringType.GetGenericArguments().Length
            : 0;
        string simpleName = type.Name;
        int tick = simpleName.IndexOf('`');
        if (tick >= 0) simpleName = simpleName[..tick];
        string name = type.IsNested
            ? FormatType(type.DeclaringType!) + "." + simpleName
            : string.IsNullOrWhiteSpace(type.Namespace) ? simpleName : type.Namespace + "." + simpleName;
        Type[] ownArguments = genericArguments.Skip(inheritedArgumentCount).ToArray();
        if (ownArguments.Length > 0)
        {
            string arguments = string.Join(',', ownArguments.Select((argument, index) =>
                FormatType(argument, inheritedArgumentCount + index < nullableArguments.Length ? nullableArguments[inheritedArgumentCount + index] : null)));
            name += "<" + arguments + ">";
        }
        return name + NullableSuffix(type, nullable);
    }

    private static string NullableSuffix(Type type, NullabilityInfo? nullable) => !type.IsValueType && nullable?.ReadState == NullabilityState.Nullable ? "?" : "";

    private static string FormatGenericParameters(IEnumerable<Type> parameters) => string.Join(';', parameters.Select(parameter =>
    {
        GenericParameterAttributes attributes = parameter.GenericParameterAttributes;
        List<string> constraints = [];
        GenericParameterAttributes variance = attributes & GenericParameterAttributes.VarianceMask;
        if (variance == GenericParameterAttributes.Covariant) constraints.Add("out");
        if (variance == GenericParameterAttributes.Contravariant) constraints.Add("in");
        GenericParameterAttributes special = attributes & GenericParameterAttributes.SpecialConstraintMask;
        if (special.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint)) constraints.Add("class");
        if (special.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) constraints.Add("struct");
        constraints.AddRange(parameter.GetGenericParameterConstraints().Select(value => FormatType(value)).OrderBy(value => value, StringComparer.Ordinal));
        if (special.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !special.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)) constraints.Add("new()");
        return parameter.Name + ":" + string.Join('&', constraints);
    }));

    private static string FormatMethodGeneric(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition) return "";
        Type[] arguments = method.GetGenericArguments();
        return "<" + string.Join(',', arguments.Select(value => value.Name)) + ">{" + FormatGenericParameters(arguments) + "}";
    }

    private static string FormatMethodModifiers(MethodBase method)
    {
        List<string> values = [];
        if (method.IsStatic) values.Add("static");
        if (method.IsAbstract) values.Add("abstract");
        else if (method.IsVirtual)
        {
            MethodInfo info = (MethodInfo)method;
            if (info.GetBaseDefinition() != info) values.Add("override"); else values.Add("virtual");
            if (method.IsFinal) values.Add("sealed");
        }
        return values.Count == 0 ? "" : string.Join(' ', values) + " ";
    }

    private static string FormatAccessor(MethodInfo accessor)
    {
        string modifiers = FormatMethodModifiers(accessor).TrimEnd();
        return MemberAccess(accessor) + (modifiers.Length == 0 ? "" : " " + modifiers);
    }

    private static string FormatParameters(ParameterInfo[] parameters, NullabilityInfoContext nullability) => string.Join(',', parameters.Select(parameter =>
    {
        string modifier = parameter.GetCustomAttribute<ParamArrayAttribute>() is not null ? "params " : parameter.ParameterType.IsByRef
            ? parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref " : "";
        Type valueType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        string defaultValue = parameter.HasDefaultValue ? "=" + FormatLiteral(parameter.DefaultValue, valueType) : "";
        return modifier + FormatType(valueType, nullability.Create(parameter)) + " " + parameter.Name + FormatTupleNames(parameter.GetCustomAttributesData()) + defaultValue;
    }));

    private static string FormatTupleNames(IList<CustomAttributeData> attributes)
    {
        CustomAttributeData? tupleNames = attributes.FirstOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Runtime.CompilerServices.TupleElementNamesAttribute");
        if (tupleNames is null || tupleNames.ConstructorArguments.Count != 1 ||
            tupleNames.ConstructorArguments[0].Value is not IReadOnlyCollection<CustomAttributeTypedArgument> names)
        {
            return "";
        }

        return "|tupleNames=" + string.Join(',', names.Select(name => name.Value as string ?? "_"));
    }

    private static string FormatLiteral(object? value, Type type)
    {
        if (value is null) return "null";
        if (value == DBNull.Value || value == Missing.Value) return "missing";
        if (type.IsEnum)
        {
            string? name = Enum.GetName(type, value);
            object numeric = Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
            return (name ?? "?") + "(" + Convert.ToString(numeric, CultureInfo.InvariantCulture) + ")";
        }
        return value switch
        {
            string text => JsonSerializer.Serialize(text),
            char character => JsonSerializer.Serialize(character.ToString()),
            bool boolean => boolean ? "true" : "false",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
        };
    }

    private static bool IsInitOnly(MethodInfo setter) => setter.ReturnParameter.GetRequiredCustomModifiers().Any(type => type.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static void ValidateRepositoryDependencies(ApiBaseline baseline)
    {
        Dictionary<string, ApiAssemblyContract> assemblies = baseline.Assemblies.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
        foreach (ApiAssemblyContract assembly in baseline.Assemblies)
        {
            foreach (string compiled in assembly.CompiledReferences.Where(assemblies.ContainsKey))
            {
                if (!assembly.DeclaredProjectReferences.Contains(compiled, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{assembly.Name} has undeclared compiled repository reference '{compiled}'.");
            }
            if (!assembly.Role.Contains("tool", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string reference in assembly.DeclaredProjectReferences)
                    if (assemblies.TryGetValue(reference, out ApiAssemblyContract? target) && target.Role == "developer-tool")
                        throw new InvalidDataException($"Production assembly '{assembly.Name}' references developer tool '{reference}'.");
            }
        }
        string[] cycles = ValidateDependencyFixture(assemblies.ToDictionary(
            pair => pair.Key,
            pair => new DependencyFixture(pair.Value.Role, pair.Value.DeclaredProjectReferences, pair.Value.CompiledReferences),
            StringComparer.OrdinalIgnoreCase)).Where(value => value.Contains("cycle", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cycles.Length > 0) throw new InvalidDataException(cycles[0]);
    }

    public static IEnumerable<string> ValidateDependencyFixture(IReadOnlyDictionary<string, DependencyFixture> graph)
    {
        List<string> failures = [];
        foreach ((string name, DependencyFixture node) in graph)
        {
            foreach (string compiled in node.CompiledReferences.Where(graph.ContainsKey))
                if (!node.DeclaredReferences.Contains(compiled, StringComparer.OrdinalIgnoreCase)) failures.Add($"{name} has undeclared compiled reference {compiled}.");
            if (!node.Role.Contains("tool", StringComparison.OrdinalIgnoreCase))
                foreach (string reference in node.DeclaredReferences)
                    if (graph.TryGetValue(reference, out DependencyFixture? target) && target.Role == "developer-tool") failures.Add($"{name} references developer tool {reference}.");
        }
        Dictionary<string, int> states = new(StringComparer.OrdinalIgnoreCase);
        List<string> stack = [];
        void Visit(string name)
        {
            if (states.GetValueOrDefault(name) == 1) { failures.Add("Dependency cycle: " + string.Join(" -> ", stack.Append(name))); return; }
            if (states.GetValueOrDefault(name) == 2 || !graph.TryGetValue(name, out DependencyFixture? node)) return;
            states[name] = 1; stack.Add(name);
            foreach (string reference in node.DeclaredReferences) Visit(reference);
            stack.RemoveAt(stack.Count - 1); states[name] = 2;
        }
        foreach (string name in graph.Keys) Visit(name);
        return failures;
    }

    public static IReadOnlyList<string> Compare(ApiBaseline expected, ApiBaseline actual)
    {
        List<string> differences = [];
        Dictionary<string, ApiAssemblyContract> expectedAssemblies = expected.Assemblies.ToDictionary(value => value.Name, StringComparer.Ordinal);
        Dictionary<string, ApiAssemblyContract> actualAssemblies = actual.Assemblies.ToDictionary(value => value.Name, StringComparer.Ordinal);
        foreach (string removed in expectedAssemblies.Keys.Except(actualAssemblies.Keys, StringComparer.Ordinal)) differences.Add($"removed assembly: {removed}");
        foreach (string added in actualAssemblies.Keys.Except(expectedAssemblies.Keys, StringComparer.Ordinal)) differences.Add($"added assembly: {added}");
        foreach (string name in expectedAssemblies.Keys.Intersect(actualAssemblies.Keys, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            ApiAssemblyContract left = expectedAssemblies[name]; ApiAssemblyContract right = actualAssemblies[name];
            CompareScalar(differences, name, "projectPath", left.ProjectPath, right.ProjectPath);
            CompareScalar(differences, name, "role", left.Role, right.Role);
            CompareScalar(differences, name, "owner", left.Owner, right.Owner);
            CompareScalar(differences, name, "apiDisposition", left.ApiDisposition, right.ApiDisposition);
            CompareSet(differences, name, "consumer", left.Consumers, right.Consumers);
            CompareSet(differences, name, "friend", left.Friends, right.Friends);
            CompareSet(differences, name, "declared project reference", left.DeclaredProjectReferences, right.DeclaredProjectReferences);
            CompareSet(differences, name, "direct package", left.DirectPackages, right.DirectPackages);
            CompareSet(differences, name, "compiled reference", left.CompiledReferences, right.CompiledReferences);
            CompareSet(differences, name, "API", left.Signatures, right.Signatures);
        }
        return differences;
    }

    private static void CompareScalar(List<string> differences, string assembly, string label, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal)) differences.Add($"{assembly}: changed {label}: '{expected}' -> '{actual}'");
    }

    private static void CompareSet(List<string> differences, string assembly, string label, IEnumerable<string> expected, IEnumerable<string> actual)
    {
        foreach (string removed in expected.Except(actual, StringComparer.Ordinal)) differences.Add($"{assembly}: removed {label}: {removed}");
        foreach (string added in actual.Except(expected, StringComparer.Ordinal)) differences.Add($"{assembly}: added {label}: {added}");
    }

    public static void WriteCandidate(string repoRoot, string outputPath, ApiBaseline baseline)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string artifactsRoot = Path.GetFullPath(Path.Combine(repoRoot, "artifacts")) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(artifactsRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Candidate output must be under ignored artifacts.");
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) throw new InvalidDataException("Candidate output already exists.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(baseline, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }
}

internal sealed class ApiBaseline
{
    public int SchemaVersion { get; set; }
    public string Configuration { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public ApiAssemblyContract[] Assemblies { get; set; } = [];
}

internal sealed class ApiAssemblyContract
{
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Role { get; set; } = "";
    public string Owner { get; set; } = "";
    public string ApiDisposition { get; set; } = "";
    public string[] Consumers { get; set; } = [];
    public string[] Friends { get; set; } = [];
    public string[] DeclaredProjectReferences { get; set; } = [];
    public string[] DirectPackages { get; set; } = [];
    public string[] CompiledReferences { get; set; } = [];
    public string[] Signatures { get; set; } = [];
}

internal sealed record DependencyFixture(string Role, string[] DeclaredReferences, string[] CompiledReferences);
