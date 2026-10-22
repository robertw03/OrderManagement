using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Api.Common.Logging;
using OrderManagement.Api.Common.Validation;
using OrderManagement.Api.Features.Auth;
using OrderManagement.Api.Features.Demo;
using OrderManagement.Api.Features.Logs;
using OrderManagement.Api.Features.Orders;
using OrderManagement.Api.Features.Products;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging: Serilog, configured from appsettings ("Serilog" section) ----
// Seq (centralised log server) is optional: set Seq:ServerUrl to ship every event there as well.
var seqUrl = builder.Configuration["Seq:ServerUrl"];
builder.Services.AddSerilog((services, config) =>
{
    config
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "OrderManagement.Api");
    if (!string.IsNullOrWhiteSpace(seqUrl))
        config.WriteTo.Seq(seqUrl, apiKey: builder.Configuration["Seq:ApiKey"]);
});

// ---- Persistence (needs ICurrentUser + TimeProvider for the audit columns) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton(TimeProvider.System);
var connectionString = builder.Configuration.GetConnectionString("OrderDb")
    ?? throw new InvalidOperationException("Connection string 'OrderDb' is not configured.");
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHealthChecks().AddNpgSql(connectionString);

// ---- Application ----
builder.Services.Configure<OrderOptions>(builder.Configuration.GetSection(OrderOptions.Section));
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ProductService>();
builder.Services.Configure<LogViewerOptions>(builder.Configuration.GetSection(LogViewerOptions.Section));
builder.Services.AddSingleton<LogReader>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ---- Security: JWT bearer, every endpoint requires login unless marked anonymous ----
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// ---- Web ----
builder.Services
    .AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();
        // Let FluentValidation own "required" rules so every validation message comes from one place.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        var result = defaultFactory(context);
        if (result is ObjectResult { Value: ProblemDetails problem })
        {
            if (problem is ValidationProblemDetails validation)
            {
                // Same key style for body and query errors: "items[0].quantity", "pageSize".
                var errors = validation.Errors.ToDictionary(
                    e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value);
                validation.Errors.Clear();
                foreach (var (key, messages) in errors)
                    validation.Errors[key] = messages;
            }
            problem.Title = "Validation failed";
            problem.Detail = "One or more fields are invalid. See 'errors' for details.";
            problem.Extensions["errorCode"] = ErrorCodes.ValidationFailed;
        }
        return result;
    };
});

// Every error response — exceptions, validation, unknown routes — goes through ProblemDetails and
// gets the same extra fields, so clients deal with exactly one error shape.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var problem = context.ProblemDetails;
    problem.Instance ??= context.HttpContext.Request.Path;
    problem.Extensions.TryAdd("errorCode", ErrorCodes.FromStatusCode(problem.Status ?? 500));
    problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
    problem.Extensions.Remove("traceId");
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}
if (app.Configuration.GetValue<bool>("Database:SeedDemoUsers"))
    await DemoUserSeeder.SeedAsync(app.Services);

// Order matters: the correlation id must exist before anything logs, and request logging wraps the
// exception handler so the logged status code is the one the client actually received.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
    // Reading the logs or polling health would otherwise flood the very logs being read.
    options.GetLevel = (http, _, exception) =>
        exception is not null || http.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/api/logs") || http.Request.Path.StartsWithSegments("/health")
            ? Serilog.Events.LogEventLevel.Debug
            : Serilog.Events.LogEventLevel.Information;
    options.EnrichDiagnosticContext = (diagnostics, http) =>
    {
        diagnostics.Set("QueryString", http.Request.QueryString.Value);
        diagnostics.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
        diagnostics.Set("UserAgent", http.Request.Headers.UserAgent.ToString());
        if (http.Request.Headers.TryGetValue(OrdersController.IdempotencyKeyHeader, out var key))
            diagnostics.Set("IdempotencyKey", key.ToString());
        diagnostics.Set("UserName", http.User.Identity?.Name ?? "anonymous");
    };
});
app.UseExceptionHandler();
app.UseStatusCodePages();

// Demo UI (wwwroot) — plain HTML/JS calling the same REST API.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
// Every log line written while handling the request carries the signed-in user.
app.Use(async (context, next) =>
{
    using (LogContext.PushProperty("UserName", context.User.Identity?.Name ?? "anonymous"))
        await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
    app.MapDemoEndpoints();
}

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();

public partial class Program
{
    protected Program() { }
}
