using GitCandy.Configuration;
using GitCandy.Observability;
using GitCandy.Operations;
using GitCandy.Profiling;

var openSshRequested = OpenSshCommandLine.IsRequested(args);
OpenSshInvocation? openSshInvocation = null;
if (openSshRequested && !OpenSshCommandLine.TryParse(args, out openSshInvocation))
{
    Console.Error.WriteLine("OpenSSH mode requires exactly one SHA-256 key fingerprint argument.");
    return 2;
}

var builder = openSshRequested
    ? WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = [],
        ContentRootPath = AppContext.BaseDirectory,
        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environments.Production
    })
    : WebApplication.CreateBuilder(args);
if (openSshRequested)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
        options.LogToStandardErrorThreshold = LogLevel.Trace);
}

// Keep ASPNETCORE_HTTP_PORTS and other host settings as production overrides for JSON defaults.
builder.Configuration.AddEnvironmentVariables(prefix: "ASPNETCORE_");
builder.Host.UseSystemd();
builder.Host.UseWindowsService(options => options.ServiceName = "GitCandy");
if (openSshRequested)
{
    builder.Services.AddGitCandyOpenSshCommand(builder.Configuration);
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

await app.Services.MigrateGitCandyDatabaseAsync();

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

app.MapStaticAssets();
app.MapGitCandyHealthChecks();
app.MapGitCandyCompatibilityRoutes();

await app.RunAsync();
return 0;
