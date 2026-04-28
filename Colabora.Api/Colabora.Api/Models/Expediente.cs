using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Colabora.Api.Data;

/// Expediente del Candidato (carpeta principal)
public class Expediente
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string Folio { get; set; } = default!; // identificador legible

    [Required]
    public int CandidateUserId { get; set; }      // FK a Users.Id (Candidato)

    [Required, MaxLength(20)]
    public string Status { get; set; } = "PENDIENTE"; // PENDIENTE | EN_REVISION | APROBADO | RECHAZADO | CERRADO

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navegación
    public List<Evidencia> Evidencias { get; set; } = new();
}

/// Evidencia específica dentro del Expediente
public class Evidencia
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ExpedienteId { get; set; }         // FK a Expediente.Id

    [Required, MaxLength(50)]
    public string Tipo { get; set; } = default!;  // acta, identificación, constancia, etc.

    [MaxLength(500)]
    public string? Descripcion { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "PENDIENTE"; // PENDIENTE | APROBADA | RECHAZADA

    [MaxLength(500)]
    public string? Observaciones { get; set; }    // comentarios del evaluador al aprobar/rechazar

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navegación
    public Expediente Expediente { get; set; } = default!;
    public List<EvidenciaArchivo> Archivos { get; set; } = new();
}

/// Archivo físico asociado a una Evidencia
public class EvidenciaArchivo
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int EvidenciaId { get; set; }          // FK a Evidencia.Id

    [Required, MaxLength(260)]
    public string Nombre { get; set; } = default!;  // nombre visible

    [Required, MaxLength(260)]
    public string Ruta { get; set; } = default!;    // ruta/clave interna (no pública)

    [MaxLength(120)]
    public string? MimeType { get; set; }

    public long? Bytes { get; set; }

    [MaxLength(128)]
    public string? Hash { get; set; }               // opcional: integridad

    public DateTime CreatedAt { get; set; }

    // Navegación
    public Evidencia Evidencia { get; set; } = default!;
}
