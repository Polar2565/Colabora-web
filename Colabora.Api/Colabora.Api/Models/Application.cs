using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Colabora.Api.Models
{
    [Table("Applications")]
    public class Application
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CandidateUserId { get; set; }

        [MaxLength(30)]
        public string? Status { get; set; } // PENDIENTE | EN_REVISION | APROBADO | RECHAZADO | CERRADO

        [MaxLength(50)]
        public string? Folio { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navegación
        public List<ApplicationDocument> Documents { get; set; } = new();
        public List<ApplicationComment> Comments { get; set; } = new();
    }
}
