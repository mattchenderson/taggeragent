using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using TaggerAgent.Models;

namespace TaggerAgent.Services;

/// <summary>
/// Service for writing audit log entries to Azure Table Storage.
/// Stores tagging operations with subscription-based partitioning.
/// </summary>
public sealed class AuditService(
    string storageAccountName,
    TokenCredential credential,
    ILogger<AuditService> logger)
{
    private const string TableName = "TagAuditLog";
    private readonly TableClient _tableClient = CreateTableClient(storageAccountName, credential);

    private static TableClient CreateTableClient(string accountName, TokenCredential credential)
    {
        var tableServiceUri = new Uri($"https://{accountName}.table.core.windows.net");
        var tableServiceClient = new TableServiceClient(tableServiceUri, credential);
        return tableServiceClient.GetTableClient(TableName);
    }

    /// <summary>
    /// Logs a tag change to the audit table.
    /// Failures are logged but do not throw exceptions.
    /// </summary>
    /// <param name="entry">Audit entry to log.</param>
    public async Task LogChangeAsync(AuditEntry entry)
    {
        logger.LogInformation("Logging audit entry: {ResourceId} - {TagKey}={TagValue} ({Action})",
            entry.ResourceId, entry.TagKey, entry.TagValue, entry.Action);

        try
        {
            // Ensure table exists
            await _tableClient.CreateIfNotExistsAsync();

            // Extract subscription ID from resource ID for partition key
            var subscriptionId = ExtractSubscriptionId(entry.ResourceId);
            entry.PartitionKey = subscriptionId;

            // Generate row key: {resourceId}_{tag}_{timestamp}
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var safeResourceId = entry.ResourceId.Replace("/", "_");
            entry.RowKey = $"{safeResourceId}_{entry.TagKey}_{timestamp}";
            entry.Timestamp = DateTimeOffset.UtcNow;

            await _tableClient.AddEntityAsync(entry);

            logger.LogInformation("Audit entry logged successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log audit entry for {ResourceId}", entry.ResourceId);
            // Don't throw - audit logging failure shouldn't block tag application
        }
    }

    private static string ExtractSubscriptionId(string resourceId)
    {
        // Resource ID format: /subscriptions/{subscriptionId}/resourceGroups/...
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var subIndex = Array.IndexOf(parts, "subscriptions");
        return subIndex >= 0 && subIndex + 1 < parts.Length ? parts[subIndex + 1] : "unknown";
    }
}
