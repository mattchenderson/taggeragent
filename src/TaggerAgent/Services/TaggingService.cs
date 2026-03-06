using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Microsoft.Extensions.Logging;

namespace TaggerAgent.Services;

/// <summary>
/// Service for applying tags to Azure resources via ARM API.
/// Uses merge semantics - never removes existing tags.
/// </summary>
public sealed class TaggingService(
    TokenCredential credential,
    ILogger<TaggingService> logger)
{
    private readonly ArmClient _armClient = new(credential);

    /// <summary>
    /// Applies a single tag to a resource using merge semantics.
    /// Existing tags are preserved - only the specified tag is added or updated.
    /// </summary>
    /// <param name="resourceId">Full ARM resource ID.</param>
    /// <param name="tagKey">Tag key to apply.</param>
    /// <param name="tagValue">Tag value to apply.</param>
    public async Task ApplyTagAsync(string resourceId, string tagKey, string tagValue)
    {
        logger.LogInformation("Applying tag {TagKey}={TagValue} to {ResourceId}", tagKey, tagValue, resourceId);

        try
        {
            var resourceIdentifier = new ResourceIdentifier(resourceId);
            var tagResource = _armClient.GetTagResource(resourceIdentifier);

            var tagPatch = new TagResourcePatch
            {
                PatchMode = TagPatchMode.Merge
            };
            tagPatch.TagValues[tagKey] = tagValue;

            await tagResource.UpdateAsync(WaitUntil.Completed, tagPatch);

            logger.LogInformation("Successfully applied tag {TagKey}={TagValue} to {ResourceId}", tagKey, tagValue, resourceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply tag {TagKey}={TagValue} to {ResourceId}", tagKey, tagValue, resourceId);
            throw;
        }
    }
}
