using System.Text.RegularExpressions;

namespace Idd.Factory.Tests;

public sealed class ProcessArchitectureTests
{
    [Fact]
    public void ProductionExternalProcessLifecycleIsOwnedByProcessesDirectory()
    {
        var productionRoot = Path.Combine(
            RepositoryRoot(),
            "src",
            "runtime",
            "Idd.Factory");
        var processesRoot = Path.Combine(productionRoot, "Processes") + Path.DirectorySeparatorChar;

        foreach (var file in Directory.EnumerateFiles(productionRoot, "*.cs", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (fullPath.StartsWith(processesRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(productionRoot, fullPath).Replace('\\', '/');
            var source = File.ReadAllText(fullPath);
            AssertForbidden(relative, source, "ProcessStartInfo");
            Assert.False(
                Regex.IsMatch(
                    source,
                    @"\bnew\s+(?:System\.Diagnostics\.)?Process\s*[\({]",
                    RegexOptions.CultureInvariant),
                $"{relative} creates System.Diagnostics.Process outside Processes/.");
            AssertForbidden(relative, source, "Process.Start(");
            AssertForbidden(relative, source, "System.Diagnostics.Process.Start(");

            var canReferToRawProcess = source.Contains(
                    "using System.Diagnostics;",
                    StringComparison.Ordinal)
                || source.Contains(
                    "System.Diagnostics.Process",
                    StringComparison.Ordinal);
            if (!canReferToRawProcess)
                continue;

            AssertForbidden(relative, source, ".WaitForExit(");
            AssertForbidden(relative, source, ".WaitForExitAsync(");
            AssertForbidden(relative, source, ".Kill(");
            Assert.False(
                Regex.IsMatch(
                    source,
                    @"\b(?:process|startedProcess)\.Standard(?:Output|Error|Input)\b",
                    RegexOptions.CultureInvariant),
                $"{relative} directly accesses redirected process streams outside Processes/.");
        }
    }

    private static void AssertForbidden(string relative, string source, string token) =>
        Assert.DoesNotContain(
            token,
            source,
            StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    "src",
                    "runtime",
                    "Idd.Factory")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
