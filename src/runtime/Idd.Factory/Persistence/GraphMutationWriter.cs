using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.State;
using Idd.Factory.Telemetry;

namespace Idd.Factory.Persistence;

public sealed record GraphMutationArtifact(
    int SchemaVersion,
    string RunId,
    long FromGraphRevision,
    long ToGraphRevision,
    string MutationId,
    string Source,
    string? SourceWorkItemId,
    string? Reason,
    IReadOnlyList<string> ChangedWorkItems,
    IReadOnlyList<string> EvidenceRefs,
    DateTimeOffset Timestamp);

/// <summary>
/// Writes append-only diagnostic graph history. History is never replayed to recover state;
/// state.json is authoritative. A crash may therefore leave an orphan mutation artifact.
/// </summary>
public sealed class GraphMutationWriter(string currentDirectory, IClock clock)
{
    public async Task<string> WriteAsync(
        FactoryState previous,
        FactoryState next,
        string source,
        string? sourceWorkItemId,
        string? reason,
        IEnumerable<string> changedWorkItems,
        IEnumerable<string> evidenceRefs,
        CancellationToken cancellationToken)
    {
        if (next.GraphRevision != previous.GraphRevision + 1)
            throw new ArgumentException("A graph mutation must advance GraphRevision by exactly one.", nameof(next));

        var directory = Path.Combine(currentDirectory, "graph", "mutations");
        Directory.CreateDirectory(directory);
        var mutationId = $"G{next.GraphRevision:000000}-{Guid.NewGuid():N}";
        var relative = $"graph/mutations/{mutationId}.json";
        var path = Path.Combine(currentDirectory, relative);
        var artifact = new GraphMutationArtifact(
            1,
            next.RunId,
            previous.GraphRevision,
            next.GraphRevision,
            mutationId,
            source,
            sourceWorkItemId,
            reason,
            changedWorkItems.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            evidenceRefs.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            clock.UtcNow);

        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, artifact, FactoryJson.Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporary, path);
        return relative;
    }
}
