using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TaggerAgent.Functions;

/// <summary>
/// Azure Function that performs automated resource tagging scans on a scheduled basis.
/// Invokes the Foundry hosted agent to scan and tag Azure resources according to configured rules.
/// </summary>
public sealed class TimerScanFunction(ILogger<TimerScanFunction> logger, DefaultAzureCredential credential)
{
    private readonly string _foundryEndpoint = Environment.GetEnvironmentVariable(EnvironmentConfig.ProjectEndpoint)
        ?? throw new InvalidOperationException($"{EnvironmentConfig.ProjectEndpoint} environment variable is required");

    private readonly string _agentName = Environment.GetEnvironmentVariable(EnvironmentConfig.AgentName) ?? "tagger-agent";

    private readonly string _subscriptionId = Environment.GetEnvironmentVariable(EnvironmentConfig.SubscriptionId)
        ?? throw new InvalidOperationException($"{EnvironmentConfig.SubscriptionId} environment variable is required");

    /// <summary>
    /// Timer trigger function for automated tagging scans.
    /// Default schedule: Daily at 2 AM UTC (0 0 2 * * *).
    /// Schedule can be overridden via TIMER_SCHEDULE environment variable.
    /// </summary>
    /// <param name="timerInfo">Timer trigger metadata including schedule status.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    [Function("TimerScanFunction")]
    public async Task RunAsync(
        [TimerTrigger("%TIMER_SCHEDULE%", RunOnStartup = false)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("TimerScanFunction triggered at: {TriggerTime}", DateTime.UtcNow);

        if (timerInfo.ScheduleStatus is not null)
        {
            logger.LogInformation("Next timer schedule at: {NextSchedule}", timerInfo.ScheduleStatus.Next);
        }

        // Create Foundry client and retrieve the hosted agent by name
        var client = new PersistentAgentsClient(_foundryEndpoint, credential);

        PersistentAgent? agent = await FindAgentByNameAsync(client).ConfigureAwait(false);
        if (agent is null)
        {
            logger.LogError("Agent '{AgentName}' not found in Foundry project", _agentName);
            return;
        }

        PersistentAgentThread? thread = null;
        try
        {
            thread = await client.Threads.CreateThreadAsync().ConfigureAwait(false);

            var message = $"""
                Run automated tagging scan for subscription {_subscriptionId}.
                Apply all changes where the rule has autoApply=true and your confidence is high.
                Do not apply changes where confidence is low or the rule has autoApply=false.
                Output a JSON report of all changes (applied and pending-review).
                """;

            await client.Messages.CreateMessageAsync(thread.Id, MessageRole.User, message)
                .ConfigureAwait(false);

            ThreadRun run = await client.Runs.CreateRunAsync(thread.Id, agent.Id)
                .ConfigureAwait(false);

            run = await WaitForRunCompletionAsync(client, thread.Id, run.Id, cancellationToken)
                .ConfigureAwait(false);

            if (run.Status != RunStatus.Completed)
            {
                logger.LogError("Agent run did not complete successfully. Status: {Status}, Error: {Error}",
                    run.Status, run.LastError?.Message ?? "Unknown");
                return;
            }

            await LogAgentResponseAsync(client, thread.Id).ConfigureAwait(false);
        }
        finally
        {
            if (thread is not null)
            {
                await client.Threads.DeleteThreadAsync(thread.Id).ConfigureAwait(false);
            }
        }

        logger.LogInformation("TimerScanFunction completed");
    }

    private async Task<PersistentAgent?> FindAgentByNameAsync(PersistentAgentsClient client)
    {
        await foreach (var agent in client.Administration.GetAgentsAsync().ConfigureAwait(false))
        {
            if (agent.Name == _agentName)
            {
                return agent;
            }
        }

        return null;
    }

    private async Task<ThreadRun> WaitForRunCompletionAsync(
        PersistentAgentsClient client,
        string threadId,
        string runId,
        CancellationToken cancellationToken)
    {
        ThreadRun run;
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            run = await client.Runs.GetRunAsync(threadId, runId).ConfigureAwait(false);
        } while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

        return run;
    }

    private async Task LogAgentResponseAsync(PersistentAgentsClient client, string threadId)
    {
        await foreach (var msg in client.Messages.GetMessagesAsync(threadId).ConfigureAwait(false))
        {
            if (msg.Role == MessageRole.Agent)
            {
                foreach (var content in msg.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        logger.LogInformation("Automated scan completed. Response: {Response}", textContent.Text);
                    }
                }
                break;
            }
        }
    }
}
