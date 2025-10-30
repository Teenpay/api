using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using TeenPay.Data;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// Строка подключения
var cs = builder.Configuration.GetConnectionString("Postgres");

// One pooled datasource for the whole app
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(cs).Build());

// Регистрируем DbContext до builder.Build()
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Health Check для БД
builder.Services.AddHealthChecks().AddNpgSql(cs);

// Регистрируем контроллеры
builder.Services.AddControllers();

// CORS
builder.Services.AddCors(o => o.AddPolicy("mobile", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// JWT
var jwt = builder.Configuration.GetSection("Jwt");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new()
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
        o.RequireHttpsMetadata = true;
        o.SaveToken = false;
    });

// <-- авторизация по умолчанию: всё закрыто, кроме [AllowAnonymous]
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Регистрируем Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Регистрируем Health Checks
app.MapHealthChecks("/health/db");

// HTTP Request Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("mobile");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
