using System;

namespace Colabora.Api.Models
{
    /// <summary>
    /// Usuario del sistema Colabora Web.
    /// Roles permitidos: Director, Evaluador, Candidato.
    /// </summary>
    public class User
    {
        public int Id { get; set; }

        /// <summary>
        /// Nombre de usuario para login (único).
        /// </summary>
        public string Username { get; set; } = null!;

        /// <summary>
        /// Hash de la contraseña (BCrypt).
        /// </summary>
        public string PasswordHash { get; set; } = null!;

        /// <summary>
        /// Rol del usuario: Director, Evaluador o Candidato.
        /// </summary>
        public string Role { get; set; } = null!;

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Dept { get; set; }

        public string? Position { get; set; }

        /// <summary>
        /// Indica si la cuenta está activa.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Obliga al usuario a cambiar su contraseña al siguiente inicio de sesión.
        /// </summary>
        public bool MustChangePassword { get; set; } = true;

        /// <summary>
        /// Obliga al usuario a completar su perfil (datos faltantes).
        /// </summary>
        public bool MustCompleteProfile { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Id del Director que creó este usuario (opcional).
        /// </summary>
        public int? CreatedByUserId { get; set; }

        public DateTime? LastLoginAt { get; set; }
    }
}
