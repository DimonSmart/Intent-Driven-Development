internal sealed class ListCodingAgentsCommand
{
    public int Run()
    {
        foreach (var codingAgent in new ManifestReader().Read().CodingAgents)
        {
            Console.WriteLine(codingAgent);
        }

        return 0;
    }
}
