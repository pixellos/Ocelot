using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Testing;

namespace Ocelot.ManualTest.Actions;

public class Issue941
{
    private class SseHub : Hub { }

    internal static async Task RunAsync(string[] args)
    {
        Console.WriteLine("Starting Playwright SSE Manual Test... ");

        int downstreamPort = PortFinder.GetRandomPort();
        int ocelotPort = PortFinder.GetRandomPort();

        // Start Downstream
        var downstreamBuilder = WebApplication.CreateBuilder(args);
        downstreamBuilder.Services.AddSignalR();
        var downstreamApp = downstreamBuilder.Build();
        downstreamApp.Urls.Add($"http://localhost:{downstreamPort}");
        downstreamApp.MapHub<SseHub>("/testhub");
        downstreamApp.MapGet("/sse-plain", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            await ctx.Response.WriteAsync("data: event1\n\n");
            await ctx.Response.Body.FlushAsync();
        });
        downstreamApp.MapGet("/not-sse", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("chunk1");
            await ctx.Response.Body.FlushAsync();
            await Task.Delay(2000);
            await ctx.Response.WriteAsync("chunk2");
            await ctx.Response.Body.FlushAsync();
        });
        await downstreamApp.StartAsync();
        Console.WriteLine($"Downstream SignalR server started on port {downstreamPort}");

        // Start Gateway
        var gatewayBuilder = WebApplication.CreateBuilder(args);
        var ocelotConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Routes:0:DownstreamPathTemplate", "/{everything}" },
                { "Routes:0:DownstreamScheme", "http" },
                { "Routes:0:DownstreamHostAndPorts:0:Host", "localhost" },
                { "Routes:0:DownstreamHostAndPorts:0:Port", downstreamPort.ToString() },
                { "Routes:0:UpstreamPathTemplate", "/proxy/{everything}" },
                { "Routes:0:UpstreamHttpMethod:0", "Get" },
                { "Routes:0:UpstreamHttpMethod:1", "Post" },
                { "Routes:0:UpstreamHttpMethod:2", "Options" },
            })
            .Build();

        gatewayBuilder.Services.AddOcelot(ocelotConfig);
        gatewayBuilder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ["text/plain", "text/event-stream"];
        });
        gatewayBuilder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(p => p
                .AllowAnyHeader()
                .AllowAnyMethod()
                .SetIsOriginAllowed(_ => true)
                .AllowCredentials());
        });

        var gatewayApp = gatewayBuilder.Build();
        gatewayApp.Urls.Add($"http://localhost:{ocelotPort}");
        gatewayApp.UseResponseCompression();
        gatewayApp.UseCors();

        await gatewayApp.UseOcelot();
        await gatewayApp.StartAsync();
        Console.WriteLine($"Ocelot Gateway started on port {ocelotPort}");

        // Start Playwright
        Console.WriteLine("Launching Chromium browser (headed mode)...");
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false, SlowMo = 50 });
        var page = await browser.NewPageAsync();

        string htmlPath = Path.Combine("Tests", "Issue941.html");
        if (!File.Exists(htmlPath)) htmlPath = Path.Combine("test", "Ocelot.ManualTest", "Tests", "Issue941.html");
        
        var html = await File.ReadAllTextAsync(htmlPath);
        html = html.Replace("{{OCELOT_PORT}}", ocelotPort.ToString());

        await page.SetContentAsync(html);
        await page.EvaluateAsync("startConnection()");

        Console.WriteLine("Browser launched and connected to SignalR Hub.");
        Console.WriteLine("Sending test messages from downstream hub...");

        var hub = downstreamApp.Services.GetRequiredService<IHubContext<SseHub>>();
        
        // Send a few messages with delay to see them appear
        for (int i = 1; i <= 3; i++)
        {
            await Task.Delay(1000);
            await hub.Clients.All.SendAsync("ReceiveMessage", $"Hello from Ocelot message #{i}!");
        }

        Console.WriteLine("Running negative test (buffering check)...");
        await page.EvaluateAsync("runNegativeTest()");

        // Wait for results and display them in console
        string sseStatus = "pending";
        string bufferingStatus = "pending";

        for (int i = 0; i < 10; i++)
        {
            sseStatus = await page.EvaluateAsync<string>("window.testStatus.sse");
            bufferingStatus = await page.EvaluateAsync<string>("window.testStatus.buffering");
            if (sseStatus != "pending" && bufferingStatus != "pending") break;
            await Task.Delay(1000);
        }

        Console.WriteLine("\n" + new string('=', 30));
        Console.WriteLine("    BROWSER TEST RESULTS");
        Console.WriteLine(new string('=', 30));
        Console.WriteLine($"SSE Transport: {(sseStatus == "success" ? "✅ OK" : "❌ FAILED")}");
        Console.WriteLine($"Buffering:     {(bufferingStatus == "success" ? "✅ OK" : "❌ FAILED")}");
        Console.WriteLine(new string('=', 30) + "\n");

        Console.WriteLine("Tests completed! Close the browser window or press any key to exit...");
        
        try 
        {
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey();
            }
        }
        catch 
        {
            await Task.Delay(5000);
        }

        await browser.CloseAsync();
        await gatewayApp.StopAsync();
        await downstreamApp.StopAsync();
    }
}
