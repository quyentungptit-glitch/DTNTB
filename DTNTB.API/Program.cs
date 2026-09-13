using DTNTB.Core.Interfaces;
using DTNTB.Infrastructure.Services;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IO;
using System.Text;
using System.Threading.RateLimiting;
using DTNTB.API.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

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
        Credential = GoogleCredential.FromFile(firebaseKeyPath)
    });
}

// ==========================================
// 2. DEPENDENCY INJECTION & FORWARDED HEADERS
// ==========================================
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddHttpClient<IAuthService, AuthService>();
builder.Services.AddScoped<IDtntbService, DtntbService>();
builder.Services.AddScoped<IHeThongService, HeThongService>();
builder.Services.AddHostedService<DTNTB.API.BackgroundWorkers.FcmNotificationWorker>();

// Đăng ký Typed HttpClient cho FileStorageService (tối ưu socket và pooling)
builder.Services.AddHttpClient<IFileStorageService, RemoteFileStorageService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60); // Thời gian chờ tối đa khi upload
});

// Cấu hình nhận diện Proxy trung gian
builder.Services.Configure<ForwardedHeadersOptions>(options => // <-- 2. THÊM MỚI
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
        limiterOptions.AutoReplenishment = true;
    });
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

// Nhận diện IP/Domain từ Reverse Proxy (phải nằm đầu tiên)
app.UseForwardedHeaders(); // <-- 3. ĐẶT ĐẦU TIÊN

// Swagger chỉ dùng khi phát triển nội bộ.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
