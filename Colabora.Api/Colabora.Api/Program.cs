using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Colabora.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// reCAPTCHA
using Colabora.Api.Models;
using Colabora.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ===============================
// SERILOG (logs a consola)
// ===============================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Host.UseSerilog();

// ===============================
// DB: EF Core -> ColaboraWeb
// ===============================
builder.Services.AddDbContext<ColaboraDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("ColaboraDb")));

// ===============================
// CORS: permitir front Angular
// ===============================
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:4200", "https://localhost:4200" };

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    p.WithOrigins(allowedOrigins)
     .AllowAnyHeader()
     .AllowAnyMethod()
     .AllowCredentials(); // usamos cookie HttpOnly
}));

// ===============================
// JWT: validación + lectura cookie
// ===============================
var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
            ClockSkew = TimeSpan.Zero
        };

        // Leer el token desde la cookie colabora_token
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                if (ctx.Request.Cookies.TryGetValue("colabora_token", out var token))
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===============================
// Google reCAPTCHA
// ===============================
builder.Services.Configure<GoogleReCaptchaSettings>(
    builder.Configuration.GetSection("GoogleReCaptcha"));

builder.Services.AddHttpClient<IReCaptchaVerifier, ReCaptchaVerifier>();

var app = builder.Build();

app.UseSerilogRequestLogging();

// límite REAL de inactividad: 2 horas
var inactivityLimit = TimeSpan.FromHours(2);

// ======================================================
// MIDDLEWARE GLOBAL de inactividad / cierre de sesión
// ======================================================
app.Use(async (ctx, next) =>
{
    var db = ctx.RequestServices.GetRequiredService<ColaboraDbContext>();
    var token = ctx.Request.Cookies["colabora_token"];

    if (!string.IsNullOrEmpty(token))
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var jwt = handler.ReadJwtToken(token);
            var jtiStr = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

            if (Guid.TryParse(jtiStr, out var jti))
            {
                var session = await db.UserSessions
                    .FirstOrDefaultAsync(s => s.Jti == jti && s.IsActive);

                if (session != null)
                {
                    var now = DateTime.UtcNow;
                    var idleTime = now - session.LastSeenAt;

                    // Si han pasado más de 2h o llegó la expiración -> cerrar sesión
                    if (idleTime >= inactivityLimit || now >= session.ExpiresAt)
                    {
                        session.IsActive = false;
                        session.LastSeenAt = now;
                        await db.SaveChangesAsync();

                        // Borrar cookie con mismos flags que en el login
                        ctx.Response.Cookies.Append("colabora_token", "", new CookieOptions
                        {
                            HttpOnly = true,
                            SameSite = SameSiteMode.None,
                            Secure = true,
                            Path = "/",
                            Expires = DateTimeOffset.UtcNow.AddDays(-1)
                        });

                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await ctx.Response.WriteAsync("Sesión expirada por inactividad.");
                        return;
                    }

                    // Sesión válida → refrescar último acceso
                    session.LastSeenAt = now;
                    await db.SaveChangesAsync();
                }
            }
        }
        catch
        {
            // Si el token está corrupto, dejamos que Authentication/JWT lo maneje
        }
    }

    await next();
});

// IMPORTANTE: orden de middlewares
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();
