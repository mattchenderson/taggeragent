using Microsoft.Extensions.Logging;
using TaggerAgent.Models;
using TaggerAgent.Services;

namespace TaggerAgent.Tools;

/// <summary>
/// Tool implementation for scanning Azure resources via Resource Graph.
/// </summary>
public sealed class ScanResourcesTool(
    ResourceGraphService resourceGraphService,
    ILogger<ScanResourcesTool> logger)
{
    /// <summary>
    /// Scans resources with optional filters.
    /// </summary>
    /// <param name="resourceType">Optional resource type filter (e.g., "Microsoft.Compute/virtualMachines")</param>
    /// <param name="resourceGroup">Optional resource group name filter</param>
    /// <param name="tagStatus">Optional tag status filter: "untagged", "partially-tagged", "all"</param>
    /// <returns>List of ResourceInfo objects matching the filters</returns>
    public async Task<List<ResourceInfo>> ExecuteAsync(
        string? resourceType = null,
        string? resourceGroup = null,
        string? tagStatus = null)
    {
        logger.LogInformation(
            "Scanning resources: type={ResourceType}, group={ResourceGroup}, tagStatus={TagStatus}",
            resourceType ?? "all", resourceGroup ?? "all", tagStatus ?? "all");

        var query = BuildKqlQuery(resourceType, resourceGroup, tagStatus);
        var resources = await resourceGraphService.QueryResourcesAsync(query);

        logger.LogInformation("Scan completed: {ResourceCount} resources found", resources.Count);
        return resources;
    }

    private static string BuildKqlQuery(string? resourceType, string? resourceGroup, string? tagStatus)
    {
        var query = "Resources | project id, name, type, resourceGroup, location, tags, subscriptionId";

        var filters = new List<string>();

        if (!string.IsNullOrEmpty(resourceType))
        {
            filters.Add($"type =~ '{resourceType}'");
        }

        if (!string.IsNullOrEmpty(resourceGroup))
        {
            filters.Add($"resourceGroup =~ '{resourceGroup}'");
        }

        if (!string.IsNullOrEmpty(tagStatus))
        {
            filters.Add(tagStatus switch
            {
                "untagged" => "isempty(tags) or isnull(tags)",
                "partially-tagged" => "isnotempty(tags)",
                _ => "1 == 1" // "all" or unknown
            });
        }

        if (filters.Count > 0)
        {
            query = $"Resources | where {string.Join(" and ", filters)} | project id, name, type, resourceGroup, location, tags, subscriptionId";
        }

        return query;
    }
}
