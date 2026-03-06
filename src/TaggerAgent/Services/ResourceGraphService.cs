using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TaggerAgent.Models;

namespace TaggerAgent.Services;

/// <summary>
/// Service for querying Azure Resource Graph to discover Azure resources.
/// </summary>
public sealed class ResourceGraphService(
    string subscriptionId,
    TokenCredential credential,
    ILogger<ResourceGraphService> logger)
{
    private readonly ArmClient _armClient = new(credential);
    private readonly string _subscriptionId = subscriptionId;

    /// <summary>
    /// Executes a KQL query against Azure Resource Graph.
    /// Handles pagination via SkipToken automatically.
    /// </summary>
    /// <param name="kqlQuery">KQL query to execute.</param>
    /// <returns>List of resources matching the query.</returns>
    public async Task<List<ResourceInfo>> QueryResourcesAsync(string kqlQuery)
    {
        logger.LogInformation("Executing Resource Graph query: {Query}", kqlQuery);

        var resources = new List<ResourceInfo>();

        try
        {
            var tenant = _armClient.GetTenants().GetAll().First();

            var queryContent = new ResourceQueryContent(kqlQuery);
            queryContent.Subscriptions.Add(_subscriptionId);
            queryContent.Options = new ResourceQueryRequestOptions
            {
                ResultFormat = ResultFormat.ObjectArray
            };

            string? skipToken = null;
            do
            {
                if (skipToken is not null)
                {
                    queryContent.Options.SkipToken = skipToken;
                }

                var response = await tenant.GetResourcesAsync(queryContent);
                var result = response.Value;

                if (result.Data is not null)
                {
                    using var doc = JsonDocument.Parse(result.Data.ToString());
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in doc.RootElement.EnumerateArray())
                        {
                            var resource = ParseResourceRow(row);
                            if (resource is not null)
                            {
                                resources.Add(resource);
                            }
                        }
                    }
                }

                skipToken = result.SkipToken;
            } while (!string.IsNullOrEmpty(skipToken));

            logger.LogInformation("Resource Graph query returned {Count} resources", resources.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute Resource Graph query: {Query}", kqlQuery);
            throw;
        }

        return resources;
    }

    private static ResourceInfo? ParseResourceRow(JsonElement row)
    {
        try
        {
            var id = row.GetProperty("id").GetString() ?? string.Empty;
            var name = row.GetProperty("name").GetString() ?? string.Empty;
            var type = row.GetProperty("type").GetString() ?? string.Empty;
            var resourceGroup = row.GetProperty("resourceGroup").GetString() ?? string.Empty;
            var location = row.GetProperty("location").GetString() ?? string.Empty;
            var subscriptionId = row.GetProperty("subscriptionId").GetString() ?? string.Empty;

            var tags = new Dictionary<string, string>();
            if (row.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var tag in tagsElement.EnumerateObject())
                {
                    tags[tag.Name] = tag.Value.GetString() ?? string.Empty;
                }
            }

            return new ResourceInfo(id, name, type, resourceGroup, location, tags, subscriptionId);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
