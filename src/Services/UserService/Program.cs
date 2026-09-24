using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Tatkal.Authentication;
using Tatkal.Observability;
using UserService;
using UserService.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddTatkalObservability(builder.Configuration, "user-service");

builder.Services.AddDbContext<UserDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddTatkalJwtIssuing();
builder.Services.AddTatkalJwtAuthentication(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseTatkalCorrelationId();
app.UseAuthentication();
app.UseAuthorization();

app.MapTatkalMetrics();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
