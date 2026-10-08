// Evaluation-only check: no attempt counters, background processes or Factory runtime.
// The normal bounded failure diagnostic is the prerequisite for the second run.
var diagnostic = args.Single();
if (!File.Exists(diagnostic) || string.IsNullOrWhiteSpace(File.ReadAllText(diagnostic)))
{
    Console.Error.WriteLine("Intentional evaluation gate failure: final verification has no bounded failure diagnostic yet.");
    return 1;
}
Console.WriteLine("Recovery gate passed: the bounded final-verification failure diagnostic exists.");
return 0;
