using DTNTB.Core.Interfaces;
using DTNTB.Infrastructure.Services;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using DTNTB.API.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

    // Dùng kho khóa riêng cho Development, tránh xung đột DPAPI key do IIS/tài khoản khác tạo.
    var developmentKeyDirectory = new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, ".dev-data-protection-keys"));
    builder.Services
        .AddDataProtection()
        .SetApplicationName("DTNTB.API.Development")
        .PersistKeysToFileSystem(developmentKeyDirectory);
}

void LoadSecretFile(string configurationKey)
{
    var environmentVariable = configurationKey.Replace(":", "__") + "_FILE";
    var secretPath = Environment.GetEnvironmentVariable(environmentVariable);
    if (string.IsNullOrWhiteSpace(secretPath)) return;
    if (!File.Exists(secretPath))
        throw new InvalidOperationException($"Không tìm thấy secret file cho {configurationKey}.");

    var value = File.ReadAllText(secretPath).TrimEnd('\r', '\n');
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Secret file cho {configurationKey} đang rỗng.");
    builder.Configuration[configurationKey] = value;
}

foreach (var secretKey in new[]
{
    "ConnectionStrings:ConnectionString_NBH",
    "Jwt:Key",
    "LegacyJwt:Key",
    "Sso:ApiKey",
    "SystemToSystem:SecretKey",
    "RemoteStorage:InternalApiKey"
})
{
    LoadSecretFile(secretKey);
}

// ==========================================
// 1. KHỞI TẠO FIREBASE
// ==========================================
var configuredFirebaseKeyPath = builder.Configuration["Firebase:ServiceAccountPath"];
var firebaseKeyPath = string.IsNullOrWhiteSpace(configuredFirebaseKeyPath)
    ? string.Empty
    : Path.IsPathFullyQualified(configuredFirebaseKeyPath)
        ? configuredFirebaseKeyPath
        : Path.Combine(builder.Environment.ContentRootPath, configuredFirebaseKeyPath);
if (System.IO.File.Exists(firebaseKeyPath))
{
    FirebaseApp.Create(new AppOptions()
    {
        Credential = CredentialFactory.FromFile<ServiceAccountCredential>(firebaseKeyPath).ToGoogleCredential()
    });
}

// ==========================================
// 2. DEPENDENCY INJECTION & FORWARDED HEADERS
// ==========================================
builder.Services.AddHttpContextAccessor();
var maxRequestSizeMb = Math.Clamp(builder.Configuration.GetValue<int?>("Upload:MaxRequestSizeInMB") ?? 15, 1, 25);
var maxRequestSizeBytes = maxRequestSizeMb * 1024L * 1024L;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestSizeBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxRequestSizeBytes);
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddHttpClient<IAuthService, AuthService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.MaxResponseContentBufferSize = 1024 * 1024;
});
builder.Services.AddScoped<IDtntbService, DtntbService>();
builder.Services.AddScoped<IHeThongService, HeThongService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
if (builder.Configuration.GetValue<bool>("BackgroundWorkers:FcmNotificationEnabled"))
{
    builder.Services.AddHostedService<DTNTB.API.BackgroundWorkers.FcmNotificationWorker>();
}

// Đăng ký Typed HttpClient cho FileStorageService (tối ưu socket và pooling)
builder.Services.AddHttpClient<IFileStorageService, RemoteFileStorageService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60); // Thời gian chờ tối đa khi upload
});

// Cấu hình nhận diện Proxy trung gian
builder.Services.Configure<ForwardedHeadersOptions>(options => // <-- 2. THÊM MỚI
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = Math.Clamp(builder.Configuration.GetValue<int?>("ReverseProxy:ForwardLimit") ?? 1, 1, 5);

    foreach (var configuredProxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
    {
        if (IPAddress.TryParse(configuredProxy, out var proxyAddress)
            && !options.KnownProxies.Contains(proxyAddress))
        {
            options.KnownProxies.Add(proxyAddress);
        }
    }
});

// ==========================================
// 3. CẤU HÌNH JWT AUTHENTICATION
// ==========================================
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key chưa được cấu hình.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer chưa được cấu hình.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience chưa được cấu hình.");
var key = Encoding.UTF8.GetBytes(jwtKey);
if (key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key phải có tối thiểu 32 byte ngẫu nhiên.");
}
builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = true;
    x.SaveToken = false;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    x.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Trình duyệt dùng cookie HttpOnly; ứng dụng di động vẫn có thể dùng Bearer token.
            if (string.IsNullOrWhiteSpace(context.Token)
                && context.Request.Cookies.TryGetValue("dtntb_access_token", out var cookieToken))
            {
                context.Token = cookieToken;
            }
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtValidation");
            logger.LogWarning("JWT authentication failed: {Reason}", context.Exception.GetType().Name);
            return Task.CompletedTask;
        }
    };
});

// ==========================================
// 4. CẤU HÌNH CORS (CHUẨN BẢO MẬT HƠN)
// ==========================================
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?.Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out _))
    .ToArray() ?? Array.Empty<string>();
if (allowedOrigins.Length == 0)
{
    throw new InvalidOperationException("Cors:AllowedOrigins chưa được cấu hình.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "DTNTB.API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "Nhập Token: Bearer {token}",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

// TLS được kết thúc tại reverse proxy; container API chỉ nghe HTTP nội bộ.
app.UseForwardedHeaders();
app.UseCors("AllowAngular");

// Cookie xác thực chỉ được phép thực hiện request thay đổi dữ liệu từ frontend tin cậy.
// Bearer token của ứng dụng di động không phụ thuộc Origin và vẫn hoạt động bình thường.
var allowedOriginSet = allowedOrigins.ToHashSet(StringComparer.OrdinalIgnoreCase);
app.Use(async (context, next) =>
{
    var isUnsafeMethod = !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method)
        && !HttpMethods.IsOptions(context.Request.Method)
        && !HttpMethods.IsTrace(context.Request.Method);
    var usesCookieAuthentication = context.Request.Cookies.ContainsKey("dtntb_access_token")
        && !context.Request.Headers.ContainsKey("Authorization");

    if (isUnsafeMethod && usesCookieAuthentication)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin) || !allowedOriginSet.Contains(origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Origin không được phép." });
            return;
        }
    }

    await next();
});

// Swagger chỉ dùng khi phát triển nội bộ.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
