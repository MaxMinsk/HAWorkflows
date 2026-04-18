using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

var frontendSection = builder.Configuration.GetSection("Frontend");
var frontendDevServerEnabled = builder.Environment.IsDevelopment() &&
                               frontendSection.GetValue("UseDevServer", false);
var frontendDevServerUrl = frontendSection.GetValue<string>("DevServerUrl") ?? "http://127.0.0.1:5173";
var apiBaseUrl = builder.Configuration.GetSection("Api").GetValue<string>("BaseUrl") ?? "http://127.0.0.1:5188";

var routes = new List<RouteConfig>
{
    new()
    {
        RouteId = "workflow-web-api",
        ClusterId = "workflow-web-api-cluster",
        Match = new RouteMatch
        {
            Path = "/api/{**catch-all}"
        },
        Transforms = new[]
        {
            new Dictionary<string, string>
            {
                ["PathRemovePrefix"] = "/api"
            }
        }
    }
};

var clusters = new List<ClusterConfig>
{
    new()
    {
        ClusterId = "workflow-web-api-cluster",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["workflow-web-api-destination"] = new()
            {
                Address = EnsureTrailingSlash(apiBaseUrl)
            }
        }
    }
};

if (frontendDevServerEnabled)
{
    routes.Add(
        new RouteConfig
        {
            RouteId = "workflow-web-frontend-dev",
            ClusterId = "workflow-web-frontend-dev-cluster",
            Match = new RouteMatch
            {
                Path = "/{**catch-all}"
            }
        });

    clusters.Add(
        new ClusterConfig
        {
            ClusterId = "workflow-web-frontend-dev-cluster",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["workflow-web-frontend-dev-destination"] = new()
                {
                    Address = EnsureTrailingSlash(frontendDevServerUrl)
                }
            }
        });
}

builder.Services
    .AddReverseProxy()
    .LoadFromMemory(routes, clusters);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Workflow.Web",
    frontendMode = frontendDevServerEnabled ? "vite-dev-proxy" : "static-wwwroot",
    apiBaseUrl
}));

if (frontendDevServerEnabled)
{
    app.Logger.LogInformation(
        "Workflow.Web started in Vite dev-proxy mode. Frontend target: {FrontendDevServerUrl}; API target: {ApiBaseUrl}",
        frontendDevServerUrl,
        apiBaseUrl);

    app.UseWebSockets();
    app.MapReverseProxy();
}
else
{
    app.MapReverseProxy();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();

static string EnsureTrailingSlash(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException($"Frontend:DevServerUrl is invalid: '{url}'.");
    }

    var normalized = uri.ToString();
    return normalized.EndsWith('/') ? normalized : $"{normalized}/";
}
