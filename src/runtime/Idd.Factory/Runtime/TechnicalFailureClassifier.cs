using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

internal enum TechnicalFailureClassification
{
    RestartableTechnicalFailure,
    ExternalBackendBlocker,
    TerminalTechnicalFailure,
    OtherProtocolFailure
}

internal static class TechnicalFailureClassifier
{
    public static TechnicalFailureClassification Classify(
        SemanticOperationKind operation,
        AgentProtocolException exception)
    {
        if (AgentFailureCodes.IsExternalBackendBlocker(exception.Code))
            return TechnicalFailureClassification.ExternalBackendBlocker;

        if (operation != SemanticOperationKind.WorkItemExecution)
            return TechnicalFailureClassification.OtherProtocolFailure;

        return exception.Code switch
        {
            AgentFailureCodes.CommandTimeout => TechnicalFailureClassification.RestartableTechnicalFailure,
            AgentFailureCodes.CommandIncomplete => TechnicalFailureClassification.RestartableTechnicalFailure,
            AgentFailureCodes.TransportFailure => TechnicalFailureClassification.RestartableTechnicalFailure,
            _ => TechnicalFailureClassification.OtherProtocolFailure
        };
    }
}
