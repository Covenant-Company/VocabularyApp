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

builder.Services.AddHsts(options =>
{
    // Short initial rollout policy; increases require separate review.
    options.MaxAge = TimeSpan.FromSeconds(300);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

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

// Production UI and API are same-origin; only local development needs CORS.
if (builder.Environment.IsDevelopment())
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(origin => origin.TrimEnd('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (allowedOrigins is not { Length: > 0 })
        throw new InvalidOperationException("Development requires Cors:AllowedOrigins configuration.");

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAngular", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });
}

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

if (app.Environment.IsProduction())
{
    app.UseWhen(context => string.Equals(context.Request.Host.Host,
        "myvocabularybuilder.org", StringComparison.OrdinalIgnoreCase),
        productionHost => productionHost.UseHsts());
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Vocabulary App API v1");
    c.RoutePrefix = "swagger";
});

// The published root web.config is the single IIS HTTP enforcement authority.

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
    app.UseCors("AllowAngular");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("{*path:nonfile}", "index.html").AllowAnonymous();

app.Run();

public partial class Program
{
}
