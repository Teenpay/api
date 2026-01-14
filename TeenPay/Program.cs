using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;
using TeenPay.Data;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF licences konfigurācija (PDF ģenerēšanai)
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// --------------------
// DB
// --------------------
var cs = builder.Configuration.GetConnectionString("Postgres");

// Npgsql DataSource (efektīvākai DB pieslēgumu pārvaldībai)
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(cs).Build());

// EF Core DbContext
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(cs));

// Health checks
builder.Services.AddHealthChecks().AddNpgSql(cs);

// Controllers
builder.Services.AddControllers();

// --------------------
// CORS (mobilajai aplikācijai)
// --------------------
builder.Services.AddCors(o => o.AddPolicy("mobile", p =>
    p.AllowAnyOrigin()
     .AllowAnyHeader()
     .AllowAnyMethod()));

// --------------------
// JWT Auth
// --------------------
var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.Zero
        };

        // DEV: atļaut HTTP, lai telefons nelūzt uz sertifikāta
        o.RequireHttpsMetadata = false;
        o.SaveToken = false;
    });

// Default authorization policy
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// --------------------
// Swagger (vienreiz + ar JWT)
// --------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TeenPay API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste token like: Bearer {your JWT token}"
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

// --------------------
// IMPORTANT: klausīties ārpus localhost (lai telefons var pieslēgties)
// Izvēlies portu, kas tev reāli ir (piem., 7051)
// --------------------
builder.WebHost.UseUrls("http://0.0.0.0:5165");

var app = builder.Build();

// Health check endpoint
app.MapHealthChecks("/health/db");

// Swagger tikai Dev vidē
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// PROD: tikai tur redirection uz HTTPS (DEV atstājam bez redirection)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("mobile");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
