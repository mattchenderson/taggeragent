using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Extensions.AI;
using TaggerAgent;
using TaggerAgent.Agent;
using TaggerAgent.Services;
using TaggerAgent.Tools;

var builder = WebApplication.CreateBuilder(args);

// Read configuration from environment
var subscriptionId = Environment.GetEnvironmentVariable(EnvironmentConfig.SubscriptionId)
    ?? throw new InvalidOperationException($"{EnvironmentConfig.SubscriptionId} environment variable is required");

var rulesStorageUrl = Environment.GetEnvironmentVariable(EnvironmentConfig.RulesStorageUrl)
    ?? throw new InvalidOperationException($"{EnvironmentConfig.RulesStorageUrl} environment variable is required");

var auditStorageAccountName = Environment.GetEnvironmentVariable(EnvironmentConfig.StorageAccountName)
    ?? throw new InvalidOperationException($"{EnvironmentConfig.StorageAccountName} environment variable is required");

var foundryEndpoint = Environment.GetEnvironmentVariable(EnvironmentConfig.ProjectEndpoint)
    ?? throw new InvalidOperationException($"{EnvironmentConfig.ProjectEndpoint} environment variable is required");

// Azure credentials — DefaultAzureCredential, zero secrets
var credential = new DefaultAzureCredential();

// Register Azure SDK services
builder.Services.AddSingleton(credential);
builder.Services.AddSingleton(sp => new ResourceGraphService(
    subscriptionId, credential, sp.GetRequiredService<ILogger<ResourceGraphService>>()));
builder.Services.AddSingleton(sp => new TaggingService(
    credential, sp.GetRequiredService<ILogger<TaggingService>>()));
builder.Services.AddSingleton(sp => new RulesService(
    rulesStorageUrl, subscriptionId, credential, sp.GetRequiredService<ILogger<RulesService>>()));
builder.Services.AddSingleton(sp => new AuditService(
    auditStorageAccountName, credential, sp.GetRequiredService<ILogger<AuditService>>()));

// Register tool implementations
builder.Services.AddSingleton<ScanResourcesTool>();
builder.Services.AddSingleton<ApplyTagsTool>();
builder.Services.AddSingleton<GetTaggingRulesTool>();

// Register agent tools provider
builder.Services.AddSingleton<TaggerAgentTools>();

// Register Foundry client for model access
builder.Services.AddSingleton(new PersistentAgentsClient(foundryEndpoint, credential));

// Register agent tools as AITool instances for the Agent Framework
builder.Services.AddSingleton<IEnumerable<AITool>>(sp =>
{
    var tools = sp.GetRequiredService<TaggerAgentTools>();
    return
    [
        AIFunctionFactory.Create(tools.ScanResourcesAsync),
        AIFunctionFactory.Create(tools.ApplyTagsAsync),
        AIFunctionFactory.Create(tools.GetTaggingRulesAsync),
        AIFunctionFactory.Create(tools.SaveTaggingRulesAsync),
        AIFunctionFactory.Create(tools.CopyTaggingRulesAsync)
    ];
});

// Register the AI agent with the Agent Framework hosting
builder.AddAIAgent("tagger-agent", AgentInstructions.SystemPrompt);

// Configure OpenAI responses protocol hosting (Foundry routes requests here)
builder.AddOpenAIResponses();

var app = builder.Build();

// Map the responses protocol endpoint
app.MapOpenAIResponses();

await app.RunAsync();
