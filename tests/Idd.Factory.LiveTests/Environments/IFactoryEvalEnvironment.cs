using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.LiveTests.Models;

namespace Idd.Factory.LiveTests.Environments;

public interface IFactoryEvalEnvironment
{
    Task PrepareAsync(FactoryEvalWorkspace workspace, CancellationToken cancellationToken);
    Task<ProcessResult> RunCodexAsync(FactoryEvalWorkspace workspace, FactoryEvalOptions options, CancellationToken cancellationToken);
    Task<ProcessResult> RunCommandAsync(FactoryEvalWorkspace workspace, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}
