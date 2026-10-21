using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var shellyUrl = args.FirstOrDefault()
                ?? throw new ArgumentException("Usage: Shelly.Mcp <url>");

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = new Uri(shellyUrl)
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

[McpServerToolType]
public static class ShellyTools
{
    [McpServerTool]
    [Description("Turns on the light")]
    public static async Task<string> TurnOnLight(HttpClient http)
    {
        var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=true");
        response.EnsureSuccessStatusCode();

        return "Light turned on.";
    }

    [McpServerTool]
    [Description("Turns off the light")]
    public static async Task<string> TurnOffLight(HttpClient http)
    {
        var response = await http.GetAsync("/rpc/Switch.Set?id=0&on=false");
        response.EnsureSuccessStatusCode();

        return "Light turned off.";
    }
}