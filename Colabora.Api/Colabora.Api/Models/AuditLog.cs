using System;

namespace Colabora.Api.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        // FK directa al usuario, pero SIN navegación
        public int? UserId { get; set; }

        public string Action { get; set; } = null!;
        public string? Payload { get; set; }
        public string? Ip { get; set; }
        public string? UserAgent { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
