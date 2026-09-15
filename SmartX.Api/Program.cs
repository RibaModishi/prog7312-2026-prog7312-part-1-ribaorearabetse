using System.Text.Json.Serialization;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        // Categories and node types travel as their names rather than ordinals, so a payload
        // stays readable in Swagger and a reordered enum cannot silently change its meaning.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

// The gateway keeps the deployment tree and all accumulated telemetry in process, so the store
// must be a singleton; a scoped registration would discard every registered sensor per request.
builder.Services.AddSingleton<SmartXStore>();
builder.Services.AddSingleton<AlertAggregationService>();
builder.Services.AddHostedService<SensorDisconnectWatcher>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Smart-X Gateway API"));
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();
