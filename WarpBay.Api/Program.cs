using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using WarpBay.Api.Auth;
using WarpBay.Api.Data;
using WarpBay.Api.Endpoints;
using WarpBay.Api.Hubs;
using WarpBay.Api.Seed;
using WarpBay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddMemoryCache();
builder.Services.AddSignalR();
builder.Services.AddScoped<SchedulingService>();
builder.Services.AddHostedService<ReminderWorker>();

// Provider switch: Sqlite (default, zero-setup demo) | SqlServer (resume keyword) | Npgsql (Render Postgres)
var provider = builder.Configuration.GetValue("Provider", "Sqlite");
var connSqlite = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=warpbay.db";
var conn = provider switch
{
    "SqlServer" => builder.Configuration.GetConnectionString("SqlServer"),
    "Npgsql" => builder.Configuration.GetConnectionString("Npgsql"),
    _ => connSqlite,
};
builder.Services.AddDbContext<WarpBayDb>(opt =>
{
    if (provider == "SqlServer") opt.UseSqlServer(conn);
    else if (provider == "Npgsql") opt.UseNpgsql(conn);
    else opt.UseSqlite(connSqlite);
});

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        };
        // SignalR JWT via query string
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("public", c =>
{
    c.PermitLimit = 60; c.Window = TimeSpan.FromMinutes(1);
}));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Warp Bay Auto Lab API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header, Name = "Authorization", Type = SecuritySchemeType.Http,
        Scheme = "bearer", BearerFormat = "JWT", Description = "Paste JWT from /api/auth/login or /api/auth/demo",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>()
    });
});
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health").WithOpenApi();
app.MapHub<ScheduleHub>("/hubs/schedule");
AuthEndpoints.Map(app);
ApiEndpoints.Map(app);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WarpBayDb>();
    await SeedData.EnsureSeededAsync(db);
}

app.Run();
