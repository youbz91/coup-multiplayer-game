using Coup.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// CORS Configuration - Secure by default, configurable per environment
var allowedOrigins = builder.Configuration.GetSection("CorsSettings:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5000" }; // Fallback for development

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for SignalR
    });
});

builder.Services.AddSignalR();

// Service en mémoire pour stocker l'état des parties (thread-safe)
builder.Services.AddSingleton<GameStore>();

// Game lock service for preventing race conditions
builder.Services.AddSingleton<GameLockService>();

// Background service pour vérifier les timeouts automatiquement
builder.Services.AddHostedService<GameTimeoutService>();

// Background service for cleaning up old games
builder.Services.AddHostedService<GameCleanupService>();

var app = builder.Build();

app.UseCors();

app.MapHub<CoupHub>("/couphub");  // endpoint SignalR

app.Run();
