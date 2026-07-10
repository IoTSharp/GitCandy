using GitCandy.Configuration;
using GitCandy.Observability;
using GitCandy.Operations;
using GitCandy.Profiling;

var builder = WebApplication.CreateBuilder(args);

// Keep ASPNETCORE_HTTP_PORTS and other host settings as production overrides for JSON defaults.
builder.Configuration.AddEnvironmentVariables(prefix: "ASPNETCORE_");
builder.Host.UseSystemd();
builder.Host.UseWindowsService(options => options.ServiceName = "GitCandy");
builder.AddGitCandyObservability();
builder.Services.AddGitCandyWebShell(builder.Configuration, builder.Environment.ContentRootPath);

var app = builder.Build();

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

app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapGitCandyHealthChecks();
app.MapGitCandyCompatibilityRoutes();

await app.RunAsync();
