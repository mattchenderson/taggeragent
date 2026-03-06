namespace TaggerAgent;

/// <summary>
/// Canonical environment variable names used by TaggerAgent components.
/// This class is the single source of truth for all environment variable names.
/// Infra (Bicep) and docs must stay aligned with these constants.
/// </summary>
public static class EnvironmentConfig
{
    // --- azd / Foundry convention variables ---

    /// <summary>
    /// Foundry project endpoint URL.
    /// azd output name: AZURE_AI_PROJECT_ENDPOINT
    /// </summary>
    public const string ProjectEndpoint = "AZURE_AI_PROJECT_ENDPOINT";

    /// <summary>
    /// Target Azure subscription ID for resource scanning and tagging.
    /// azd auto-injects this from the active subscription.
    /// </summary>
    public const string SubscriptionId = "AZURE_SUBSCRIPTION_ID";

    /// <summary>
    /// Managed identity client ID (Function App only — set via Bicep app settings).
    /// </summary>
    public const string ClientId = "AZURE_CLIENT_ID";

    // --- Application-specific variables ---

    /// <summary>
    /// Blob service endpoint URL for rules storage.
    /// Example: https://{account}.blob.core.windows.net
    /// The rules service constructs per-subscription paths from this base URL.
    /// </summary>
    public const string RulesStorageUrl = "RULES_STORAGE_URL";

    /// <summary>
    /// Storage account name used for audit table access.
    /// The audit service constructs the table endpoint from this name.
    /// </summary>
    public const string StorageAccountName = "STORAGE_ACCOUNT_NAME";

    /// <summary>
    /// Name of the Foundry hosted agent to invoke (Function App only).
    /// Default: "tagger-agent"
    /// </summary>
    public const string AgentName = "AGENT_NAME";

    /// <summary>
    /// NCRONTAB cron expression for the timer trigger (Function App only).
    /// Default: "0 0 2 * * *" (daily at 2 AM UTC)
    /// </summary>
    public const string TimerSchedule = "TIMER_SCHEDULE";
}
