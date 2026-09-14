using Idd.Factory.Processes;

namespace Idd.Factory.Tests;

internal sealed class StubProcessExecutor(
    Func<ProcessExecutionRequest, CancellationToken, Task<ProcessExecutionResult>> runAsync)
    : IProcessExecutor
{
    public Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken) =>
        runAsync(request, cancellationToken);

    public static ProcessExecutionResult StartFailure(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception captured)
        {
            return new(
                null,
                null,
                ProcessCompletionReason.StartFailed,
                "",
                "",
                ProcessTerminationOutcome.NotRequested,
                [],
                captured);
        }
    }
}
