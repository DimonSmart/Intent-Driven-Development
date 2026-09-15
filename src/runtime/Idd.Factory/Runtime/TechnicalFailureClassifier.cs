using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal enum TechnicalFailureClassification
{
    RestartableTechnicalFailure,
    TerminalTechnicalFailure,
    OtherProtocolFailure
}

internal static class TechnicalFailureClassifier
{
    public static TechnicalFailureClassification Classify(
        SemanticOperationKind operation,
        AgentProtocolException exception)
    {
        if (operation != SemanticOperationKind.WorkItemExecution)
            return TechnicalFailureClassification.OtherProtocolFailure;

        return exception.Code switch
        {
            "AGENT_COMMAND_TIMEOUT" => TechnicalFailureClassification.RestartableTechnicalFailure,
            "AGENT_COMMAND_INCOMPLETE" => TechnicalFailureClassification.RestartableTechnicalFailure,
            "AGENT_TRANSPORT_FAILURE" => TechnicalFailureClassification.RestartableTechnicalFailure,
            _ => TechnicalFailureClassification.OtherProtocolFailure
        };
    }
}
