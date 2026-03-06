using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using TaggerAgent.Models;

namespace TaggerAgent.Services;

/// <summary>
/// Service for managing tagging rules in Azure Blob Storage.
/// Rules are stored per-subscription at {subscription-id}/rules.json within the rules container.
/// </summary>
public sealed class RulesService
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<RulesService> _logger;
    private readonly string _defaultSubscriptionId;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public RulesService(
        string blobServiceEndpoint,
        string subscriptionId,
        TokenCredential credential,
        ILogger<RulesService> logger,
        string containerName = "rules")
    {
        _logger = logger;
        _defaultSubscriptionId = subscriptionId;
        
        var blobServiceClient = new BlobServiceClient(new Uri(blobServiceEndpoint), credential);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    /// <summary>
    /// Loads tagging rules for the specified subscription.
    /// Returns an empty list if no rules exist (404).
    /// </summary>
    /// <param name="subscriptionId">Subscription ID to load rules for. Uses default subscription if null.</param>
    /// <returns>List of tagging rules for the subscription.</returns>
    public async Task<List<TaggingRule>> LoadRulesAsync(string? subscriptionId = null)
    {
        subscriptionId ??= _defaultSubscriptionId;
        var blobPath = $"{subscriptionId}/rules.json";
        
        _logger.LogInformation("Loading tagging rules from {BlobPath}", blobPath);

        try
        {
            var blobClient = _containerClient.GetBlobClient(blobPath);
            var response = await blobClient.DownloadContentAsync();
            var jsonContent = response.Value.Content.ToString();

            var rulesWrapper = JsonSerializer.Deserialize<RulesWrapper>(jsonContent, JsonOptions);
            var rules = rulesWrapper?.Rules ?? [];

            _logger.LogInformation("Loaded {RuleCount} tagging rules for subscription {SubscriptionId}", 
                rules.Count, subscriptionId);
            return rules;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("No rules found for subscription {SubscriptionId}", subscriptionId);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tagging rules from {BlobPath}", blobPath);
            throw;
        }
    }

    /// <summary>
    /// Saves tagging rules for the specified subscription.
    /// Uses the default subscription if not specified.
    /// </summary>
    /// <param name="rules">List of tagging rules to save.</param>
    /// <param name="subscriptionId">Subscription ID to save rules for. Uses default subscription if null.</param>
    public async Task SaveRulesAsync(List<TaggingRule> rules, string? subscriptionId = null)
    {
        subscriptionId ??= _defaultSubscriptionId;
        var blobPath = $"{subscriptionId}/rules.json";
        
        _logger.LogInformation("Saving {RuleCount} tagging rules to {BlobPath}", rules.Count, blobPath);

        try
        {
            var rulesWrapper = new RulesWrapper { Rules = rules };
            var jsonContent = JsonSerializer.Serialize(rulesWrapper, JsonOptions);
            var content = new BinaryData(Encoding.UTF8.GetBytes(jsonContent));

            var blobClient = _containerClient.GetBlobClient(blobPath);
            await blobClient.UploadAsync(content, overwrite: true);

            _logger.LogInformation("Successfully saved {RuleCount} rules for subscription {SubscriptionId}", 
                rules.Count, subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tagging rules to {BlobPath}", blobPath);
            throw;
        }
    }

    /// <summary>
    /// Copies tagging rules from one subscription to another.
    /// Returns the count of rules copied.
    /// </summary>
    /// <param name="sourceSubscriptionId">Source subscription ID to copy from.</param>
    /// <param name="targetSubscriptionId">Target subscription ID to copy to.</param>
    /// <returns>Number of rules copied.</returns>
    public async Task<int> CopyRulesAsync(string sourceSubscriptionId, string targetSubscriptionId)
    {
        _logger.LogInformation("Copying rules from {SourceSub} to {TargetSub}", 
            sourceSubscriptionId, targetSubscriptionId);

        var sourceRules = await LoadRulesAsync(sourceSubscriptionId);
        
        if (sourceRules.Count == 0)
        {
            _logger.LogWarning("No rules found in source subscription {SourceSub}, nothing to copy", 
                sourceSubscriptionId);
            return 0;
        }

        await SaveRulesAsync(sourceRules, targetSubscriptionId);
        
        _logger.LogInformation("Successfully copied {RuleCount} rules from {SourceSub} to {TargetSub}", 
            sourceRules.Count, sourceSubscriptionId, targetSubscriptionId);
        
        return sourceRules.Count;
    }

    /// <summary>
    /// Wrapper class for JSON serialization matching the expected format.
    /// </summary>
    private class RulesWrapper
    {
        public List<TaggingRule> Rules { get; set; } = [];
    }
}
