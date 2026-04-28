using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Colabora.Api.Models
{
    [Table("ApplicationDocuments")]
    public class ApplicationDocument
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ApplicationId { get; set; }

        [MaxLength(50)]
        public string? Type { get; set; }

        [MaxLength(260)]
        public string? FileName { get; set; }

        [MaxLength(260)]
        public string? FilePath { get; set; }

        [MaxLength(120)]
        public string? MimeType { get; set; }

        public long? SizeBytes { get; set; }

        [MaxLength(20)]
        public string? Status { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navegación
        public Application Application { get; set; } = default!;
    }
}
