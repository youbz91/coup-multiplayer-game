using Coup.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// CORS (dev) : autoriser tout pour tester en local / multi-machines LAN
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin()
));

builder.Services.AddSignalR();

// Service en mémoire pour stocker l'état des parties (thread-safe)
builder.Services.AddSingleton<GameStore>();

// Background service pour vérifier les timeouts automatiquement
builder.Services.AddHostedService<GameTimeoutService>();

var app = builder.Build();

app.UseCors();

app.MapHub<CoupHub>("/couphub");  // endpoint SignalR

app.Run();
