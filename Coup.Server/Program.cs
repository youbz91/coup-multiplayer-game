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

// Game action service (used by both regular hub and bots)
builder.Services.AddSingleton<Coup.Server.Services.GameActionService>();

// Bot AI services
builder.Services.AddSingleton<Coup.Server.AI.EasyBotStrategy>();
builder.Services.AddSingleton<Coup.Server.AI.MediumBotStrategy>();
builder.Services.AddSingleton<Coup.Server.AI.HardBotStrategy>();
builder.Services.AddSingleton<Coup.Server.Services.BotActionExecutor>();
builder.Services.AddSingleton<Coup.Server.Services.BotDecisionEngine>();
builder.Services.AddHostedService<Coup.Server.Services.BotOrchestrationService>();

var app = builder.Build();

app.UseCors();

app.MapHub<CoupHub>("/couphub");  // endpoint SignalR

app.Run();
