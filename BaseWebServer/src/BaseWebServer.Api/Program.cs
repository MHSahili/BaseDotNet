using BaseWebServer.Api.Common.Caching;
using BaseWebServer.Api.Common.Errors;
using BaseWebServer.Api.Common.Observability;
using BaseWebServer.Api.Data;
using BaseWebServer.Api.Features;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddErrorHandling();
builder.Services.AddCaching(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFeatures();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapHealthChecks("/health/live").AllowAnonymous();

app.Run();

public partial class Program;
