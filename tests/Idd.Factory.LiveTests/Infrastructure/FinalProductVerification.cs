using Idd.Factory.LiveTests.Environments;

namespace Idd.Factory.LiveTests.Infrastructure;

public static class FinalProductVerification
{
    public static async Task<(ProcessResult Build, ProcessResult Tests)> RunAsync(IFactoryEvalEnvironment environment, FactoryEvalWorkspace workspace, CancellationToken cancellationToken)
    {
        var build = await environment.RunCommandAsync(workspace, "dotnet", ["build", "MiniCatalog.sln", "--no-restore"], cancellationToken);
        var tests = await environment.RunCommandAsync(workspace, "dotnet", ["test", "tests/MiniCatalog.Tests/MiniCatalog.Tests.csproj", "--no-restore"], cancellationToken);
        return (build, tests);
    }
}
