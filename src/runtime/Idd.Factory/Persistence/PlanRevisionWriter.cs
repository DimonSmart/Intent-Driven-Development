using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Telemetry;

namespace Idd.Factory.Persistence;

public sealed record PlanRevisionArtifact(
    int SchemaVersion,
    string RunId,
    long Revision,
    string Reason,
    string? SourceAttemptId,
    string? SourceWorkItemId,
    IReadOnlyList<string> PreviousRemainingIds,
    IReadOnlyList<string> NewRemainingIds,
    DateTimeOffset Timestamp);

public sealed class PlanRevisionWriter(string currentDirectory, IClock clock)
{
    public async Task<string> WriteAsync(FactoryState previous, FactoryState next, string reason, string? sourceAttemptId, string? sourceWorkItemId, CancellationToken cancellationToken)
    {
        if (next.PlanRevision != previous.PlanRevision + 1) throw new ArgumentException("PlanRevision must advance by exactly one.", nameof(next));
        var directory = Path.Combine(currentDirectory, "plan-revisions");
        Directory.CreateDirectory(directory);
        var relative = $"plan-revisions/P{next.PlanRevision:000000}.json";
        var path = Path.Combine(currentDirectory, relative);
        var artifact = new PlanRevisionArtifact(1, next.RunId, next.PlanRevision, reason, sourceAttemptId, sourceWorkItemId,
            previous.Remaining.Select(x => x.Id).ToArray(), next.Remaining.Select(x => x.Id).ToArray(), clock.UtcNow);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(artifact, FactoryJson.Options), cancellationToken);
        File.Move(temporary, path, true);
        return relative;
    }
}
