using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Runtime;

public sealed partial class FactoryRuntime
{
    private async Task<FactoryCliOutcome?> DecomposeAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var input = $"Original Factory request:\n{request}\n\nRecorded clarifications:\n{await ReadClarificationsAsync(state, cancellationToken)}\n\n" +
                    "Produce only the minimum safe task graph needed to make progress. Use executable work for self-contained work that can run now and outline work for known future scope that still requires scoped refinement. A complete up-front plan is not required.";
        var result = await InvokeSemanticAsync(state, "initial-decomposition", null, input, SemanticOperationKind.Decomposition, cancellationToken);
        if (result.Outcome != "ready")
            return await HandleSemanticStopAsync(state, null, result, SemanticOperationKind.Decomposition, input, cancellationToken);
        if (result.Payload is not { } payload || !payload.TryGetProperty("workItems", out var workItems) || workItems.ValueKind != JsonValueKind.Array)
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Decomposer ready result requires payload.workItems.");

        var candidate = CloneState(state);
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.RunStatus = FactoryRunStatus.Running;
        candidate.GraphRevision = state.GraphRevision + 1;
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in workItems.EnumerateArray())
            candidate.WorkItems.Add(ParseNewWorkItem(candidate, node, contracts));
        NormalizeCandidateReadiness(candidate);
        if (candidate.WorkItems.Count == 0 || !candidate.WorkItems.Any(item => item.Dependencies.Count == 0 && item.Status is WorkItemStatus.Ready or WorkItemStatus.Planned))
            throw new AgentProtocolException("INVALID_DECOMPOSITION", "Initial decomposition must expose executable or refinable work unless the semantic result reports a real blocker.");

        InvalidateFinalVerification(candidate);
        await PersistGraphMutationAsync(state, candidate, contracts, "initial-decomposition", null, result.Reason,
            candidate.WorkItems.Select(x => x.Id), [$"attempts/{result.AttemptId}/result.json"], cancellationToken);
        return null;
    }

    private async Task<FactoryCliOutcome?> RefineWorkAsync(FactoryState state, string workItemId, CancellationToken cancellationToken)
    {
        var item = state.WorkItems.Single(x => x.Id == workItemId);
        if (item.DefinitionState != WorkDefinitionState.Outline || !DependenciesCompleted(state, item))
            throw new AgentProtocolException("INVALID_REFINEMENT", $"Work item {item.Id} is not currently refinable.");
        if (item.AttemptCount >= configuration.Limits.MaxAgentAttempts)
            throw new AgentProtocolException("REFINEMENT_BUDGET_EXHAUSTED", $"{item.Id} exhausted its scoped refinement attempt budget.");

        var contract = await File.ReadAllTextAsync(Path.Combine(currentDirectory, item.ContractPath), cancellationToken);
        var dependencyContext = await BuildDependencyContextAsync(state, item, cancellationToken);
        var input = $"Scoped refinement target:\n{contract}\n\nCompleted prerequisite results:\n{dependencyContext}\n\n" +
                    "Refine only this outline. Return one executable replacement with the same id, or a small replacement subgraph. Do not globally replan unrelated work.";
        var result = await InvokeSemanticAsync(state, "scoped-refinement", item, input, SemanticOperationKind.ScopedRefinement, cancellationToken);
        if (result.Outcome != "ready")
            return await HandleSemanticStopAsync(state, item, result, SemanticOperationKind.ScopedRefinement, input, cancellationToken);
        if (result.Payload is not { } payload || !payload.TryGetProperty("workItems", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() == 0)
            throw new AgentProtocolException("INVALID_REFINEMENT", "Scoped refinement requires a non-empty payload.workItems array.");

        var candidate = CloneState(state);
        var source = candidate.WorkItems.Single(x => x.Id == item.Id);
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        var replacementNodes = nodes.EnumerateArray().ToArray();
        var changed = new List<string> { source.Id };

        if (replacementNodes.Length == 1 && string.Equals(NodeId(replacementNodes[0]), source.Id, StringComparison.Ordinal))
        {
            ApplyRefinedDefinition(source, replacementNodes[0], contracts);
            source.PriorResultRefs.Add($"attempts/{result.AttemptId}/result.json");
            source.Status = WorkItemStatus.Planned;
            source.CurrentAttemptId = null;
            if (source.DefinitionState != WorkDefinitionState.Executable)
                throw new AgentProtocolException("INVALID_REFINEMENT", "A same-id scoped refinement must make the outline executable.");
        }
        else
        {
            source.Status = WorkItemStatus.Superseded;
            source.CurrentAttemptId = null;
            var replacements = new List<WorkItemState>();
            foreach (var node in replacementNodes)
            {
                var replacement = ParseNewWorkItem(candidate, node, contracts, forcedSequence: NextSequence(candidate));
                if (replacement.Dependencies.Count == 0) replacement.Dependencies.AddRange(source.Dependencies);
                replacement.Dependencies.RemoveAll(id => id == source.Id);
                candidate.WorkItems.Add(replacement);
                replacements.Add(replacement);
                changed.Add(replacement.Id);
            }
            var replacementIds = replacements.Select(x => x.Id).ToArray();
            foreach (var downstream in candidate.WorkItems.Where(x => x.Id != source.Id && !replacementIds.Contains(x.Id, StringComparer.Ordinal) && x.Dependencies.Contains(source.Id, StringComparer.Ordinal)))
            {
                downstream.Dependencies.RemoveAll(id => id == source.Id);
                foreach (var replacementId in replacementIds)
                    if (!downstream.Dependencies.Contains(replacementId, StringComparer.Ordinal)) downstream.Dependencies.Add(replacementId);
                changed.Add(downstream.Id);
            }
        }

        candidate.GraphRevision = state.GraphRevision + 1;
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.RunStatus = FactoryRunStatus.Running;
        NormalizeCandidateReadiness(candidate);
        InvalidateFinalVerification(candidate);
        await PersistGraphMutationAsync(state, candidate, contracts, "scoped-refinement", item.Id, result.Reason, changed,
            [$"attempts/{result.AttemptId}/result.json"], cancellationToken);
        return null;
    }

    private async Task<FactoryCliOutcome?> ReplanAsync(FactoryState state, CancellationToken cancellationToken)
    {
        var trigger = state.PendingReplanTrigger
            ?? throw new AgentProtocolException("MISSING_REPLAN_TRIGGER", "Global replan requires a persisted trigger.");
        if (state.ReplanCount >= configuration.Limits.MaxReplans)
            throw new AgentProtocolException("REPLAN_BUDGET_EXHAUSTED", "Global semantic replan budget exhausted.");

        var request = await File.ReadAllTextAsync(Path.Combine(currentDirectory, state.RequestPath), cancellationToken);
        var remaining = state.WorkItems.Where(x => x.Status is not (WorkItemStatus.Completed or WorkItemStatus.Superseded or WorkItemStatus.Cancelled))
            .Select(x => new { x.Id, x.Sequence, x.Capability, x.DefinitionState, x.Status, x.ContractPath, x.Dependencies });
        var completed = state.WorkItems.Where(x => x.Status == WorkItemStatus.Completed)
            .Select(x => new { x.Id, x.Capability, x.ContractPath, x.LastResultRef });
        var input = $"Original request:\n{request}\n\nRecorded clarifications:\n{await ReadClarificationsAsync(state, cancellationToken)}\n\n" +
                    $"Global replan trigger:\n{JsonSerializer.Serialize(trigger, FactoryJson.Options)}\n\nRemaining graph:\n{JsonSerializer.Serialize(remaining, FactoryJson.Options)}\n\n" +
                    $"Completed immutable work:\n{JsonSerializer.Serialize(completed, FactoryJson.Options)}\n\nPropose only graph changes necessary for the global strategy change.";
        var result = await InvokeSemanticAsync(state, "global-replan", null, input, SemanticOperationKind.GlobalReplan, cancellationToken);
        if (result.Outcome != "replan-proposed")
            return await HandleSemanticStopAsync(state, null, result, SemanticOperationKind.GlobalReplan, input, cancellationToken);

        if (result.Payload is not { } payload || !payload.TryGetProperty("operations", out var operations) || operations.ValueKind != JsonValueKind.Array)
            throw new AgentProtocolException("INVALID_REPLAN", "Global replan proposal requires payload.operations.");
        var candidate = CloneState(state);
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        string? runContext = null;
        foreach (var operation in operations.EnumerateArray())
            ApplyReplanOperation(candidate, operation, contracts, changed, ref runContext);
        if (changed.Count == 0)
            throw new AgentProtocolException("INVALID_REPLAN", "A global replan must change task-graph topology or definition.");

        candidate.GraphRevision = state.GraphRevision + 1;
        candidate.ReplanCount++;
        candidate.PendingReplanTrigger = null;
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.RunStatus = FactoryRunStatus.Running;
        NormalizeCandidateReadiness(candidate);
        InvalidateFinalVerification(candidate);
        if (runContext is not null)
            await WriteRuntimeArtifactAtomicallyAsync(Path.Combine(currentDirectory, "run-context.md"), runContext, cancellationToken);
        await PersistGraphMutationAsync(state, candidate, contracts, "semantic-replan", trigger.SourceWorkItemId, trigger.Reason, changed,
            trigger.EvidenceRefs.Concat([$"attempts/{result.AttemptId}/result.json"]), cancellationToken);
        return null;
    }

    private void ApplyReplanOperation(
        FactoryState candidate,
        JsonElement operation,
        IDictionary<string, string> contracts,
        ISet<string> changed,
        ref string? runContext)
    {
        var kind = operation.GetProperty("kind").GetString();
        switch (kind)
        {
            case "add-work":
            case "insert-subtask":
            case "insert-checkpoint":
            {
                var nodeName = operation.TryGetProperty("workItem", out _) ? "workItem" : operation.TryGetProperty("subtask", out _) ? "subtask" : "checkpoint";
                var item = ParseNewWorkItem(candidate, operation.GetProperty(nodeName), contracts, forcedSequence: NextSequence(candidate));
                candidate.WorkItems.Add(item);
                changed.Add(item.Id);
                break;
            }
            case "supersede-work":
            case "supersede-ready-subtask":
            case "remove-unused-ready-checkpoint":
            {
                var item = MutableDefinitionItem(candidate, operation.GetProperty("id").GetString()!);
                item.Status = WorkItemStatus.Superseded;
                changed.Add(item.Id);
                break;
            }
            case "change-dependencies":
            {
                var item = MutableDefinitionItem(candidate, operation.GetProperty("id").GetString()!);
                item.Dependencies.Clear();
                item.Dependencies.AddRange(Strings(operation, "dependencies"));
                changed.Add(item.Id);
                break;
            }
            case "refine-work":
            case "replace-ready-subtask":
            {
                var id = operation.GetProperty("id").GetString()!;
                var item = MutableDefinitionItem(candidate, id);
                var node = operation.TryGetProperty("workItem", out var newWork) ? newWork : operation.GetProperty("subtask");
                if (string.Equals(NodeId(node), id, StringComparison.Ordinal))
                {
                    ApplyRefinedDefinition(item, node, contracts);
                    changed.Add(id);
                }
                else
                {
                    item.Status = WorkItemStatus.Superseded;
                    var replacement = ParseNewWorkItem(candidate, node, contracts, forcedSequence: NextSequence(candidate));
                    candidate.WorkItems.Add(replacement);
                    changed.Add(id);
                    changed.Add(replacement.Id);
                }
                break;
            }
            case "reorder-work":
            case "reorder-ready-work":
            {
                var ids = operation.TryGetProperty("workItemIds", out _) ? Strings(operation, "workItemIds") : Strings(operation, "ids");
                var mutable = ids.Select(id => MutableDefinitionItem(candidate, id)).ToArray();
                if (mutable.Length != ids.Distinct(StringComparer.Ordinal).Count()) throw new AgentProtocolException("INVALID_REPLAN", "Reorder IDs must be unique.");
                var slots = mutable.Select(x => x.Sequence).Order().ToArray();
                for (var index = 0; index < ids.Count; index++)
                {
                    mutable.Single(x => x.Id == ids[index]).Sequence = slots[index];
                    changed.Add(ids[index]);
                }
                break;
            }
            case "update-checkpoint-coverage":
            {
                var item = MutableDefinitionItem(candidate, operation.GetProperty("id").GetString()!);
                item.CoveredWorkItems.Clear();
                item.CoveredWorkItems.AddRange(Strings(operation, "coveredWorkItems"));
                changed.Add(item.Id);
                break;
            }
            case "update-run-context":
                runContext = operation.GetProperty("content").GetString() ?? "";
                break;
            default:
                throw new AgentProtocolException("INVALID_REPLAN", $"Unsupported replan operation '{kind}'.");
        }
    }

    private async Task<FactoryCliOutcome?> MaterializeAdditionalWorkAsync(
        FactoryState state,
        WorkItemState sourceItem,
        AgentResultEnvelope result,
        CancellationToken cancellationToken)
    {
        if (result.Payload is not { } payload)
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "additional-work-required requires a payload.");
        var requirement = payload.TryGetProperty("requirement", out var nested) ? nested : payload.TryGetProperty("additionalWork", out nested) ? nested : payload;
        var capability = requirement.TryGetProperty("capability", out var capabilityNode) ? capabilityNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(capability))
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Additional work requires a capability.");
        FactoryCapabilityCatalog.ResolveWorkItem(capability);
        if (!configuration.AllowedCapabilities.Contains(capability))
            throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Capability '{capability}' requested by {sourceItem.Id} is not allowed by Factory configuration.");
        var requiredSlots = sourceItem.Capability == "semantic-review" && !sourceItem.IsFinalReview ? 2 : 1;
        EnsureWorkItemCapacity(state, requiredSlots, "Dynamic work expansion");

        var goal = RequiredString(requirement, "goal", "Additional work goal is required.");
        var reason = RequiredString(requirement, "reason", "Additional work reason is required.");
        var id = NextGeneratedId(state, capability);
        var contract = BuildAdditionalWorkContract(requirement, goal, reason);
        var candidate = CloneState(state);
        var source = candidate.WorkItems.Single(x => x.Id == sourceItem.Id);
        source.LastResultRef = $"attempts/{result.AttemptId}/result.json";
        source.LastSemanticOutcome = result.Outcome;
        if (!source.PriorResultRefs.Contains(source.LastResultRef, StringComparer.Ordinal)) source.PriorResultRefs.Add(source.LastResultRef);

        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        var newItem = NewRuntimeWorkItem(candidate, id, capability, WorkItemKind.Subtask, contract, source.Dependencies, requirement, contracts);
        candidate.WorkItems.Add(newItem);
        var changed = new List<string> { source.Id, newItem.Id };

        if (source.Capability == "semantic-review")
        {
            source.Status = WorkItemStatus.Completed;
            source.CurrentAttemptId = null;
            if (source.IsFinalReview)
            {
                candidate.FinalReview = new(result.Outcome, source.LastResultRef, (candidate.FinalReview?.AttemptCount ?? 0) + 1, source.Id, source.ReviewTargetGraphRevision);
            }
            else
            {
                var followUp = CreateFollowUpReview(candidate, source, [newItem.Id], contracts);
                candidate.WorkItems.Add(followUp);
                changed.Add(followUp.Id);
            }
        }
        else
        {
            source.Status = WorkItemStatus.Waiting;
            source.CurrentAttemptId = null;
            if (!source.Dependencies.Contains(newItem.Id, StringComparer.Ordinal)) source.Dependencies.Add(newItem.Id);
        }

        candidate.GraphRevision = state.GraphRevision + 1;
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.RunStatus = FactoryRunStatus.Running;
        NormalizeCandidateReadiness(candidate);
        InvalidateFinalVerification(candidate);
        await PersistGraphMutationAsync(state, candidate, contracts, "worker-additional-work", sourceItem.Id, reason, changed,
            source.VerificationEvidenceRefs.Concat([$"attempts/{result.AttemptId}/result.json"]), cancellationToken);
        return null;
    }

    private async Task<FactoryCliOutcome?> MaterializeReviewCorrectionAsync(
        FactoryState state,
        WorkItemState reviewItem,
        AgentResultEnvelope result,
        CancellationToken cancellationToken)
    {
        if (state.CorrectiveCycleCount >= configuration.Limits.MaxCorrectiveCycles)
            throw new AgentProtocolException("CORRECTIVE_BUDGET_EXHAUSTED", "Corrective work budget exhausted.");
        if (result.Payload is not { } payload || !(payload.TryGetProperty("correctiveSubtask", out var correction) || payload.TryGetProperty("correction", out correction)))
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "Review correction requires payload.correctiveSubtask or payload.correction.");
        EnsureWorkItemCapacity(state, reviewItem.IsFinalReview ? 1 : 2, "Semantic review correction");

        var contract = RequiredString(correction, "contractMarkdown", "Correction contract is required.");
        var capability = correction.TryGetProperty("capability", out var cap) ? cap.GetString() ?? "implementation" : "implementation";
        FactoryCapabilityCatalog.ResolveWorkItem(capability);
        if (!configuration.AllowedCapabilities.Contains(capability))
            throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Correction capability '{capability}' is not allowed.");

        var candidate = CloneState(state);
        var review = candidate.WorkItems.Single(x => x.Id == reviewItem.Id);
        review.Status = WorkItemStatus.Completed;
        review.CurrentAttemptId = null;
        review.LastResultRef = $"attempts/{result.AttemptId}/result.json";
        review.LastSemanticOutcome = result.Outcome;
        var correctionId = correction.TryGetProperty("id", out var idNode) && !string.IsNullOrWhiteSpace(idNode.GetString())
            ? idNode.GetString()!
            : NextGeneratedId(candidate, "implementation", "CF");
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        var correctionItem = NewRuntimeWorkItem(candidate, correctionId, capability, WorkItemKind.CorrectiveSubtask, contract, review.Dependencies, correction, contracts);
        candidate.WorkItems.Add(correctionItem);
        candidate.CorrectiveCycleCount++;
        var changed = new List<string> { review.Id, correctionItem.Id };

        if (review.IsFinalReview)
        {
            candidate.FinalReview = new(result.Outcome, review.LastResultRef, (candidate.FinalReview?.AttemptCount ?? 0) + 1, review.Id, review.ReviewTargetGraphRevision);
        }
        else
        {
            var followUp = CreateFollowUpReview(candidate, review, [correctionItem.Id], contracts);
            candidate.WorkItems.Add(followUp);
            changed.Add(followUp.Id);
        }

        candidate.GraphRevision = state.GraphRevision + 1;
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        candidate.RunStatus = FactoryRunStatus.Running;
        NormalizeCandidateReadiness(candidate);
        InvalidateFinalVerification(candidate);
        await PersistGraphMutationAsync(state, candidate, contracts, "semantic-review", review.Id, result.Reason, changed,
            review.VerificationEvidenceRefs.Concat([$"attempts/{result.AttemptId}/result.json"]), cancellationToken);
        return null;
    }

    private async Task<FactoryCliOutcome?> CreateFinalReviewAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (!configuration.FinalReview.Required) throw new InvalidOperationException("Final review policy cannot be disabled in this Factory version.");
        EnsureWorkItemCapacity(state, 1, "Mandatory final review");

        var candidate = CloneState(state);
        candidate.GraphRevision = state.GraphRevision + 1;
        var id = NextGeneratedId(candidate, "semantic-review", "RV-FINAL");
        var contract = "# Final integrated semantic review\n\nReview the integrated product against the original Factory request and current durable intent. Use compact authoritative verification observations and evidence references supplied by runtime. Do not rerun deterministic checks. Return an approved verdict only when no semantic defect remains.";
        var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = ContractPath(id, 1, contract);
        contracts.Add(path, contract);
        var dependencies = candidate.WorkItems.Where(x => x.Status == WorkItemStatus.Completed).Select(x => x.Id).ToList();
        var review = new WorkItemState
        {
            Id = id,
            Sequence = NextSequence(candidate),
            Kind = WorkItemKind.ReviewCheckpoint,
            Capability = "semantic-review",
            DefinitionState = WorkDefinitionState.Executable,
            Status = WorkItemStatus.Ready,
            ContractPath = path,
            ContractRevision = 1,
            Dependencies = dependencies,
            IsFinalReview = true,
            ReviewTargetGraphRevision = candidate.GraphRevision
        };
        candidate.WorkItems.Add(review);
        candidate.PendingContinuation = null;
        candidate.Blocker = null;
        InvalidateFinalVerification(candidate);
        await PersistGraphMutationAsync(state, candidate, contracts, "runtime-final-review", null, "Mandatory final integrated review materialized after product-work quiescence.", [review.Id], state.VerificationEvidenceRefs, cancellationToken);
        return null;
    }

    private WorkItemState CreateFollowUpReview(FactoryState candidate, WorkItemState priorReview, IReadOnlyList<string> dependencies, IDictionary<string, string> contracts)
    {
        var id = NextGeneratedId(candidate, "semantic-review");
        var contract = File.ReadAllText(Path.Combine(currentDirectory, priorReview.ContractPath));
        var path = ContractPath(id, 1, contract);
        contracts.Add(path, contract);
        return new WorkItemState
        {
            Id = id,
            Sequence = NextSequence(candidate),
            Kind = WorkItemKind.ReviewCheckpoint,
            Capability = "semantic-review",
            DefinitionState = WorkDefinitionState.Executable,
            Status = WorkItemStatus.Planned,
            ContractPath = path,
            Dependencies = dependencies.ToList(),
            CoveredWorkItems = dependencies.ToList()
        };
    }

    private WorkItemState NewRuntimeWorkItem(
        FactoryState candidate,
        string id,
        string capability,
        WorkItemKind kind,
        string contract,
        IEnumerable<string> dependencies,
        JsonElement source,
        IDictionary<string, string> contracts)
    {
        if (candidate.WorkItems.Any(x => x.Id == id)) throw new AgentProtocolException("INVALID_GRAPH_MUTATION", $"Work item ID '{id}' already exists.");
        var path = ContractPath(id, 1, contract);
        contracts.Add(path, contract);
        var checkIds = Strings(source, "verificationCheckIds");
        var expectations = ParseVerificationExpectations(source);
        return new WorkItemState
        {
            Id = id,
            Sequence = NextSequence(candidate),
            Kind = kind,
            Capability = capability,
            DefinitionState = WorkDefinitionState.Executable,
            Status = WorkItemStatus.Planned,
            ContractPath = path,
            Dependencies = dependencies.Distinct(StringComparer.Ordinal).ToList(),
            VerificationCheckIds = checkIds,
            VerificationExpectations = expectations
        };
    }

    private WorkItemState ParseNewWorkItem(
        FactoryState candidate,
        JsonElement node,
        IDictionary<string, string> contracts,
        int? forcedSequence = null)
    {
        var id = NodeId(node);
        if (candidate.WorkItems.Any(x => x.Id == id)) throw new AgentProtocolException("INVALID_GRAPH_MUTATION", $"Duplicate work item ID '{id}'.");
        var definitionText = node.TryGetProperty("definitionState", out var definitionNode) ? definitionNode.GetString() : "executable";
        var definition = definitionText switch
        {
            "outline" => WorkDefinitionState.Outline,
            "executable" => WorkDefinitionState.Executable,
            _ => throw new AgentProtocolException("INVALID_GRAPH_MUTATION", $"Unknown definitionState '{definitionText}' for {id}.")
        };
        var capability = node.TryGetProperty("capability", out var capabilityNode) ? capabilityNode.GetString() : null;
        if (definition == WorkDefinitionState.Executable)
        {
            if (string.IsNullOrWhiteSpace(capability)) throw new AgentProtocolException("INVALID_GRAPH_MUTATION", $"Executable work item {id} requires capability.");
            FactoryCapabilityCatalog.ResolveWorkItem(capability);
        }
        var kindText = node.TryGetProperty("kind", out var kindNode) ? kindNode.GetString() : capability == "semantic-review" ? "review-checkpoint" : "subtask";
        var kind = kindText switch
        {
            "subtask" => WorkItemKind.Subtask,
            "review-checkpoint" => WorkItemKind.ReviewCheckpoint,
            "corrective-subtask" => WorkItemKind.CorrectiveSubtask,
            _ => throw new AgentProtocolException("INVALID_GRAPH_MUTATION", $"Unknown work item kind '{kindText}'.")
        };
        var sequence = forcedSequence ?? (node.TryGetProperty("sequence", out var sequenceNode) ? sequenceNode.GetInt32() : NextSequence(candidate));
        var contract = RequiredString(node, "contractMarkdown", $"Work item {id} requires contractMarkdown.");
        var path = ContractPath(id, 1, contract);
        contracts.Add(path, contract);
        return new WorkItemState
        {
            Id = id,
            Sequence = sequence,
            Kind = kind,
            Capability = capability,
            DefinitionState = definition,
            Status = WorkItemStatus.Planned,
            ContractPath = path,
            ContractRevision = 1,
            Dependencies = Strings(node, "dependencies"),
            CoveredWorkItems = Strings(node, "coveredWorkItems"),
            VerificationCheckIds = Strings(node, "verificationCheckIds"),
            VerificationExpectations = ParseVerificationExpectations(node)
        };
    }

    private void ApplyRefinedDefinition(WorkItemState item, JsonElement node, IDictionary<string, string> contracts)
    {
        var id = NodeId(node);
        if (id != item.Id) throw new AgentProtocolException("INVALID_REFINEMENT", "Same-item refinement must preserve the work item ID.");
        var definitionText = node.TryGetProperty("definitionState", out var definitionNode) ? definitionNode.GetString() : "executable";
        item.DefinitionState = definitionText == "outline" ? WorkDefinitionState.Outline : definitionText == "executable" ? WorkDefinitionState.Executable
            : throw new AgentProtocolException("INVALID_REFINEMENT", $"Unknown definitionState '{definitionText}'.");
        item.Capability = node.TryGetProperty("capability", out var capabilityNode) ? capabilityNode.GetString() : item.Capability;
        if (item.DefinitionState == WorkDefinitionState.Executable)
        {
            if (string.IsNullOrWhiteSpace(item.Capability)) throw new AgentProtocolException("INVALID_REFINEMENT", $"Executable work item {item.Id} requires capability.");
            FactoryCapabilityCatalog.ResolveWorkItem(item.Capability);
        }
        var contract = RequiredString(node, "contractMarkdown", $"Refined work item {item.Id} requires contractMarkdown.");
        item.ContractRevision++;
        item.ContractPath = ContractPath(item.Id, item.ContractRevision, contract);
        contracts.Add(item.ContractPath, contract);
        if (node.TryGetProperty("dependencies", out _))
        {
            item.Dependencies.Clear();
            item.Dependencies.AddRange(Strings(node, "dependencies"));
        }
        item.CoveredWorkItems.Clear();
        item.CoveredWorkItems.AddRange(Strings(node, "coveredWorkItems"));
        item.VerificationCheckIds.Clear();
        item.VerificationCheckIds.AddRange(Strings(node, "verificationCheckIds"));
        item.VerificationExpectations.Clear();
        foreach (var pair in ParseVerificationExpectations(node)) item.VerificationExpectations.Add(pair.Key, pair.Value);
    }

    private async Task PersistGraphMutationAsync(
        FactoryState state,
        FactoryState candidate,
        IReadOnlyDictionary<string, string> contracts,
        string source,
        string? sourceWorkItemId,
        string? reason,
        IEnumerable<string> changedWorkItems,
        IEnumerable<string> evidenceRefs,
        CancellationToken cancellationToken)
    {
        ValidateCandidateGraph(state, candidate);
        foreach (var (relative, content) in contracts)
            await WriteImmutableContractAsync(relative, content, cancellationToken);
        var mutationRef = await graphMutations.WriteAsync(state, candidate, source, sourceWorkItemId, reason, changedWorkItems, evidenceRefs, cancellationToken);
        ApplyCandidate(state, candidate);
        await SaveAsync(state, cancellationToken);
        await events.WriteAsync(state.RunId, "graph-mutated", new
        {
            source,
            state.GraphRevision,
            sourceWorkItemId,
            mutationRef,
            changedWorkItems = changedWorkItems.ToArray()
        }, cancellationToken);
    }

    private void ValidateCandidateGraph(FactoryState previous, FactoryState candidate)
    {
        if (candidate.WorkItems.Count > configuration.Limits.MaxWorkItems)
            throw new AgentProtocolException("WORK_EXPANSION_BUDGET_EXHAUSTED", $"Task graph exceeds the configured maximum of {configuration.Limits.MaxWorkItems} work items.");
        foreach (var item in candidate.WorkItems.Where(x => x.DefinitionState == WorkDefinitionState.Executable))
        {
            if (!configuration.AllowedCapabilities.Contains(item.Capability!))
                throw new AgentProtocolException("CAPABILITY_NOT_ALLOWED", $"Capability '{item.Capability}' is not allowed by the pinned Factory configuration.");
        }
        verification.ValidateCheckIds(candidate.WorkItems.SelectMany(x => x.VerificationCheckIds).Concat(candidate.WorkItems.SelectMany(x => x.VerificationExpectations.Keys)));
        try { stateValidator.ValidateMutation(previous, candidate); }
        catch (FactoryStateException exception) { throw new AgentProtocolException("INVALID_GRAPH_MUTATION", exception.Message); }
    }

    private async Task WriteImmutableContractAsync(string relative, string content, CancellationToken cancellationToken)
    {
        var path = Path.Combine(currentDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            if (await File.ReadAllTextAsync(path, cancellationToken) == content) return;
            throw new AgentProtocolException("CONTRACT_REVISION_COLLISION", $"Immutable contract artifact collision: {relative}.");
        }
        await WriteRuntimeArtifactAtomicallyAsync(path, content, cancellationToken, overwrite: false);
    }

    private static async Task WriteRuntimeArtifactAtomicallyAsync(string path, string content, CancellationToken cancellationToken, bool overwrite = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite);
    }

    private void NormalizeCandidateReadiness(FactoryState candidate)
    {
        foreach (var item in candidate.WorkItems.Where(x => x.DefinitionState == WorkDefinitionState.Executable && x.Status is WorkItemStatus.Planned or WorkItemStatus.Waiting))
            if (DependenciesCompleted(candidate, item)) item.Status = WorkItemStatus.Ready;
    }

    private static void InvalidateFinalVerification(FactoryState state)
    {
        state.FinalVerificationPassed = false;
        state.FinalVerificationGraphRevision = null;
    }

    private static WorkItemState MutableDefinitionItem(FactoryState state, string id)
    {
        var item = state.WorkItems.SingleOrDefault(x => x.Id == id)
            ?? throw new AgentProtocolException("INVALID_REPLAN", $"Unknown work item {id}.");
        if (item.Status is WorkItemStatus.Completed or WorkItemStatus.Superseded or WorkItemStatus.Cancelled or WorkItemStatus.Dispatching or WorkItemStatus.Running)
            throw new AgentProtocolException("INVALID_REPLAN", $"Work item {id} is not mutable in state {item.Status}.");
        return item;
    }

    private void EnsureWorkItemCapacity(FactoryState state, int additionalCount, string operation)
    {
        if (state.WorkItems.Count + additionalCount > configuration.Limits.MaxWorkItems)
            throw new AgentProtocolException("WORK_EXPANSION_BUDGET_EXHAUSTED", $"{operation} would exceed the configured maximum of {configuration.Limits.MaxWorkItems} work items.");
    }

    private static int NextSequence(FactoryState state) => state.WorkItems.Count == 0 ? 1 : state.WorkItems.Max(x => x.Sequence) + 1;

    private static string NodeId(JsonElement node)
    {
        var id = node.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
        return !string.IsNullOrWhiteSpace(id) ? id : throw new AgentProtocolException("INVALID_GRAPH_MUTATION", "Work item id is required.");
    }

    private static string RequiredString(JsonElement node, string name, string error)
    {
        var value = node.TryGetProperty(name, out var property) ? property.GetString() : null;
        return !string.IsNullOrWhiteSpace(value) ? value : throw new AgentProtocolException("MALFORMED_AGENT_RESULT", error);
    }

    private static List<string> Strings(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList()
            : [];

    private static Dictionary<string, VerificationExpectation> ParseVerificationExpectations(JsonElement node)
    {
        var result = new Dictionary<string, VerificationExpectation>(StringComparer.Ordinal);
        if (!node.TryGetProperty("verificationExpectations", out var expectations) || expectations.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return result;
        if (expectations.ValueKind != JsonValueKind.Object)
            throw new AgentProtocolException("MALFORMED_AGENT_RESULT", "verificationExpectations must be an object keyed by stable verification check ID.");
        foreach (var property in expectations.EnumerateObject())
        {
            result.Add(property.Name, property.Value.GetString() switch
            {
                "must-pass" => VerificationExpectation.MustPass,
                "may-fail" => VerificationExpectation.MayFail,
                var value => throw new AgentProtocolException("MALFORMED_AGENT_RESULT", $"Unknown verification expectation '{value}' for {property.Name}.")
            });
        }
        return result;
    }

    private static string ContractPath(string id, int revision, string content)
    {
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant()[..12];
        return $"work-items/{ArtifactId(id)}/contracts/{revision:000000}-{contentHash}.md";
    }

    private static string ArtifactId(string id)
    {
        var slug = string.Join('-', new string(id.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(slug)) slug = "work";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant()[..8];
        return $"{slug}-{hash}";
    }

    private static string NextGeneratedId(FactoryState state, string capability, string? prefixOverride = null)
    {
        var prefix = prefixOverride ?? capability switch
        {
            "research" => "R",
            "implementation" => "W",
            "documentation" => "D",
            "semantic-review" => "RV",
            _ => "W"
        };
        var index = 1;
        while (state.WorkItems.Any(x => x.Id == $"{prefix}-{index:000}")) index++;
        return $"{prefix}-{index:000}";
    }

    private static string BuildAdditionalWorkContract(JsonElement requirement, string goal, string reason)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Dynamically discovered work").AppendLine().AppendLine("## Goal").AppendLine().AppendLine(goal)
            .AppendLine().AppendLine("## Reason").AppendLine().AppendLine(reason);
        foreach (var field in new[] { "context", "expectedOutput" })
            if (requirement.TryGetProperty(field, out var node) && !string.IsNullOrWhiteSpace(node.GetString()))
                builder.AppendLine().AppendLine($"## {field}").AppendLine().AppendLine(node.GetString());
        if (requirement.TryGetProperty("constraints", out var constraints) && constraints.ValueKind == JsonValueKind.Array)
        {
            builder.AppendLine().AppendLine("## Constraints").AppendLine();
            foreach (var value in constraints.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x))) builder.AppendLine($"- {value}");
        }
        return builder.ToString().TrimEnd() + "\n";
    }
}
