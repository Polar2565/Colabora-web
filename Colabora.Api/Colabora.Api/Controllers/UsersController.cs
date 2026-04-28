using System.Security.Claims;
using Colabora.Api.Data;
using Colabora.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Colabora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Director")]  // Solo Director puede acceder
public class UsersController : ControllerBase
{
    private readonly ColaboraDbContext _db;

    public UsersController(ColaboraDbContext db)
    {
        _db = db;
    }

    // =====================================
    // DTOs utilizados por Angular
    // =====================================

    public record CreateUserRequest(
        string Username,
        string FirstName,
        string LastName,
        string Email,
        string Role
    );

    public record UserListDto(
        int Id,
        string Username,
        string Role,
        string FirstName,
        string LastName,
        string Email,
        bool IsActive
    );

    public record UpdateUserRequest(
        string FirstName,
        string LastName,
        string Email,
        string Role,
        bool IsActive
    );

    // =====================================
    // GET: /api/users
    // Lista completa
    // =====================================
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserListDto>>> GetUsers()
    {
        try
        {
            var users = await _db.Users
                .OrderBy(u => u.Role)
                .ThenBy(u => u.Username)
                .Select(u => new UserListDto(
                    u.Id,
                    u.Username,
                    u.Role,
                    u.FirstName ?? string.Empty,
                    u.LastName ?? string.Empty,
                    u.Email ?? string.Empty,
                    u.IsActive
                ))
                .ToListAsync();

            return Ok(users);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Error obteniendo usuarios.",
                detail = ex.InnerException?.Message ?? ex.Message
            });
        }
    }

    // =====================================
    // POST: /api/users
    // Crear usuario
    // =====================================
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) ||
            string.IsNullOrWhiteSpace(dto.Role))
        {
            return BadRequest(new { message = "Usuario y rol son obligatorios." });
        }

        var username = dto.Username.Trim();

        // Duplicado exacto
        if (await _db.Users.AnyAsync(u => u.Username == username))
        {
            return Conflict(new { message = "El nombre de usuario ya existe." });
        }

        var rolesValidos = new[] { "Director", "Evaluador", "Candidato" };
        if (!rolesValidos.Contains(dto.Role))
        {
            return BadRequest(new { message = "Rol inválido." });
        }

        // Contraseña temporal
        string tempPassword = "Colabora#Temp1";
        string hash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        // Obtener ID del Director que crea
        int? directorId = null;
        string? claimId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(claimId, out int parsed))
            directorId = parsed;

        var now = DateTime.UtcNow;

        var user = new User
        {
            Username = username,
            PasswordHash = hash,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Role = dto.Role,
            IsActive = true,
            MustChangePassword = true,
            MustCompleteProfile = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = directorId
        };

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new
            {
                message = "Error al guardar el usuario en la base de datos.",
                dbError = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Error inesperado creando el usuario.",
                detail = ex.InnerException?.Message ?? ex.Message
            });
        }

        return Ok(new
        {
            message = "Usuario creado correctamente.",
            tempPassword
        });
    }

    // =====================================
    // PUT: /api/users/{id}
    // Editar usuario
    // =====================================
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest dto)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null)
            return NotFound(new { message = "Usuario no encontrado." });

        var rolesValidos = new[] { "Director", "Evaluador", "Candidato" };
        if (!rolesValidos.Contains(dto.Role))
            return BadRequest(new { message = "Rol inválido." });

        u.FirstName = dto.FirstName;
        u.LastName = dto.LastName;
        u.Email = dto.Email;
        u.Role = dto.Role;
        u.IsActive = dto.IsActive;
        u.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new
            {
                message = "Error al actualizar el usuario en la base de datos.",
                dbError = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Error inesperado actualizando el usuario.",
                detail = ex.InnerException?.Message ?? ex.Message
            });
        }

        return Ok(new { message = "Usuario actualizado correctamente." });
    }

    // =====================================
    // PUT: /api/users/{id}/toggle
    // Activar / Desactivar usuario
    // =====================================
    [HttpPut("{id}/toggle")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (user == null)
            return NotFound(new { message = "Usuario no encontrado." });

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new
            {
                message = "Error al cambiar el estado del usuario en la base de datos.",
                dbError = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Error inesperado cambiando el estado del usuario.",
                detail = ex.InnerException?.Message ?? ex.Message
            });
        }

        return Ok(new
        {
            message = user.IsActive ? "Usuario activado" : "Usuario desactivado",
            isActive = user.IsActive
        });
    }
}
