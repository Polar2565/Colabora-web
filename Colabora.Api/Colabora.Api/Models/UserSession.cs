using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Colabora.Api.Models
{
    public class UserSession
    {
        /// <summary>
        /// Id de la sesión (PK). Usamos GUID para que sea único globalmente.
        /// </summary>
        [Key]
        public Guid Id { get; set; }

        /// <summary>
        /// Usuario dueño de la sesión (FK -> User.Id).
        /// </summary>
        [Required]
        public int UserId { get; set; }

        /// <summary>
        /// JTI del JWT asociado a esta sesión (claim "jti").
        /// Sirve para validar que el token que trae el front
        /// corresponde exactamente a esta sesión.
        /// </summary>
        [Required]
        public Guid Jti { get; set; }

        /// <summary>
        /// Indica si la sesión sigue activa (no cerrada manualmente / no invalidada).
        /// </summary>
        [Required]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Fecha/hora en que se creó la sesión (UTC).
        /// </summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Última vez que el usuario hizo una petición válida (UTC).
        /// Esto lo iremos actualizando en un middleware para el timeout de inactividad.
        /// </summary>
        [Required]
        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Momento en que debe expirar la sesión (UTC).
        /// Normalmente: CreatedAt + 2 horas de inactividad máxima.
        /// </summary>
        [Required]
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// IP del cliente que creó la sesión (opcional, para auditoría).
        /// </summary>
        [MaxLength(64)]
        public string? ClientIp { get; set; }

        /// <summary>
        /// User-Agent del navegador / cliente (opcional, para auditoría).
        /// </summary>
        [MaxLength(256)]
        public string? UserAgent { get; set; }

        // =============================
        //   NAVEGACIÓN
        // =============================

        [ForeignKey(nameof(UserId))]
        public User User { get; set; } = null!;
    }
}
