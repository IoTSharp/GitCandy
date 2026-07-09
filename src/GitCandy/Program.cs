using GitCandy.Configuration;
using GitCandy.Profiling;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitCandyWebShell(builder.Configuration);

var app = builder.Build();
app.ConfigureGitCandyLegacyLogger();
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
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapGitCandyCompatibilityRoutes();

app.Run();
