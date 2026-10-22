using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Api.Common.Logging;
using OrderManagement.Api.Common.Validation;
using OrderManagement.Api.Features.Demo;
using OrderManagement.Api.Features.Orders;
using OrderManagement.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Logging: Serilog, configured from appsettings ("Serilog" section) ----
builder.Services.AddSerilog((services, config) => config
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "OrderManagement.Api"));

// ---- Persistence (needs ICurrentUser + TimeProvider for the audit columns) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton(TimeProvider.System);
var connectionString = builder.Configuration.GetConnectionString("OrderDb")
    ?? throw new InvalidOperationException("Connection string 'OrderDb' is not configured.");
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddHealthChecks().AddNpgSql(connectionString);

// ---- Application ----
builder.Services.AddScoped<OrderService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

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
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}

// Order matters: the correlation id must exist before anything logs, and request logging wraps the
// exception handler so the logged status code is the one the client actually received.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
    options.EnrichDiagnosticContext = (diagnostics, http) =>
    {
        diagnostics.Set("QueryString", http.Request.QueryString.Value);
        diagnostics.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString());
        diagnostics.Set("UserAgent", http.Request.Headers.UserAgent.ToString());
        if (http.Request.Headers.TryGetValue(OrdersController.IdempotencyKeyHeader, out var key))
            diagnostics.Set("IdempotencyKey", key.ToString());
    };
});
app.UseExceptionHandler();
app.UseStatusCodePages();

// Demo UI (wwwroot) — plain HTML/JS calling the same REST API.
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapDemoEndpoints();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
