using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Colabora.Api.Models
{
    [Table("ApplicationComments")]
    public class ApplicationComment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ApplicationId { get; set; }

        public int? AuthorUserId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Text { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        // Navegaciones
        public Application Application { get; set; } = null!;
        public User? AuthorUser { get; set; }
    }
}
