using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed partial class SmokeTestSuite
{
    private readonly string repoRoot = FindRepoRoot();
    private readonly FailureCollector failures = new();
    private readonly string generatorDll;
    private readonly string toolDll;

    public SmokeTestSuite()
    {
        generatorDll = Path.Combine(repoRoot, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
        toolDll = Path.Combine(repoRoot, "tools", "idd-tool", "bin", "Debug", "net10.0", "IntentDrivenDevelopment.Tool.dll");
    }

    public int Run()
    {
        RunGenerator();
        
        ExpectManifestShape();
        ExpectFile("generated/codex/AGENTS.md");
        
        ExpectFile("generated/claude/CLAUDE.md");
        
        ExpectFile("generated/gemini/GEMINI.md");
        ExpectNoDirectory("generated/gemini/.agents");
        ExpectNoDirectory("generated/gemini/.claude");
        ExpectNoDirectory("generated/gemini/.github/skills");
        
        ExpectFile("generated/copilot/.github/copilot-instructions.md");
        
        ExpectNoGeneratedHeaderComments();
        ExpectNoEntryIncludes("generated/claude/CLAUDE.md", "AGENTS.md");
        ExpectNoEntryIncludes("generated/gemini/GEMINI.md", "AGENTS.md");
        ExpectEntryPointLineLimits();
        ExpectAllSkillsGenerated();
        ExpectClaudeSkillMetadata();
        ExpectPackManifestShape();
        ExpectNoLegacyPublicNames();
        ExpectFactoryGeneratedShape();
        ExpectFactoryRolePromptReferences();
        ExpectListPacks();
        ExpectListCodingAgents();
        ExpectDefaultInstallCoreOnly();
        ExpectFactoryInstall();
        ExpectFactoryUnsupportedTargetRejected();
        ExpectInstallEntryNone();
        ExpectInstallGeminiEntryNoneRejected();
        ExpectInstallAllAfterInit();
        ExpectNpmListTargets();
        ExpectNpmListCodingAgents();
        ExpectNpmListPacks();
        ExpectNpmInstallDefaultMinimal();
        ExpectNpmInstallDefaultCoreOnly();
        ExpectNpmInstallFactory();
        ExpectNpmInstallEntryNone();
        ExpectNpmInstallEntryFull();
        ExpectNpmRejectsGeminiEntryNone();
        ExpectNpmRejectsFactoryForGemini();
        ExpectNpmRejectsUnknownPack();
        ExpectNpmRejectsUnknownEntryMode();
        ExpectGeneratorCheckPasses();
        ExpectSecondRunStable();
        

        if (failures.HasFailures)
        {
            return failures.PrintAndReturnExitCode();
        }

        Console.WriteLine("Smoke tests passed.");
        return 0;
    }

}
