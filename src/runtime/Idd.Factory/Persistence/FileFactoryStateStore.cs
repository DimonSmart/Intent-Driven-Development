using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.State;

namespace Idd.Factory.Persistence;

public interface IFactoryStateStore
{
    Task<FactoryState?> LoadAsync(CancellationToken cancellationToken);
    Task CreateAsync(FactoryState state, CancellationToken cancellationToken);
    Task SaveAsync(FactoryState state, long expectedRevision, CancellationToken cancellationToken);
}

public sealed class FileFactoryStateStore(string currentDirectory, FactoryStateValidator validator) : IFactoryStateStore
{
    public string StatePath => Path.Combine(currentDirectory, "state.json");

    public async Task<FactoryState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StatePath)) return null;
        try
        {
            await using var schemaStream = File.OpenRead(StatePath);
            using var document = await JsonDocument.ParseAsync(schemaStream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaNode) || !schemaNode.TryGetInt32(out var schemaVersion))
                throw new FactoryStateException("CORRUPT_FACTORY_STATE", "state.json has no valid schemaVersion.");
            if (schemaVersion != FactoryState.CurrentSchemaVersion)
                throw new FactoryStateException(
                    "LEGACY_FACTORY_STATE",
                    $"Active Factory state uses schema {schemaVersion}; this runtime uses schema {FactoryState.CurrentSchemaVersion}. Finish the run with the previous Factory version, or cancel/restart it with the new runtime.");

            var state = document.RootElement.Deserialize<FactoryState>(FactoryJson.Options)
                ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "state.json is empty.");
            validator.Validate(state);
            return state;
        }
        catch (FactoryStateException) { throw; }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw new FactoryStateException("CORRUPT_FACTORY_STATE", $"Cannot read state.json: {exception.Message}");
        }
    }

    public async Task CreateAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (File.Exists(StatePath)) throw new FactoryStateException("FACTORY_RUN_EXISTS", "A Factory run already exists.");
        validator.Validate(state);
        Directory.CreateDirectory(currentDirectory);
        await WriteAtomicAsync(state, cancellationToken);
    }

    public async Task SaveAsync(FactoryState state, long expectedRevision, CancellationToken cancellationToken)
    {
        var previous = await LoadAsync(cancellationToken)
            ?? throw new FactoryStateException("MISSING_FACTORY_STATE", "No Factory state exists.");
        if (previous.Revision != expectedRevision)
            throw new FactoryStateException("STALE_STATE_REVISION", $"Expected revision {expectedRevision}, actual {previous.Revision}.");
        state.Revision = expectedRevision + 1;
        validator.ValidateMutation(previous, state);
        await WriteAtomicAsync(state, cancellationToken);
    }

    private async Task WriteAtomicAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var temporaryPath = StatePath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, state, FactoryJson.Options, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(true);
        }
        File.Move(temporaryPath, StatePath, true);
        await using var verificationStream = File.OpenRead(StatePath);
        var persisted = await JsonSerializer.DeserializeAsync<FactoryState>(verificationStream, FactoryJson.Options, cancellationToken)
            ?? throw new FactoryStateException("CORRUPT_FACTORY_STATE", "Atomic save verification failed.");
        validator.Validate(persisted);
    }
}
