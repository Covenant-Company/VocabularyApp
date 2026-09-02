using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using VocabularyApp.Data;
using VocabularyApp.Data.Models;
using VocabularyApp.WebApi.Configuration;
using VocabularyApp.WebApi.Helpers;
using VocabularyApp.WebApi.Security;
using VocabularyApp.WebApi.Services;

AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
var builder = WebApplication.CreateBuilder(args);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? new[] { "http://localhost:4200", "https://localhost:4200" };

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtSettings = JwtSettings.BindAndValidate(builder.Configuration);
builder.Services.AddSingleton(jwtSettings);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = jwtSettings.CreateTokenValidationParameters();
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<JwtHelper>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<ILegacyPasswordVerifier, LegacyPasswordVerifier>();
builder.Services.AddSingleton<IPasswordService, PasswordService>();

builder.Services.AddHttpClient<IWordService, WordService>(client =>
{
    var baseUrl = builder.Configuration["WordsApi:BaseUrl"]
        ?? "https://wordsapiv1.p.rapidapi.com/";
    var apiKey = builder.Configuration["WordsApi:ApiKey"];
    var host = builder.Configuration["WordsApi:Host"]
        ?? "wordsapiv1.p.rapidapi.com";

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.DefaultRequestHeaders.Add("X-RapidAPI-Host", host);
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-RapidAPI-Key", apiKey);
    }
});

var merriamWebsterOptions = builder.Configuration
    .GetSection(MerriamWebsterOptions.SectionName)
    .Get<MerriamWebsterOptions>()
    ?? new MerriamWebsterOptions();
builder.Services.AddSingleton(merriamWebsterOptions);
builder.Services
    .AddHttpClient<IPronunciationAudioService, MerriamWebsterPronunciationService>(client =>
    {
        var baseUrl = Uri.TryCreate(
            merriamWebsterOptions.BaseUrl,
            UriKind.Absolute,
            out var configuredBaseUrl) && configuredBaseUrl.Scheme == Uri.UriSchemeHttps
            ? new Uri(configuredBaseUrl.AbsoluteUri.TrimEnd('/') + "/")
            : new Uri(MerriamWebsterOptions.DefaultBaseUrl);
        var timeoutSeconds = Math.Clamp(
            merriamWebsterOptions.TimeoutSeconds,
            1,
            30);

        client.BaseAddress = baseUrl;
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    // The provider contract places the secret in the query string. Suppress the
    // default HttpClient request logger so it cannot record that URI.
    .RemoveAllLoggers();

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Vocabulary App API",
        Version = "v1",
        Description = "A comprehensive vocabulary building application with dictionary lookup, personal collections, and quiz functionality."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Vocabulary App API v1");
    c.RoutePrefix = "swagger";
});

// app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAngular");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("{*path:nonfile}", "index.html").AllowAnonymous();

app.Run();

public partial class Program
{
}
