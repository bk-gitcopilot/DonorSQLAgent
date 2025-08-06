using Microsoft.SemanticKernel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.Sqlite;
using DonorSQLAgent.Plugins;
using System.Data;
using Microsoft.AspNetCore.Components.Authorization;
using DonorSQLAgent.Authentication;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;


var builder = WebApplication.CreateBuilder(args);

// --- Blazor UI services ---
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// --- Semantic Kernel setup ---
var kernelBuilder = Kernel.CreateBuilder();
kernelBuilder.AddAzureOpenAIChatCompletion(
   deploymentName: "jdrf-gpt-4o-mini",
    endpoint: "https://jdrf-poc-openai.openai.azure.com/",
    apiKey: "DE1unyjOBXJm24VUnAQrTwoHFAQkLGHjbGwqWCIkRaQFjWiq9QDZJQQJ99BGACYeBjFXJ3w3AAABACOGj9Zm");

// Build the SK Kernel
var kernel = kernelBuilder.Build();

// --- Database + Cache setup for plugin ---
var env = builder.Environment;
var wwwRootPath = env.WebRootPath;
var dbConnection = new SqliteConnection($@"Data Source={wwwRootPath}\SQLLite_DB\donation_database.db");
var cache = new MemoryCache(new MemoryCacheOptions());

// --- Register Kernel & Auth ---
builder.Services.AddSingleton(kernel);
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

// --- Register Postgres DbContext as Factory ---
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector()));   // <-- important

// --- Register Plugins as Singletons ---
builder.Services.AddSingleton<SQLAgentPlugin>(sp =>
    new SQLAgentPlugin(dbConnection, kernel, cache));

builder.Services.AddSingleton<PostGraAgentPlugin>(sp =>
{
    var dbFactory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    return new PostGraAgentPlugin(dbFactory, kernel, cache);
});

var app = builder.Build();

// === Register plugins to kernel after DI is built ===
using (var scope = app.Services.CreateScope())
{
    var sqlAgentPlugin = scope.ServiceProvider.GetRequiredService<SQLAgentPlugin>();
    var postGraAgentPlugin = scope.ServiceProvider.GetRequiredService<PostGraAgentPlugin>();

    kernel.Plugins.AddFromObject(sqlAgentPlugin, "SQLAgent");
    kernel.Plugins.AddFromObject(postGraAgentPlugin, "SQL_Postgres");
}

// --- Configure pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

Console.WriteLine("SQL Agent ready. Run Blazor app to test UI.");

app.Run();
