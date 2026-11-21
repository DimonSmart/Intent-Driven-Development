using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Idd.Factory.Report;

public static class ReportApplication
{
    public static int Run(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(CliOptions.Help);
                return 0;
            }
            if (options.ShowVersion)
            {
                Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");
                return 0;
            }

            var repository = RepositoryLocator.Resolve(options.Repository);
            var codexHome = CodexHomeLocator.Resolve(options.CodexHome);
            var reports = new FactoryReportEngine().FindRuns(repository, codexHome, options.Verbose);

            if (options.Command == "list")
            {
                ReportWriters.WriteList(reports, Console.Out);
                return 0;
            }

            FactoryRunReport? report;
            if (options.Command == "latest")
            {
                report = reports.OrderByDescending(x => x.Run.StartedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
            }
            else
            {
                var threadReports = reports.Where(x => x.Run.RootThreadId == options.Command)
                    .OrderBy(x => x.Run.RunIndex).ToArray();
                if (options.RunIndex is not null)
                    report = threadReports.FirstOrDefault(x => x.Run.RunIndex == options.RunIndex);
                else
                {
                    report = threadReports.LastOrDefault();
                    if (report is not null && threadReports.Length > 1)
                    {
                        report.Diagnostics.Add(new Diagnostic
                        {
                            Severity = "warning",
                            Category = "reporter",
                            Code = "multiple_runs_in_thread",
                            Message = $"Thread contains {threadReports.Length} Factory runs; the latest was selected. Use --run <N>."
                        });
                    }
                }
            }

            if (report is null)
            {
                Console.Error.WriteLine("No matching Factory run was found.");
                return 2;
            }

            ReportWriters.WriteConsole(report, Console.Out, options.Verbose);
            if (options.JsonPath is not null)
                ReportWriters.WriteJson(report, options.JsonPath);
            if (options.MarkdownPath is not null)
                ReportWriters.WriteMarkdown(report, options.MarkdownPath, options.Verbose);
            return 0;
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"idd-factory-report failed: {ex.Message}");
            return 1;
        }
    }
}

public sealed class CliOptions
{
    public string Command { get; private set; } = "latest";
    public string Repository { get; private set; } = ".";
    public string? CodexHome { get; private set; }
    public int? RunIndex { get; private set; }
    public string? JsonPath { get; private set; }
    public string? MarkdownPath { get; private set; }
    public bool Verbose { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }

    public const string Help = """
idd-factory-report - diagnostics for ordinary IDD Factory runs

Usage:
  idd-factory-report latest [options]
  idd-factory-report list [options]
  idd-factory-report <thread-id> [--run N] [options]

Options:
  --repo <path>         Repository or a path inside it (default: .)
  --codex-home <path>   Override CODEX_HOME
  --run <N>             Factory run index inside an explicit thread
  --json <file>         Write normalized JSON report
  --markdown <file>     Write Markdown report
  --verbose             Show linkage/parser details and rollout paths
  --help                Show help
  --version             Show version
""";

    public static CliOptions Parse(string[] args)
    {
        var result = new CliOptions();
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    result.ShowHelp = true;
                    break;
                case "--version":
                    result.ShowVersion = true;
                    break;
                case "--verbose":
                    result.Verbose = true;
                    break;
                case "--repo":
                    result.Repository = Value(args, ref i, arg);
                    break;
                case "--codex-home":
                    result.CodexHome = Value(args, ref i, arg);
                    break;
                case "--json":
                    result.JsonPath = Value(args, ref i, arg);
                    break;
                case "--markdown":
                    result.MarkdownPath = Value(args, ref i, arg);
                    break;
                case "--run":
                    var raw = Value(args, ref i, arg);
                    if (!int.TryParse(raw, out var run) || run < 1)
                        throw new CliException("--run must be a positive integer.");
                    result.RunIndex = run;
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        throw new CliException($"Unknown option: {arg}");
                    positionals.Add(arg);
                    break;
            }
        }

        if (positionals.Count > 1)
            throw new CliException("Specify one command/thread id.");
        if (positionals.Count == 1)
            result.Command = positionals[0];
        if (result.RunIndex is not null && result.Command is "latest" or "list")
            throw new CliException("--run can only be used with an explicit thread id.");
        return result;
    }

    private static string Value(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
            throw new CliException($"Missing value for {option}.");
        return args[index];
    }
}

public sealed class CliException(string message) : Exception(message);

public static class RepositoryLocator
{
    public static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(full);
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add("--show-toplevel");

        using var process = Process.Start(psi) ?? throw new CliException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new CliException($"Could not determine repository root for '{path}'. {stderr.Trim()}");
        return Path.GetFullPath(stdout.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

public static class CodexHomeLocator
{
    public static string Resolve(string? explicitPath)
    {
        var candidate = !string.IsNullOrWhiteSpace(explicitPath)
            ? explicitPath
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_HOME"))
                ? Environment.GetEnvironmentVariable("CODEX_HOME")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return Path.GetFullPath(candidate!);
    }
}
