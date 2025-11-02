using PokemonQuizAPI.Hubs;
using PokemonQuizAPI.Data;
using PokemonQuizAPI.Controllers;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Register DatabaseHelper as Scoped instead of Singleton for better connection management
builder.Services.AddScoped<DatabaseHelper>();

// Register SeedController's dependencies so we can instantiate it via DI later
builder.Services.AddHttpClient();

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

// Configure CORS with more specific settings for production
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .SetIsOriginAllowed(_ => true);
        }
        else
        {
            policy
                .WithOrigins("http://localhost:19006", "exp://192.168.1.100:19000")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// Add health checks
builder.Services.AddHealthChecks();

// Admin API key (optional) - set in appsettings as Admin:ApiKey
var adminApiKey = builder.Configuration.GetValue<string>("Admin:ApiKey");

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.UseCors();

// Simple admin API key middleware: protects /api/admin and /admin static UI when ApiKey is provided
if (!string.IsNullOrWhiteSpace(adminApiKey))
{
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/wwwroot/admin", StringComparison.OrdinalIgnoreCase))
        {
            var key = context.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? context.Request.Query["adminKey"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key) || key != adminApiKey)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        await next();
    });
}

app.UseAuthorization();

// Serve a simple static admin UI if present
app.UseDefaultFiles();
app.UseStaticFiles();

// Map endpoints
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");
app.MapHealthChecks("/health");

// Add a simple root endpoint
app.MapGet("/", () => "Pokemon Quiz API is running!");

// After application starts, ensure minimal seed exists so app works out-of-the-box
app.Lifetime.ApplicationStarted.Register(() =>
{
    // Run in background
    _ = Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DatabaseHelper>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AutoSeeder");
            var count = await db.GetPokemonCountAsync();
            if (count == 0)
            {
                logger.LogInformation("No Pokémon found in DB/local storage — auto-seeding default 151 Pokémon in background.");
                // Instantiate controller to run seed logic (it will use HttpClient from DI)
                var seedController = ActivatorUtilities.CreateInstance<SeedController>(scope.ServiceProvider);
                try
                {
                    // fire-and-forget but await to catch errors
                    var res = await seedController.SeedPokemon(151);
                    logger.LogInformation("Auto-seed completed: {Result}", res?.ToString() ?? "no result");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Auto-seed failed");
                }
            }
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AutoSeeder");
            logger.LogWarning(ex, "Auto-seed background task failed");
        }
    });
});

app.Run();