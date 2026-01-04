using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using TeenPay.Data;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF licences konfigurācija (PDF ģenerēšanai sistēmā)
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Datubāzes pieslēguma virkne no konfigurācijas (appsettings.json / secrets)
var cs = builder.Configuration.GetConnectionString("Postgres");

// Swagger (API dokumentācija) konfigurācija + JWT Bearer autorizācijas atbalsts Swagger vidē
builder.Services.AddSwaggerGen(c =>
{
    // Izveido Swagger dokumentu ar nosaukumu un versiju
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "TeenPay API", Version = "v1" });

    // Pievieno "Bearer" drošības definīciju, lai Swagger varētu sūtīt JWT tokenu Authorization headerī
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste token like: Bearer {your JWT token}"
    });

    // Norāda, ka šī drošības shēma jāpiemēro visiem endpointiem (ja nepieciešams)
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

// Vienots Npgsql DataSource visai lietotnei (efektīvākai DB savienojumu pārvaldībai)
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(cs).Build());

// Entity Framework DbContext reģistrācija (darbam ar teenpay DB caur modeļiem)
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Veselības pārbaude (Health Check) datubāzei — lai var pārbaudīt DB pieejamību
builder.Services.AddHealthChecks().AddNpgSql(cs);

// Kontrolieru reģistrācija (API maršruti un metodes)
builder.Services.AddControllers();

// CORS politika mobilajai lietotnei (atļauj pieprasījumus no jebkuras izcelsmes)
builder.Services.AddCors(o => o.AddPolicy("mobile", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// JWT konfigurācija: atslēga, izdevējs, auditorija un tokena validācijas noteikumi
var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));

// Autentifikācijas konfigurācija ar JWT Bearer (tokena pārbaude katram pieprasījumam)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Tokena validācijas parametri (issuer, audience, paraksts, derīguma termiņš)
        o.TokenValidationParameters = new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.Zero // Bez “pielaides” derīguma termiņam (precīza pārbaude)
        };

        // Pieprasa HTTPS metadatus (drošībai)
        o.RequireHttpsMetadata = true;

        // Netiek saglabāts token servera pusē (token paliek klienta pusē)
        o.SaveToken = false;
    });

// Noklusējuma autorizācijas politika:
// ja kontrolierī/metodē nav [AllowAnonymous], tad lietotājam jābūt autentificētam
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Swagger infrastruktūra (endpointu atklāšanai un dokumentēšanai)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Health Check endpoint (DB pieejamības pārbaude)
app.MapHealthChecks("/health/db");

// HTTP pieprasījumu apstrādes “pipeline”
if (app.Environment.IsDevelopment())
{
    // Izstrādes vidē ieslēdz Swagger UI
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Automātiska HTTP -> HTTPS pāradresācija
app.UseHttpsRedirection();

// CORS politika mobilajam klientam
app.UseCors("mobile");

// Autentifikācija (JWT tokena nolasīšana un validācija)
app.UseAuthentication();

// Autorizācija (piekļuves tiesību pārbaude pēc autentifikācijas)
app.UseAuthorization();

// Kontrolieru maršrutēšana (API endpointi)
app.MapControllers();

// Palaist lietotni
app.Run();
