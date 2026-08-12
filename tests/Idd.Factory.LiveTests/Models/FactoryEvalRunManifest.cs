namespace Idd.Factory.LiveTests.Models;

public sealed record FactoryEvalRunManifest(
    int SchemaVersion,
    string CaseId,
    string ModelRequested,
    string ReasoningEffortRequested,
    string MethodologyVersion,
    string SourceRevision,
    bool SourceDirty,
    bool ReleaseCertification,
    string CodexVersion,
    string DotnetVersion,
    DateTimeOffset StartedAtUtc);
