using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OmniConnect.Backend.Data;
using OmniConnect.Backend.Hubs;
using OmniConnect.Backend.Middleware;
using OmniConnect.Backend.Services;
using OmniConnect.Shared.Converters;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=omniconnect;Username=postgres;Password=postgres";
    options.UseNpgsql(connectionString);
});

// ── Authentication ────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? "ThisIsADevelopmentSecretKeyThatMustBeChangedInProduction!@#$%";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "OmniConnect",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "OmniConnect",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── Core Services ─────────────────────────────────────────────────────
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<CommunityService>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<CallService>();
builder.Services.AddScoped<EncryptionService>();

// ── Feature Services ──────────────────────────────────────────────────
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<RoutingService>();
builder.Services.AddSingleton<RoutingEngine>();
builder.Services.AddSingleton<DeviceDiscoveryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceDiscoveryService>());
builder.Services.AddHttpClient<AiService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddHttpClient("DeviceRegistration")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.Configure<DeviceDiscoveryOptions>(builder.Configuration.GetSection("DeviceDiscovery"));
builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddHostedService<AiBotService>();
builder.Services.AddSingleton<IAiBotState, AiBotState>();

// ── SignalR ───────────────────────────────────────────────────────────
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumParallelInvocationsPerClient = 5;
}).AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(new FlexibleEnumConverterFactory());
});

// ── CORS ──────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
    options.AddPolicy("SignalR", policy =>
    {
        policy.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    });
});

// ── Swagger ───────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OmniConnect API",
        Version = "v1",
        Description = "Enterprise Distributed Communication Platform"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter your JWT token"
    });
});

// ── Controllers ───────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new FlexibleEnumConverterFactory());
    });

// ── Host resilience ───────────────────────────────────────────────────
// A background service (AI bot, device discovery) must never be able to take
// the whole API down. Log and keep the other features alive instead.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

var app = builder.Build();

// ── Middleware Pipeline ───────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("SignalR");
app.UseAuthentication();
app.UseAuthorization();

// ── Health check ──────────────────────────────────────────────────────
// Used by the mobile client to auto-discover the backend on the local
// network before login, and handy for confirming the API is up.
app.MapGet("/health", async (AppDbContext db) =>
{
    var dbOk = true;
    try { await db.Database.CanConnectAsync(); }
    catch { dbOk = false; }

    return Results.Ok(new
    {
        status = "ok",
        database = dbOk ? "connected" : "unreachable",
        time = DateTime.UtcNow,
    });
});

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();

// ── Auto-migrate database in development ──────────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrated successfully.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database migration failed. Falling back to EnsureCreated.");
        try
        {
            // No migration history yet (or migrations unavailable) — build the
            // schema straight from the EF model so a fresh database just works.
            await db.Database.EnsureCreatedAsync();
            app.Logger.LogInformation("Database schema created from model.");
        }
        catch (Exception inner)
        {
            app.Logger.LogError(inner, "Could not initialise database schema.");
        }
    }
}

app.Run();