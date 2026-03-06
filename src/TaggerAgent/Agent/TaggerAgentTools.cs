using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaggerAgent.Models;
using TaggerAgent.Services;
using TaggerAgent.Tools;

namespace TaggerAgent.Agent;

/// <summary>
/// Provides tool methods for the TaggerAgent hosted agent.
/// Methods decorated with [Description] are discovered and registered by the Agent Framework
/// via AIFunctionFactory. The framework handles the tool-calling loop and parameter marshaling.
/// </summary>
public sealed class TaggerAgentTools(
    ScanResourcesTool scanTool,
    ApplyTagsTool applyTool,
    GetTaggingRulesTool rulesTool,
    RulesService rulesService,
    ILogger<TaggerAgentTools> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Scans Azure resources with optional filters for type, resource group, and tag status.
    /// </summary>
    /// <param name="resourceType">Resource type filter (e.g. 'Microsoft.Compute/virtualMachines'). Omit to scan all types.</param>
    /// <param name="resourceGroup">Resource group name filter. Omit to scan all groups.</param>
    /// <param name="tagStatus">Tag status filter: 'untagged', 'partially-tagged', or 'all'. Omit for all.</param>
    /// <returns>JSON array of resources with their current tags.</returns>
    [Description("Scan Azure resources with optional filters for type, resource group, and tag status. Returns a JSON array of resources with their current tags.")]
    public async Task<string> ScanResourcesAsync(
        [Description("Resource type filter (e.g. 'Microsoft.Compute/virtualMachines'). Omit to scan all types.")] string? resourceType = null,
        [Description("Resource group name filter. Omit to scan all groups.")] string? resourceGroup = null,
        [Description("Tag status filter: 'untagged', 'partially-tagged', or 'all'. Omit for all.")] string? tagStatus = null)
    {
        logger.LogInformation("Tool: scan_resources invoked (type={ResourceType}, group={ResourceGroup}, status={TagStatus})",
            resourceType ?? "all", resourceGroup ?? "all", tagStatus ?? "all");

        var resources = await scanTool.ExecuteAsync(resourceType, resourceGroup, tagStatus);
        return JsonSerializer.Serialize(resources, JsonOptions);
    }

    /// <summary>
    /// Applies tags to one or more Azure resources using merge semantics (never removes existing tags).
    /// </summary>
    /// <param name="changesJson">JSON array of tag changes. Each element must have: resourceId (string), tagKey (string), tagValue (string), confidence (string: 'high' or 'low').</param>
    /// <param name="executionMode">Execution mode: 'interactive' or 'automated'.</param>
    /// <returns>JSON result with applied and failed changes.</returns>
    [Description("Apply tags to one or more Azure resources using merge semantics (never removes existing tags). Accepts a JSON array of tag changes.")]
    public async Task<string> ApplyTagsAsync(
        [Description("JSON array of tag changes. Each element must have: resourceId (string), tagKey (string), tagValue (string), confidence (string: 'high' or 'low').")] string changesJson,
        [Description("Execution mode: 'interactive' or 'automated'.")] string executionMode = "interactive")
    {
        logger.LogInformation("Tool: apply_tags invoked (mode={ExecutionMode})", executionMode);

        var changes = JsonSerializer.Deserialize<List<TagChange>>(changesJson, JsonOptions)
            ?? throw new ArgumentException("Invalid tag changes JSON");

        var result = await applyTool.ExecuteAsync(changes, executionMode);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// Loads the current tagging rules from Blob Storage.
    /// </summary>
    /// <returns>Rules JSON with assessment criteria.</returns>
    [Description("Load the current tagging rules from Blob Storage. Returns the rules JSON with assessment criteria.")]
    public async Task<string> GetTaggingRulesAsync()
    {
        logger.LogInformation("Tool: get_tagging_rules invoked");

        var rules = await rulesTool.ExecuteAsync();
        return JsonSerializer.Serialize(rules, JsonOptions);
    }

    /// <summary>
    /// Saves tagging rules for the current subscription.
    /// </summary>
    /// <param name="rulesJson">JSON array of rules. Each rule has: tag (string), assessment (string - natural language), autoApply (boolean).</param>
    /// <returns>Success or error message.</returns>
    [Description("Save tagging rules for the current subscription. Rules are natural language assessments the agent uses to evaluate and tag resources.")]
    public async Task<string> SaveTaggingRulesAsync(
        [Description("JSON array of rules. Each rule has: tag (string), assessment (string - natural language), autoApply (boolean).")] string rulesJson)
    {
        logger.LogInformation("Tool: save_tagging_rules invoked");

        try
        {
            var rulesWrapper = JsonSerializer.Deserialize<RulesWrapper>(rulesJson, JsonOptions);
            var rules = rulesWrapper?.Rules ?? throw new ArgumentException("Invalid rules JSON - expected {\"rules\": [...]}");

            if (rules.Count == 0)
            {
                throw new ArgumentException("Cannot save empty rules list");
            }

            await rulesService.SaveRulesAsync(rules);

            return $"Successfully saved {rules.Count} tagging rules for the current subscription.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save tagging rules");
            return $"Error saving rules: {ex.Message}";
        }
    }

    /// <summary>
    /// Copies tagging rules from one subscription to another.
    /// </summary>
    /// <param name="sourceSubscriptionId">Source subscription ID to copy rules from.</param>
    /// <param name="targetSubscriptionId">Target subscription ID to copy rules to.</param>
    /// <returns>Success or error message with count of copied rules.</returns>
    [Description("Copy tagging rules from one subscription to another. Useful for applying consistent policies across subscriptions.")]
    public async Task<string> CopyTaggingRulesAsync(
        [Description("Source subscription ID to copy rules from.")] string sourceSubscriptionId,
        [Description("Target subscription ID to copy rules to.")] string targetSubscriptionId)
    {
        logger.LogInformation("Tool: copy_tagging_rules invoked (source={Source}, target={Target})", 
            sourceSubscriptionId, targetSubscriptionId);

        try
        {
            var count = await rulesService.CopyRulesAsync(sourceSubscriptionId, targetSubscriptionId);
            
            if (count == 0)
            {
                return $"No rules found in source subscription {sourceSubscriptionId}. Nothing copied.";
            }

            return $"Successfully copied {count} rules from subscription {sourceSubscriptionId} to {targetSubscriptionId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to copy tagging rules from {Source} to {Target}", 
                sourceSubscriptionId, targetSubscriptionId);
            return $"Error copying rules: {ex.Message}";
        }
    }

    /// <summary>
    /// Helper class for deserializing rules JSON.
    /// </summary>
    private class RulesWrapper
    {
        public List<TaggingRule> Rules { get; set; } = [];
    }
}
