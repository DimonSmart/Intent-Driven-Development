using System.Text.RegularExpressions;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryDispatchViolation(string Code, string Message);

// Retained only for rollout diagnostics from public/root sessions. Runtime
// subprocess invocation is authoritative; collaboration dispatch is forbidden.
public static class FactoryDispatchContract
{
    private static readonly Regex ActionPattern = new("(?im)^\\s*Action:[ \\t]*(?:\\r?\\n[ \\t]*)?(?<value>[^\\r\\n]+)", RegexOptions.Compiled);
    private static readonly Regex RolePattern = new("(?im)^\\s*Role:[ \\t]*(?:\\r?\\n[ \\t]*)?(?<value>[^\\r\\n]+)", RegexOptions.Compiled);

    public static string? ReadAction(string? dispatch) => Read(ActionPattern, dispatch)?.ToUpperInvariant();

    public static IReadOnlyList<FactoryDispatchViolation> Validate(string role, string? dispatch)
    {
        if (role == "factory-root" || string.IsNullOrWhiteSpace(dispatch)) return [];
        if (role is not ("planner" or "executor")) return [new("ROLE_FORBIDDEN", $"Factory runtime must not dispatch unknown semantic role '{role}'.")];
        if (ReadAction(dispatch) is not null) return [new("DISPATCH_ACTION_FORBIDDEN", $"Semantic role '{role}' must not receive an orchestration Action field.")];
        var declared = Read(RolePattern, dispatch);
        return declared is null || declared == role ? [] : [new("DISPATCH_ROLE_MISMATCH", $"Declared role '{declared}' does not match '{role}'.")];
    }

    private static string? Read(Regex regex, string? text)
    { var value = regex.Match(text ?? string.Empty).Groups["value"].Value.Trim(); return value.Length == 0 ? null : value.ToLowerInvariant(); }
}
