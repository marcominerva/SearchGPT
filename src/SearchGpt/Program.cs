using System.Diagnostics;
using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using ChatGptNet;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi.Models;
using MinimalHelpers.OpenApi;
using MinimalHelpers.Routing;
using OperationResults.AspNetCore.Http;
using SearchGpt.BusinessLayer.Services;
using SearchGpt.BusinessLayer.Services.Interfaces;
using SearchGpt.BusinessLayer.Settings;
using SearchGpt.Extensions;
using SearchGpt.Swagger;
using TinyHelpers.AspNetCore.Extensions;
using TinyHelpers.AspNetCore.Swagger;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true);

// Add services to the container.
var settings = builder.Services.ConfigureAndGet<AppSettings>(builder.Configuration, nameof(AppSettings));
var swagger = builder.Services.ConfigureAndGet<SwaggerSettings>(builder.Configuration, nameof(SwaggerSettings));
var cognitiveSearchSettings = builder.Services.ConfigureAndGet<CognitiveSearchSettings>(builder.Configuration, nameof(CognitiveSearchSettings));

builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages();

builder.Services.AddWebOptimizer(minifyCss: true, minifyJavaScript: builder.Environment.IsProduction());

builder.Services.AddChatGpt(builder.Configuration);

builder.Services.AddSingleton<SearchClient>(_ =>
{
    // Create a SearchIndexClient to send create/delete index commands
    var serviceEndpoint = new Uri($"https://{cognitiveSearchSettings.ServiceName}.search.windows.net/");
    var credential = new AzureKeyCredential(cognitiveSearchSettings.ApiKey);

    // Create a SearchClient to load and query documents
    var client = new SearchClient(serviceEndpoint, cognitiveSearchSettings.IndexName, credential);
    return client;
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var statusCode = context.ProblemDetails.Status.GetValueOrDefault(StatusCodes.Status500InternalServerError);
        context.ProblemDetails.Type ??= $"https://httpstatuses.io/{statusCode}";
        context.ProblemDetails.Title ??= ReasonPhrases.GetReasonPhrase(statusCode);
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddScoped<IChatService, ChatService>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

if (swagger.IsEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "SearchGpt API", Version = "v1" });

        options.AddDefaultResponse();
        options.AddMissingSchemas();
    });
}

builder.Services.AddOperationResult(options =>
{
    options.ErrorResponseFormat = ErrorResponseFormat.List;
});

var app = builder.Build();
app.Services.GetRequiredService<IWebHostEnvironment>().ApplicationName = settings.ApplicationName;

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.UseWhen(context => context.IsWebRequest(), builder =>
{
    if (!app.Environment.IsDevelopment())
    {
        builder.UseExceptionHandler("/errors/500");

        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        builder.UseHsts();
    }

    builder.UseStatusCodePagesWithReExecute("/errors/{0}");
});

app.UseWhen(context => context.IsApiRequest(), builder =>
{
    if (!app.Environment.IsDevelopment())
    {
        // Error handling
        builder.UseExceptionHandler(new ExceptionHandlerOptions
        {
            AllowStatusCode404Response = true,
            ExceptionHandler = async (HttpContext context) =>
            {
                var problemDetailsService = context.RequestServices.GetService<IProblemDetailsService>();
                var exceptionHandlerFeature = context.Features.Get<IExceptionHandlerFeature>();
                var error = exceptionHandlerFeature?.Error;

                // Write as JSON problem details
                await problemDetailsService.WriteAsync(new()
                {
                    HttpContext = context,
                    AdditionalMetadata = exceptionHandlerFeature?.Endpoint?.Metadata,
                    ProblemDetails =
                    {
                        Status = context.Response.StatusCode,
                        Title = error?.GetType().FullName ?? "An error occurred while processing your request",
                        Detail = error?.Message
                    }
                });
            }
        });
    }

    builder.UseStatusCodePages();
});

app.UseWebOptimizer();
app.UseStaticFiles();

if (swagger.IsEnabled)
{
    app.UseMiddleware<SwaggerBasicAuthenticationMiddleware>();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SearchGpt API v1");
        options.InjectStylesheet("/css/swagger.css");
    });
}

app.UseRouting();

// app.UseCors();

// In Razor Pages apps and apps with controllers, UseOutputCache must be called after UseRouting.
//app.UseOutputCache();

app.MapEndpoints();
app.MapRazorPages();

app.Run();