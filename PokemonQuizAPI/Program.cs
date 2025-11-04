using PokemonQuizAPI.Hubs;
using PokemonQuizAPI.Data;
using PokemonQuizAPI.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<DatabaseHelper>();
builder.Services.AddControllers();
builder.Services.AddLogging();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS policy to allow local frontend during development
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalDev", policy =>
    {
        policy.WithOrigins("http://localhost:8081", "http://localhost:19006", "http://localhost:19000", "http://localhost:5168")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Ensure DB is initialized (runs schema.sql when using MySQL)
try
{
    var db = app.Services.GetRequiredService<DatabaseHelper>();
    await db.EnsureDatabaseInitializedAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Database initialization failed at startup");
}

// Simple middleware: protect admin endpoints with Bearer token validated against sessions
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments(new PathString("/api/admin"), StringComparison.OrdinalIgnoreCase))
    {
        var auth = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer "))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var token = auth.Substring("Bearer ".Length).Trim();
        var db = context.RequestServices.GetRequiredService<DatabaseHelper>();
        var userId = await db.GetUserIdForTokenAsync(token);
        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        // attach user id for controllers to use if needed
        context.Items["UserId"] = userId;
    }

    await next();
});

app.UseRouting();

// Enable CORS
app.UseCors("LocalDev");

app.UseStaticFiles();

// Map SignalR hub for multiplayer
app.MapHub<GameHub>("/hubs/game");

app.MapControllers();

app.Run();