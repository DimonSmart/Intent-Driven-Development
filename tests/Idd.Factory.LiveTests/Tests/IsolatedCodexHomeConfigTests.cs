using System.Text.Json;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class IsolatedCodexHomeConfigTests
{
    [Fact]
    public void WindowsHomeHasNativeSandboxBackendAndRetainsAgentSettings()
    {
        var projectKey = JsonSerializer.Serialize(@"C:\Temp\factory-eval\workspace");

        var config = NativeFactoryEndToEndLiveTests.BuildIsolatedCodexConfig(projectKey, isWindows: true);

        Assert.Contains($"[projects.{projectKey}]\ntrust_level = \"trusted\"", config);
        Assert.Contains("[features.multi_agent_v2]\nenabled = true", config);
        Assert.Contains("expose_spawn_agent_model_overrides = true", config);
        Assert.Contains("wait_agent_enabled = true", config);
        Assert.Contains("[windows]\nsandbox = \"elevated\"", config);
        Assert.DoesNotContain("danger-full-access", config);
    }

    [Fact]
    public void NonWindowsHomeDoesNotSetWindowsSandboxBackend()
    {
        var projectKey = JsonSerializer.Serialize("/tmp/factory-eval/workspace");

        var config = NativeFactoryEndToEndLiveTests.BuildIsolatedCodexConfig(projectKey, isWindows: false);

        Assert.Contains($"[projects.{projectKey}]", config);
        Assert.Contains("[features.multi_agent_v2]\nenabled = true", config);
        Assert.DoesNotContain("[windows]", config);
    }
}
