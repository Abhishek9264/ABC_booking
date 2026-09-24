using Microsoft.EntityFrameworkCore;
using Serilog;
using SearchService;
using SearchService.Caching;
using Tatkal.Authentication;
using Tatkal.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddTatkalObservability(builder.Configuration, "search-service");

builder.Services.AddDbContext<SearchDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddMemoryCache();
builder.Services.AddStackExchangeRedisCache(o =>
    o.Configuration = builder.Configuration["Redis:ConnectionString"]);
builder.Services.AddScoped<ISearchCacheService, SearchCacheService>();

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
