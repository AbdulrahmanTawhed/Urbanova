using FluentValidation;
using Scalar.AspNetCore;
using Serilog;
using Urbanova.Api.Middleware;
using Urbanova.Api.Services;
using Urbanova.Application.Analysis;
using Urbanova.Application.Auth;
using Urbanova.Application.Common;
using Urbanova.Application.Costing;
using Urbanova.Application.Projects;
using Urbanova.Application.Scenarios;

var builder = WebApplication.CreateBuilder(args);

// --- Logging (Serilog -> console; sinks/file added in later phases) ---
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// --- API baseline ---
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();      // Microsoft.AspNetCore.OpenApi (net10)

// Explicit validator registration (no reflection-based scanning by design).
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<RefreshRequest>, RefreshRequestValidator>();
builder.Services.AddScoped<IValidator<CreateProjectRequest>, CreateProjectRequestValidator>(); // Phase 4
builder.Services.AddScoped<IValidator<UpdateProjectRequest>, UpdateProjectRequestValidator>();
builder.Services.AddScoped<IValidator<AnalyzeRequest>, AnalyzeRequestValidator>(); // Phase 7
builder.Services.AddScoped<IValidator<CreateScenarioRequest>, CreateScenarioRequestValidator>(); // Phase 8
builder.Services.AddScoped<IValidator<UpdateScenarioRequest>, UpdateScenarioRequestValidator>();
builder.Services.AddScoped<IValidator<CreateCostEstimateRequest>, CreateCostEstimateRequestValidator>(); // Phase 11

// Caller identity for owner-scoped services (Phase 4).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

// Phase 2: SQL Server persistence (skipped when no connection string, e.g. CI smoke).
var sqlConnectionString =
    Urbanova.Infrastructure.Persistence.DependencyInjection.AddUrbanovaPersistence(builder.Services, builder.Configuration);
if (sqlConnectionString is not null)
{
    builder.Services.AddHealthChecks().AddSqlServer(sqlConnectionString, name: "urbanova-db", tags: ["ready", "db"]);

    // Phase 3: Identity + JWT + authorization (requires persistence for EF stores).
    // Skipped with persistence; full API requires a database (documented in env-vars.md).
    Urbanova.Infrastructure.Auth.AuthDependencyInjection.AddUrbanovaAuth(
        builder.Services, builder.Configuration, builder.Environment);
}

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("URBANOVA API")).AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live").AllowAnonymous().WithName("Live");
app.MapHealthChecks("/health/ready").AllowAnonymous().WithName("Ready");

// Diagnostics redirect (public by design).
app.MapGet("/", () => Results.Redirect("/scalar/v1"))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();

// Required for WebApplicationFactory in IntegrationTests.
public partial class Program;
