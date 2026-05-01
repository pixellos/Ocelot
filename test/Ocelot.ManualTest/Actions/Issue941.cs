using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            for (int i = 1; i <= 3; i++)
            {
                await ctx.Response.WriteAsync($"data: plain-event-{i}\n\n");
                await ctx.Response.Body.FlushAsync();
                await Task.Delay(1000);
            }
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

        // Prepare HTML for manual opening
        string htmlPath = Path.Combine("Tests", "Issue941.html");
        if (!File.Exists(htmlPath)) htmlPath = Path.Combine("test", "Ocelot.ManualTest", "Tests", "Issue941.html");
        
        var html = await File.ReadAllTextAsync(htmlPath);
        html = html.Replace("{{OCELOT_PORT}}", ocelotPort.ToString());
        
        string tempHtmlPath = Path.Combine(Directory.GetCurrentDirectory(), "OcelotSSETest.html");
        await File.WriteAllTextAsync(tempHtmlPath, html);

        Console.WriteLine("\n" + new string('*', 60));
        Console.WriteLine("           MANUAL VERIFICATION REQUIRED");
        Console.WriteLine(new string('*', 60));
        Console.WriteLine($"1. Open your browser and navigate to:");
        Console.WriteLine($"   file:///{tempHtmlPath.Replace("\\", "/")}");
        Console.WriteLine($"2. Verification (SignalR SSE, Plain SSE, Buffering) will start automatically.");
        Console.WriteLine(new string('*', 60) + "\n");

        var hub = downstreamApp.Services.GetRequiredService<IHubContext<SseHub>>();
        _ = Task.Run(async () => {
            int i = 1;
            while(true) {
                await Task.Delay(2000);
                await hub.Clients.All.SendAsync("ReceiveMessage", $"Hello from Ocelot message #{i++}!");
            }
        });

        Console.WriteLine("Tests running! Press any key to exit and stop servers...");
        
        try 
        {
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey();
            }
        }
        catch 
        {
            await Task.Delay(60000); // Wait 1 minute if non-interactive
        }

        if (File.Exists(tempHtmlPath)) File.Delete(tempHtmlPath);
        await gatewayApp.StopAsync();
        await downstreamApp.StopAsync();
    }
}
