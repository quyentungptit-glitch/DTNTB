using DTNTB.Core.Interfaces;
using DTNTB.Infrastructure.Services;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.IO;
using System.Text;
using DTNTB.API.Security;
using Microsoft.AspNetCore.HttpOverrides; // <-- 1. THÊM MỚI

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. KHỞI TẠO FIREBASE
// ==========================================
var firebaseKeyPath = Path.Combine(builder.Environment.ContentRootPath, "firebase-service-account.json");
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

// Đăng ký Typed HttpClient cho FileStorageService (tối ưu socket và pooling)
builder.Services.AddHttpClient<IFileStorageService, RemoteFileStorageService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60); // Thời gian chờ tối đa khi upload
});

// Cấu hình nhận diện Proxy trung gian
builder.Services.Configure<ForwardedHeadersOptions>(options => // <-- 2. THÊM MỚI
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ==========================================
// 3. CẤU HÌNH JWT AUTHENTICATION
// ==========================================
var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"] ?? "Trung_Tam_Ha_Tang_Ma_Bao_Mat_Mac_Dinh_256_bit_VNPT");
builder.Services.AddAuthentication(x =>
{
    x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

// ==========================================
// 4. CẤU HÌNH CORS (CHUẨN BẢO MẬT HƠN)
// ==========================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://clm.vnptninhbinh.com.vn"
              )
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
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

// Chỉ mở Swagger khi chạy Dev ở máy
if (app.Environment.IsDevelopment()) // <-- 4. BỌC LẠI
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAngular");

// Cấu hình Static Files đưa lên trước Authentication & Controllers
var physRoot = builder.Configuration["MatLuoi:PhysRoot"] ?? "D:\\DataUpload\\matluoi\\";
if (!Directory.Exists(physRoot))
{
    Directory.CreateDirectory(physRoot);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(physRoot),
    RequestPath = "/uploads/matluoi"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();