using Seed.Dashboard;
using Seed.Dashboard.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add simulation runner as singleton and hosted service
builder.Services.AddSingleton<SimulationRunner>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationRunner>());

var app = builder.Build();

// Configure pipeline
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<SimulationHub>("/hub/simulation");

// Fallback for SPA
app.MapFallbackToFile("index.html");

Console.WriteLine("=== Seed Dashboard ===");
Console.WriteLine("Backend running at: http://localhost:5000");
Console.WriteLine("SignalR Hub at: http://localhost:5000/hub/simulation");
Console.WriteLine();

app.Run();
