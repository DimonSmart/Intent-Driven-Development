using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Idd.Factory.Report;

public sealed class CodexStateDbReader
{
    public CodexStateSnapshot Read(string codexHome)
    {
        var snapshot = new CodexStateSnapshot();
        var files = Directory.Exists(codexHome)
            ? Directory.EnumerateFiles(codexHome, "state_*.sqlite", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray()
            : [];

        if (files.Length == 0)
            return snapshot;

        foreach (var file in files)
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = file,
                    Mode = SqliteOpenMode.ReadOnly
                };
                using var connection = new SqliteConnection(builder.ToString());
                connection.Open();
                snapshot.DatabaseAvailable = true;

                var tables = ReadTables(connection);
                if (tables.Contains("thread_spawn_edges", StringComparer.OrdinalIgnoreCase))
                {
                    snapshot.SpawnEdgesAvailable = ReadSpawnEdges(connection, snapshot);
                }

                if (tables.Contains("threads", StringComparer.OrdinalIgnoreCase))
                    ReadThreadIndex(connection, snapshot);

                return snapshot;
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                snapshot.Diagnostics.Add(new Diagnostic
                {
                    Severity = "warning",
                    Category = "reporter",
                    Code = "state_db_unavailable",
                    Message = $"Could not read {Path.GetFileName(file)} read-only: {ex.Message}"
                });
            }
        }

        return snapshot;
    }

    private static HashSet<string> ReadTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    private static bool ReadSpawnEdges(SqliteConnection connection, CodexStateSnapshot snapshot)
    {
        var columns = ReadColumns(connection, "thread_spawn_edges");
        var parent = Pick(columns, "parent_thread_id", "parent_id", "parent_thread");
        var child = Pick(columns, "child_thread_id", "child_id", "child_thread");
        if (parent is null || child is null)
            return false;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Q(parent)}, {Q(child)} FROM {Q("thread_spawn_edges")}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;
            var p = reader.GetValue(0).ToString();
            var c = reader.GetValue(1).ToString();
            if (!string.IsNullOrWhiteSpace(p) && !string.IsNullOrWhiteSpace(c))
                snapshot.ParentByChild[c!] = p!;
        }
        return true;
    }

    private static void ReadThreadIndex(SqliteConnection connection, CodexStateSnapshot snapshot)
    {
        var columns = ReadColumns(connection, "threads");
        var id = Pick(columns, "id", "thread_id");
        var rollout = Pick(columns, "rollout_path", "rollout", "path");
        if (id is null || rollout is null)
            return;

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Q(id)}, {Q(rollout)} FROM {Q("threads")}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;
            var thread = reader.GetValue(0).ToString();
            var path = reader.GetValue(1).ToString();
            if (!string.IsNullOrWhiteSpace(thread) && !string.IsNullOrWhiteSpace(path))
                snapshot.RolloutByThread[thread!] = path!;
        }
    }

    private static HashSet<string> ReadColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Q(table)})";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            result.Add(reader.GetString(1));
        return result;
    }

    private static string? Pick(HashSet<string> columns, params string[] candidates) =>
        candidates.FirstOrDefault(columns.Contains);

    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
