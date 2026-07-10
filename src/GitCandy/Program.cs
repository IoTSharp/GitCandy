using GitCandy.Configuration;
using GitCandy.Operations;
using GitCandy.Profiling;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSystemd();
builder.Host.UseWindowsService(options => options.ServiceName = "GitCandy");
builder.Services.AddGitCandyWebShell(builder.Configuration, builder.Environment.ContentRootPath);

var app = builder.Build();
app.ConfigureGitCandyLegacyLogger();

if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await app.Services.MigrateGitCandyDatabaseAsync();
    return;
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

app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapGitCandyHealthChecks();
app.MapGitCandyCompatibilityRoutes();

await app.RunAsync();
