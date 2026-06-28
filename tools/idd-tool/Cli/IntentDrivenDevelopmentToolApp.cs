internal sealed class IntentDrivenDevelopmentToolApp
{
    public int Run(string[] args)
    {
        try
        {
            return new CommandDispatcher().Run(args);
        }
        catch (ToolException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
