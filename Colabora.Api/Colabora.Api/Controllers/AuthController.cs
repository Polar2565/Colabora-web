using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Colabora.Api.Data;
using Colabora.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Colabora.Api.Services;

namespace Colabora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController : ControllerBase
{
    private readonly ColaboraDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IReCaptchaVerifier _captcha;

    private static readonly TimeSpan InactivityLimit = TimeSpan.FromHours(2);

    public AuthController(ColaboraDbContext db, IConfiguration cfg, IReCaptchaVerifier captcha)
    {
        _db = db;
        _cfg = cfg;
        _captcha = captcha;
    }

    // DTOs
    public record LoginReq(string Username, string Password, string? RecaptchaToken);
    public record RegisterReq(string Username, string Password, string? Role, string? RecaptchaToken);

    // ⬅️ AHORA incluye mustChangePassword y mustCompleteProfile
    public record MeDto(
        int Id,
        string Username,
        string Role,
        bool MustChangePassword,
        bool MustCompleteProfile
    );

    public record ChangePassReq(string CurrentPassword, string NewPassword);

    // =====================================================
    //  REGISTER (SOLO DIRECTOR)
    // =====================================================
    [HttpPost("register")]
    [Authorize(Roles = "Director")]
    public async Task<IActionResult> Register([FromBody] RegisterReq req)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (string.IsNullOrWhiteSpace(req.Username))
            return BadRequest(new { message = "El nombre de usuario es obligatorio." });

        var exists = await _db.Users.AnyAsync(u => u.Username == req.Username);
        if (exists)
            return Conflict(new { message = "El nombre de usuario ya existe." });

        var pwdError = PasswordError(req.Password);
        if (pwdError is not null)
            return BadRequest(new { message = pwdError });

        var allowedRoles = new[] { "Director", "Evaluador", "Candidato" };
        var role = string.IsNullOrWhiteSpace(req.Role) ? "Candidato" : req.Role.Trim();

        if (!allowedRoles.Contains(role))
            return BadRequest(new { message = "Rol inválido." });

        var now = DateTime.UtcNow;

        var user = new User
        {
            Username = req.Username.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = role,
            IsActive = true,
            CreatedAt = now,
            CreatedByUserId = null
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    // =====================================================
    //  LOGIN
    // =====================================================
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginReq req)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = Request.Headers.UserAgent.ToString();

        // reCAPTCHA (obligatorio con tu implementación actual)
        if (string.IsNullOrWhiteSpace(req.RecaptchaToken))
            return BadRequest(new { message = "Debes completar el reCAPTCHA." });

        var captchaOk = await _captcha.VerifyAsync(req.RecaptchaToken, ip);
        if (!captchaOk)
            return BadRequest(new { message = "Verificación reCAPTCHA inválida." });

        // buscar lockout
        var lockout = await _db.LoginLockouts
            .FirstOrDefaultAsync(x => x.Username == req.Username && x.Ip == ip);

        if (lockout?.LockedUntil > DateTime.UtcNow)
        {
            var secs = (int)(lockout.LockedUntil.Value - DateTime.UtcNow).TotalSeconds;
            return Unauthorized(new { message = $"Debes esperar {secs} segundos para reintentar." });
        }

        // buscar usuario
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);
        var validPassword = user != null &&
                            user.IsActive &&
                            BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);

        if (!validPassword)
        {
            // fallos
            if (lockout == null)
            {
                lockout = new LoginLockout
                {
                    Username = req.Username,
                    Ip = ip,
                    FailCount = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.LoginLockouts.Add(lockout);
            }
            else
            {
                lockout.FailCount++;
                lockout.UpdatedAt = DateTime.UtcNow;

                if (lockout.FailCount >= 3 && lockout.LockedUntil == null)
                {
                    lockout.LockedUntil = DateTime.UtcNow.AddSeconds(30);
                }
            }

            await _db.SaveChangesAsync();

            if (lockout.LockedUntil > DateTime.UtcNow)
            {
                var secs = (int)(lockout.LockedUntil.Value - DateTime.UtcNow).TotalSeconds;
                return Unauthorized(new { message = $"Debes esperar {secs} segundos." });
            }

            return Unauthorized(new { message = "Usuario o contraseña inválidos." });
        }

        // reset lockout
        if (lockout != null)
        {
            lockout.FailCount = 0;
            lockout.LockedUntil = null;
            lockout.UpdatedAt = DateTime.UtcNow;
        }

        var now = DateTime.UtcNow;
        var expires = now.Add(InactivityLimit);

        // verificar sesión activa (control multi-sesión)
        var activeSession = await _db.UserSessions
            .Where(s => s.UserId == user.Id && s.IsActive)
            .OrderByDescending(s => s.LastSeenAt)
            .FirstOrDefaultAsync();

        if (activeSession != null)
        {
            var inactiveTime = now - activeSession.LastSeenAt;
            var expiredByTime = activeSession.ExpiresAt <= now;

            if (!expiredByTime && inactiveTime < InactivityLimit)
            {
                return Conflict(new { message = "Ya tienes una sesión activa en otro dispositivo." });
            }

            activeSession.IsActive = false;
        }

        // crear nueva sesión
        var jti = Guid.NewGuid();

        var newSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Jti = jti,
            IsActive = true,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = expires,
            ClientIp = ip,
            UserAgent = ua
        };

        _db.UserSessions.Add(newSession);
        await _db.SaveChangesAsync();

        // token
        var token = BuildJwt(user, jti, now, expires, _cfg);

        Response.Cookies.Append("colabora_token", token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.None,
            Secure = true,
            Path = "/",
            Expires = expires
        });

        // ⬅️ AHORA devolvemos también MustChangePassword / MustCompleteProfile
        return Ok(new MeDto(
            user.Id,
            user.Username,
            user.Role,
            user.MustChangePassword,
            user.MustCompleteProfile
        ));
    }

    // =====================================================
    //  LOGOUT
    // =====================================================
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var token = Request.Cookies["colabora_token"];
        if (string.IsNullOrEmpty(token))
            return Ok(new { ok = true });

        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt;

        try { jwt = handler.ReadJwtToken(token); }
        catch { return Ok(); }

        var sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        var jtiClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

        if (int.TryParse(sub, out var uid) && Guid.TryParse(jtiClaim, out var jti))
        {
            var session = await _db.UserSessions
                .FirstOrDefaultAsync(s => s.UserId == uid && s.Jti == jti && s.IsActive);

            if (session != null)
            {
                session.IsActive = false;
                session.LastSeenAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        Response.Cookies.Append("colabora_token", "", new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.None,
            Secure = true,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(-1)
        });

        return Ok(new { ok = true });
    }

    // =====================================================
    //  ME
    // =====================================================
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var (user, session) = await ValidateSession();
        if (user == null) return Unauthorized();

        return Ok(new MeDto(
            user.Id,
            user.Username,
            user.Role,
            user.MustChangePassword,
            user.MustCompleteProfile
        ));
    }

    // =====================================================
    //  CAMBIO DE CONTRASEÑA
    // =====================================================
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePassReq req)
    {
        var (user, session) = await ValidateSession();
        if (user == null) return Unauthorized();

        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "La contraseña actual es incorrecta." });

        var pwdError = PasswordError(req.NewPassword);
        if (pwdError is not null)
            return BadRequest(new { message = pwdError });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    // =====================================================
    // VALIDAR SESIÓN DESDE COOKIE
    // =====================================================
    private async Task<(User? user, UserSession? session)> ValidateSession()
    {
        var token = Request.Cookies["colabora_token"];
        if (string.IsNullOrEmpty(token)) return (null, null);

        JwtSecurityToken jwt;
        try
        {
            jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        }
        catch
        {
            return (null, null);
        }

        var sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        var jtiClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

        if (!int.TryParse(sub, out var uid) || !Guid.TryParse(jtiClaim, out var jti))
            return (null, null);

        var user = await _db.Users.FindAsync(uid);
        if (user == null || !user.IsActive)
            return (null, null);

        var session = await _db.UserSessions
            .FirstOrDefaultAsync(s => s.UserId == uid && s.Jti == jti && s.IsActive);

        if (session == null) return (null, null);

        var now = DateTime.UtcNow;
        var inactive = now - session.LastSeenAt;

        if (inactive >= InactivityLimit || session.ExpiresAt <= now)
        {
            session.IsActive = false;
            session.LastSeenAt = now;
            await _db.SaveChangesAsync();
            return (null, null);
        }

        session.LastSeenAt = now;
        session.ExpiresAt = now.Add(InactivityLimit);
        await _db.SaveChangesAsync();

        return (user, session);
    }

    // =====================================================
    // JWT HELPER
    // =====================================================
    private static string BuildJwt(User user, Guid jti, DateTime now, DateTime exp, IConfiguration cfg)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, jti.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: cfg["Jwt:Issuer"],
            audience: cfg["Jwt:Audience"],
            claims: claims,
            notBefore: now,
            expires: exp,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // =====================================================
    // Password policy
    // =====================================================
    private static string? PasswordError(string? pwd)
    {
        if (string.IsNullOrWhiteSpace(pwd))
            return "La contraseña es obligatoria.";

        if (pwd.Length < 8)
            return "Debe tener al menos 8 caracteres.";

        if (!pwd.Any(char.IsUpper))
            return "Debe incluir una letra mayúscula.";

        if (!pwd.Any(char.IsLower))
            return "Debe incluir una letra minúscula.";

        if (!pwd.Any(ch => !char.IsLetterOrDigit(ch)))
            return "Debe incluir un carácter especial.";

        return null;
    }
}
