using Microsoft.AspNetCore.SignalR;
using PokemonQuizAPI.Hubs;
using PokemonQuizAPI.Data;
using PokemonQuizAPI.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Load configuration from appsettings.json and environment-specific JSON file
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

// Add services
builder.Services.AddSingleton<DatabaseHelper>();
// register repositories
builder.Services.AddScoped<IGameRoomRepository, GameRoomRepository>();
builder.Services.AddScoped<IPokemonRepository, PokemonRepository>();

// Add MVC controllers with views so we can serve a server-rendered admin page
builder.Services.AddControllersWithViews();
builder.Services.AddLogging();

// Add SignalR — enable Redis backplane only when a Redis connection string exists
var signalRBuilder = builder.Services.AddSignalR();
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    #pragma warning disable CS0618
    signalRBuilder.AddStackExchangeRedis(redisConn, options =>
    {
        // ChannelPrefix is a simple string; the obsolete warning originates inside the Redis lib
        options.Configuration.ChannelPrefix = "PokeQuiz";
    });
    #pragma warning restore CS0618
}

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
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Ensure DB is initialized (runs schema.sql when using MySQL)
try
{
    var db = app.Services.GetRequiredService<DatabaseHelper>();
    await db.EnsureDatabaseInitializedAsync();
    // Ensure default admin account (development only)
    if (app.Environment.IsDevelopment())
    {
        await db.EnsureAdminUserAsync("jack", "jackaroonie636@gmail.com", "test");
    }
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Database initialization failed at startup");
}

// Simple middleware: protect admin API endpoints with Bearer token validated against sessions
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.StartsWithSegments(new PathString("/api/admin"), StringComparison.OrdinalIgnoreCase))
    {
        // In development allow unrestricted access for presentation/demo convenience
        if (app.Environment.IsDevelopment())
        {
            // Attach a placeholder user id so controllers that expect context.Items["UserId"] can use it
            context.Items["UserId"] = "dev-admin";
            await next();
            return;
        }

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

        // Verify role
        var role = await db.GetUserRoleAsync(userId);
        if (string.IsNullOrWhiteSpace(role) || !role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
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

// Serve admin static index at /admin and files under /admin only if folder exists
var adminFolder = Path.Combine(AppContext.BaseDirectory, "wwwroot", "admin");
// Also consider project source folder when running via `dotnet run` (working directory may be project root)
var projectAdminFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "admin");
string? resolvedAdminFolder = null;
if (Directory.Exists(adminFolder)) resolvedAdminFolder = adminFolder;
else if (Directory.Exists(projectAdminFolder)) resolvedAdminFolder = projectAdminFolder;

if (!string.IsNullOrEmpty(resolvedAdminFolder))
{
    var adminFilesProvider = new PhysicalFileProvider(resolvedAdminFolder);
    var defaultAdminOptions = new DefaultFilesOptions
    {
        RequestPath = "/admin",
        FileProvider = adminFilesProvider
    };
    // look for index.html in the admin folder
    defaultAdminOptions.DefaultFileNames.Clear();
    defaultAdminOptions.DefaultFileNames.Add("index.html");
    app.UseDefaultFiles(defaultAdminOptions);
}
else
{
    logger.LogWarning("Admin SPA folder not found at {Path} or {Path2}; /admin will not be served as static content", adminFolder, projectAdminFolder);
}

// Ensure /admin serves the admin SPA index directly and support SPA fallback under /admin/*

var adminIndexPathCandidates = new[] { Path.Combine(adminFolder, "index.html"), Path.Combine(projectAdminFolder, "index.html") };
var adminIndexPath = adminIndexPathCandidates.FirstOrDefault(p => File.Exists(p));
if (!string.IsNullOrEmpty(adminIndexPath))
{
    // nothing here - AdminController will serve /admin
}
else
{
    logger.LogInformation("Admin index not present at candidates; skipping /admin endpoints");

    // Provide a small fallback admin page so /admin always responds when running in development
    var fallbackHtml = @"<!doctype html>
<html>
<head>
  <meta charset=""utf-8""> <meta name=""viewport"" content=""width=device-width,initial-scale=1""> 
  <title>Admin UI</title>
  <style>body{font-family:Arial,Helvetica,sans-serif;padding:24px;background:#f3f4f6} .card{background:#fff;padding:18px;border-radius:8px;max-width:780px;margin:24px auto} h1{margin:0 0 12px} input{width:100%;padding:8px;margin-top:8px;border-radius:6px;border:1px solid #ddd} button{margin-top:10px;padding:8px 12px;border-radius:6px;background:#123e7a;color:#fff;border:none} .muted{color:#666;margin-top:8px}</style>
</head>
<body>
  <div class=""card"">
    <h1>Admin UI (fallback)</h1>
    <p class=""muted"">No built admin SPA was found in the expected folder. You can still paste an admin token below to test API access.</p>
    <label>Token (paste the session token):</label>
    <input id=""tokenInput"" placeholder=""paste token here"" />
    <div style=""margin-top:12px;display:flex;gap:8px"">
      <button onclick=""save()"">Save token</button>
      <button onclick=""clearToken()"">Clear</button>
    </div>
    <p class=""muted"">After saving, use your API client or the frontend to call admin endpoints with Authorization: Bearer &lt;token&gt;.</p>
    <pre id=""status"" style=""margin-top:12px;white-space:pre-wrap;color:#111""></pre>
  </div>
  <script>
    function save(){ const v = document.getElementById('tokenInput').value || ''; localStorage.setItem('admin_token', v); document.getElementById('status').innerText = 'Token saved to localStorage.admin_token'; }
    function clearToken(){ localStorage.removeItem('admin_token'); document.getElementById('tokenInput').value=''; document.getElementById('status').innerText='Token cleared'; }
    document.getElementById('tokenInput').value = localStorage.getItem('admin_token') || '';
  </script>
</body>
</html>";

    app.MapGet("/admin", async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(fallbackHtml);
    });

    app.MapGet("/admin/{**slug}", async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(fallbackHtml);
    });
}

// Map SignalR hub for multiplayer
app.MapHub<GameHub>("/hubs/game");

// Map controllers and enable default route for views
app.MapControllers();

// Optional conventional route (not strictly required because AdminController uses attribute routing)
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();