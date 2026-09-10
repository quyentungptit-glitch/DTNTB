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
// 2. ĐĂNG KÝ DEPENDENCY INJECTION (DI) & SECURITY CHUẨN MỚI
// ==========================================
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Kích hoạt Dynamic Authorization Policy Provider
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Đăng ký HttpClient kết nối SSO & Service
builder.Services.AddHttpClient<IAuthService, AuthService>();
builder.Services.AddScoped<IDtntbService, DtntbService>();
builder.Services.AddScoped<IHeThongService, HeThongService>(); // <-- THÊM DÒNG NÀY

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
// 4. CẤU HÌNH CORS
// ==========================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular",
        policy => policy.WithOrigins("http://localhost:4200")
                        .AllowAnyMethod()
                        .AllowAnyHeader());
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

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseCors("AllowAngular");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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

app.Run();