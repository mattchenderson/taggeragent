using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TaggerAgent.Functions;

/// <summary>
/// Entry point for the Azure Functions application.
/// Configures dependency injection, Application Insights telemetry, and Azure authentication.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Main entry point for the Functions host.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    public static void Main(string[] args)
    {
        var builder = FunctionsApplication.CreateBuilder(args);

        // Configure Application Insights telemetry
        builder.Services.AddApplicationInsightsTelemetryWorkerService();
        builder.Services.ConfigureFunctionsApplicationInsights();

        // Register Azure credential for service authentication
        builder.Services.AddSingleton(new DefaultAzureCredential());

        builder.Build().Run();
    }
}
