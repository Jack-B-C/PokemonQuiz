using PokemonQuizAPI.Hubs;
using PokemonQuizAPI.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Register DatabaseHelper as Scoped instead of Singleton for better connection management
builder.Services.AddScoped<DatabaseHelper>();

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
            // Development: Allow all origins for Expo and local testing
            policy
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .SetIsOriginAllowed(_ => true);
        }
        else
        {
            // Production: Specify allowed origins
            policy
                .WithOrigins("http://localhost:19006", "exp://192.168.1.100:19000") // Add your production origins
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// Add health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Order matters - this is critical for middleware
app.UseRouting();
app.UseCors();
app.UseAuthorization();

// Map endpoints
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");
app.MapHealthChecks("/health");

// Add a simple root endpoint
app.MapGet("/", () => "Pokemon Quiz API is running!");

app.Run();