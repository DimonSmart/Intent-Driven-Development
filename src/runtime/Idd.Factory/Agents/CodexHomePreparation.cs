using System.Text.Json;
using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

internal sealed record CodexPrivateHome(string Path, int InheritedSkillCount);

internal sealed class CodexHomePreparation(
    string pluginRoot,
    AgentCapabilityPolicy capabilityPolicy)
{
    private readonly string pluginRoot = Path.GetFullPath(pluginRoot);

    public CodexPrivateHome Prepare(string runId, string attemptId, string selectedSkill)
    {
        var home = Path.Combine(Path.GetTempPath(), "idd-factory", "codex-private", runId, attemptId);
        CleanupDirectory(home);
        Directory.CreateDirectory(home);
        var configuredHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var sourceHome = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configuredHome;
        var sourceAuth = Path.Combine(sourceHome, "auth.json");
        if (File.Exists(sourceAuth))
            File.Copy(sourceAuth, Path.Combine(home, "auth.json"), overwrite: true);

        var inheritedSkillCount = 0;
        var sourceSkills = Path.Combine(sourceHome, "skills");
        if (capabilityPolicy.InheritUserSkills && Directory.Exists(sourceSkills))
        {
            foreach (var sourceSkill in Directory.EnumerateDirectories(sourceSkills))
            {
                if (!ShouldInheritSkill(Path.GetFileName(sourceSkill), selectedSkill)) continue;
                CopyDirectory(sourceSkill, Path.Combine(home, "skills", Path.GetFileName(sourceSkill)));
                inheritedSkillCount++;
            }
        }
        return new(home, inheritedSkillCount);
    }

    public string ReadSkillInstructions(AgentInvocation invocation) =>
        ReadSkillInstructions(pluginRoot, invocation);

    public string ReadSkillSourceVersion()
    {
        var path = Path.Combine(
            pluginRoot,
            "skills",
            "idd-factory-run",
            "references",
            "methodology-version.json");
        if (!File.Exists(path)) return "unknown";
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("methodologyVersion", out var value)
                ? value.GetString() ?? "unknown"
                : "unknown";
        }
        catch (JsonException)
        {
            return "unknown";
        }
    }

    public static string ReadSkillInstructions(string pluginRoot, AgentInvocation invocation)
    {
        ValidateSkillIdentity(pluginRoot, invocation);
        var path = Path.Combine(
            Path.GetFullPath(pluginRoot),
            "skills",
            invocation.SkillName,
            "SKILL.md");
        var instructions = File.ReadAllText(path).Trim();
        if (instructions.Length == 0)
            throw new AgentProtocolException(
                "FACTORY_SKILL_UNAVAILABLE",
                $"Factory skill {invocation.SkillName} is empty in the configured plugin.");
        return instructions;
    }

    public static void ValidateSkillIdentity(string pluginRoot, AgentInvocation invocation)
    {
        var skillName = invocation.SkillName;
        var source = Path.GetFullPath(Path.Combine(pluginRoot, "skills", skillName));
        var skillsRoot = Path.GetFullPath(Path.Combine(pluginRoot, "skills")) + Path.DirectorySeparatorChar;
        if (!source.StartsWith(skillsRoot, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(source, "SKILL.md")))
        {
            throw new AgentProtocolException(
                "FACTORY_SKILL_UNAVAILABLE",
                $"Factory skill {skillName} is not available in the configured plugin.");
        }
        var projectSkill = Path.Combine(invocation.Workspace, ".agents", "skills", skillName, "SKILL.md");
        if (File.Exists(projectSkill))
            throw new AgentProtocolException(
                "FACTORY_SKILL_COLLISION",
                $"Project-local skill {skillName} conflicts with the runtime-selected Factory skill.");
    }

    public static bool ShouldInheritSkill(string candidateSkill, string selectedFactorySkill) =>
        !string.Equals(candidateSkill, selectedFactorySkill, StringComparison.OrdinalIgnoreCase);

    public static int CountProjectSkills(string workspace)
    {
        var root = Path.Combine(workspace, ".agents", "skills");
        return Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Count(path => File.Exists(Path.Combine(path, "SKILL.md")))
            : 0;
    }

    public static void CleanupDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    public static void TryCleanupDirectory(string path, string resultPath)
    {
        try
        {
            CleanupDirectory(path);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(resultPath)!, "cleanup-warning.log"),
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            catch (Exception diagnosticException) when (
                diagnosticException is UnauthorizedAccessException or IOException)
            {
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
