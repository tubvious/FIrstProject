using Chess.Web.Configuration;
using Chess.Web.Endpoints;
using Chess.Web.Hubs;
using Chess.Web.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddChessServices(builder.Configuration, builder.Environment);

// Behind a tunnel or reverse proxy on this machine, use the address the browser actually used.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost);

var app = builder.Build();

await app.InitializeDatabaseAsync();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseSecurityHeaders(app.Environment);
app.UseStatusCodePagesWithReExecute("/NotFound");
app.UseHttpsRedirection();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapHub<GameHub>(GameHub.Route);
app.MapGameEndpoints();

await app.RunAsync();

/// <summary>Entry point marker, referenced by integration tests through WebApplicationFactory.</summary>
public partial class Program;
