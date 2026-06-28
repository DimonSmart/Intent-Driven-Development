internal sealed class FailureCollector
{
    private readonly List<string> failures = [];

    public bool HasFailures => failures.Count > 0;

    public void Add(string message) => failures.Add(message);

    public int PrintAndReturnExitCode()
    {
        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }

        return 1;
    }
}
