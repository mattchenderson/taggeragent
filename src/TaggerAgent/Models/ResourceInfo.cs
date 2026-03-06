namespace TaggerAgent.Models;

/// <summary>
/// Represents an Azure resource discovered during a scan.
/// </summary>
/// <param name="ResourceId">Full ARM resource ID.</param>
/// <param name="Name">Resource name.</param>
/// <param name="Type">Resource type (e.g., "Microsoft.Compute/virtualMachines").</param>
/// <param name="ResourceGroup">Resource group name.</param>
/// <param name="Location">Azure region.</param>
/// <param name="Tags">Current tags on the resource.</param>
/// <param name="SubscriptionId">Subscription ID containing the resource.</param>
public sealed record ResourceInfo(
    string ResourceId,
    string Name,
    string Type,
    string ResourceGroup,
    string Location,
    Dictionary<string, string> Tags,
    string SubscriptionId
);
