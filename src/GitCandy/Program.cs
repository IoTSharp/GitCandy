using GitCandy.Configuration;
using GitCandy.Observability;
using GitCandy.Operations;
using GitCandy.Profiling;
using Microsoft.Extensions.Options;

var openSshRequested = OpenSshCommandLine.IsRequested(args);
var receiveHookRequested = args.Length == 1
    && string.Equals(args[0], "--git-pre-receive", StringComparison.Ordinal);
var migrationOnlyRequested = args.Length == 1
    && string.Equals(args[0], "--migrate", StringComparison.Ordinal);
OpenSshInvocation? openSshInvocation = null;
if (openSshRequested && !OpenSshCommandLine.TryParse(args, out openSshInvocation))
{
    Console.Error.WriteLine("OpenSSH mode requires exactly one SHA-256 key fingerprint argument.");
    return 2;
}

var commandModeRequested = openSshRequested || receiveHookRequested;
var builder = commandModeRequested
    ? WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [],
        ContentRootPath = AppContext.BaseDirectory,
        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production
    })
    : WebApplication.CreateBuilder(args);
if (commandModeRequested)
{
    builder.Logging.ClearProviders();
    if (openSshRequested)
    {
        builder.Logging.AddConsole(options =>
            options.LogToStandardErrorThreshold = LogLevel.Trace);
    }
}

// Keep ASPNETCORE_HTTP_PORTS and other host settings as production overrides for JSON defaults.
builder.Configuration.AddEnvironmentVariables(prefix: "ASPNETCORE_");
builder.Host.UseSystemd();
builder.Host.UseWindowsService(options => options.ServiceName = "GitCandy");
if (openSshRequested)
{
    builder.Services.AddGitCandyOpenSshCommand(builder.Configuration);
}
else if (receiveHookRequested)
{
    builder.Services.AddGitCandyReceiveHookCommand(builder.Configuration);
}
else
{
    builder.AddGitCandyObservability();
    builder.Services.AddGitCandyWebShell(builder.Configuration, builder.Environment.ContentRootPath);
}

var app = builder.Build();

if (openSshRequested)
{
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await OpenSshCommandLine.ExecuteAsync(
            openSshInvocation!,
            scope.ServiceProvider,
            cancellation.Token);
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

if (receiveHookRequested)
{
    Environment.SetEnvironmentVariable("ConnectionStrings__GitCandy", null);
    Environment.SetEnvironmentVariable("GitCandy__Database__Provider", null);
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<GitCandy.Git.IGitReceiveHookRunner>()
                .ExecuteAsync(Console.In, Console.Error, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("GitCandy push gate was canceled.");
            return 1;
        }
        catch
        {
            Console.Error.WriteLine("GitCandy push gate failed safely.");
            return 1;
        }
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

await app.Services.MigrateGitCandyDatabaseAsync();
if (migrationOnlyRequested)
{
    return 0;
}

if (app.Services.GetRequiredService<IOptions<GitCandyProxyOptions>>().Value.Enabled)
{
    app.UseForwardedHeaders();
}

app.UseGitCandyRequestProfiler();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRequestTimeouts();

app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapGitCandyHealthChecks();
app.MapGitCandyCompatibilityRoutes();

await app.RunAsync();
return 0;
