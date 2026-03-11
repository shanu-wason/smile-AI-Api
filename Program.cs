using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Serilog;
using SmileApi.Application.Interfaces;
using SmileApi.Application.Services;
using SmileApi.Domain.Engines;
using SmileApi.Infrastructure.AI;
using SmileApi.Infrastructure.ImageProcessing;
using SmileApi.Infrastructure.Persistence;
using smile_api.Hubs;
using smile_api.Infrastructure;
using smile_api.Middleware;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddControllers();

    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50 MB
    });
    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50 MB
    });

    // JWT & Auth
    builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
    builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
    builder.Services.AddScoped<IUserRepository, EfUserRepository>();
    builder.Services.AddScoped<IAuthService, AuthService>();

    var jwtKey = builder.Configuration["Jwt:Key"] ?? "";
    if (!string.IsNullOrWhiteSpace(jwtKey))
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "smile-api",
                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["Jwt:Audience"] ?? "smile-api",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
    }

    // Application
    builder.Services.AddScoped<ISmileScanService, SmileScanService>();
    builder.Services.AddSignalR();
    builder.Services.AddScoped<IScanProgressNotifier, SignalRScanProgressNotifier>();

    // Domain engines
    builder.Services.AddSingleton<ISmileScoringEngine, SmileScoringEngine>();
    builder.Services.AddSingleton<IFeatureConsistencyEngine, FeatureConsistencyEngine>();
    builder.Services.AddSingleton<IConfidenceEngine, ConfidenceEngine>();

    // Infrastructure
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("SupabaseConnection")));
    builder.Services.AddHttpClient<IImageProcessingService, ImageProcessingService>();
    builder.Services.AddScoped<ISmileScanRepository, SupabaseSmileScanRepository>();

    builder.Services.AddHttpClient<IAIAnalysisService, OpenAIAnalysisService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddTransientHttpErrorPolicy(policyBuilder => policyBuilder.WaitAndRetryAsync(
        Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(1), 3)
    ))
    .AddTransientHttpErrorPolicy(policyBuilder => policyBuilder.AdvancedCircuitBreakerAsync(
        failureThreshold: 0.5,
        samplingDuration: TimeSpan.FromSeconds(30),
        minimumThroughput: 3,
        durationOfBreak: TimeSpan.FromSeconds(30)
    ));

    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    }).AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        });
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddFixedWindowLimiter("strict", options =>
        {
            options.PermitLimit = 10;
            options.Window = TimeSpan.FromMinutes(1);
            options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            options.QueueLimit = 0;
        });
    });

    var app = builder.Build();

    app.UseCors("AllowAll");
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers().RequireRateLimiting("strict");
    app.MapHub<ScanLogicHub>("/hubs/scan");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
