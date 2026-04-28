using System.Security.Claims;
using System.IO;
using Colabora.Api.Data;
using Colabora.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Colabora.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ApplicationDocumentsController : ControllerBase
{
    private readonly ColaboraDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ApplicationDocumentsController(ColaboraDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    // ===== DTOs =====
    public record DocItem(
        int Id,
        int ApplicationId,
        string? Type,
        string? FileName,
        string? MimeType,
        long? SizeBytes,
        string? Status,
        string? Notes,
        DateTime CreatedAt
    );

    public record UpdateDocStatusReq(string Status, string? Notes);

    public class UploadForm
    {
        public int ApplicationId { get; set; }
        public string? Type { get; set; }
        public IFormFile File { get; set; } = default!;
    }

    // ===== Helpers =====
    private int CurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        return int.TryParse(sub, out var uid) ? uid : -1;
    }

    private bool IsDirector => User.IsInRole("Director");
    private bool IsEvaluador => User.IsInRole("Evaluador");
    private bool IsCandidato => User.IsInRole("Candidato");

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".docx", ".xlsx", ".txt" };

    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    // ============================================
    // GET: /api/applicationdocuments/by-application/{applicationId}
    // ============================================
    [HttpGet("by-application/{applicationId:int}")]
    public async Task<ActionResult<IEnumerable<DocItem>>> ListByApplication(
        [FromRoute] int applicationId)
    {
        var app = await _db.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId);

        if (app is null) return NotFound();

        if (IsCandidato && app.CandidateUserId != CurrentUserId())
            return Forbid();

        var docs = await _db.ApplicationDocuments
            .AsNoTracking()
            .Where(d => d.ApplicationId == applicationId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DocItem(
                d.Id,
                d.ApplicationId,
                d.Type,
                d.FileName,
                d.MimeType,
                d.SizeBytes,
                d.Status,
                d.Notes,
                d.CreatedAt
            ))
            .ToListAsync();

        return Ok(docs);
    }

    // ============================================
    // POST: /api/applicationdocuments/upload
    // ============================================
    [HttpPost("upload")]
    [Authorize(Roles = "Candidato,Director")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<DocItem>> Upload([FromForm] UploadForm form)
    {
        var file = form.File;
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Archivo requerido." });

        if (file.Length > MaxBytes)
            return BadRequest(new
            {
                message = $"El archivo excede {MaxBytes / (1024 * 1024)} MB."
            });

        var app = await _db.Applications
            .FirstOrDefaultAsync(a => a.Id == form.ApplicationId);

        if (app is null)
            return NotFound(new { message = "Expediente no encontrado." });

        if (IsCandidato && app.CandidateUserId != CurrentUserId())
            return Forbid();

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return BadRequest(new
            {
                message = $"Extensión no permitida. Usa: {string.Join(", ", AllowedExtensions)}"
            });

        var root = Path.Combine(
            _env.ContentRootPath,
            "Storage",
            "Applications",
            form.ApplicationId.ToString()
        );
        Directory.CreateDirectory(root);

        var fileId = Guid.NewGuid().ToString("N");
        var storedName = $"{fileId}{ext}";
        var physicalPath = Path.Combine(root, storedName);

        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await file.CopyToAsync(stream);
        }

        var now = DateTime.UtcNow;
        var doc = new ApplicationDocument
        {
            ApplicationId = form.ApplicationId,
            Type = string.IsNullOrWhiteSpace(form.Type) ? null : form.Type.Trim(),
            FileName = file.FileName,
            FilePath = physicalPath,
            MimeType = file.ContentType,
            SizeBytes = file.Length,
            Status = "PENDIENTE",
            CreatedAt = now
        };

        _db.ApplicationDocuments.Add(doc);
        await _db.SaveChangesAsync();

        var dto = new DocItem(
            doc.Id,
            doc.ApplicationId,
            doc.Type,
            doc.FileName,
            doc.MimeType,
            doc.SizeBytes,
            doc.Status,
            doc.Notes,
            doc.CreatedAt
        );

        return Ok(dto);
    }

    // ============================================
    // GET: /api/applicationdocuments/{id}/download
    // ============================================
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> GetDownload([FromRoute] int id)
    {
        var doc = await _db.ApplicationDocuments
            .Include(d => d.Application)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (doc is null) return NotFound();

        if (IsCandidato && doc.Application.CandidateUserId != CurrentUserId())
            return Forbid();

        if (string.IsNullOrWhiteSpace(doc.FilePath) ||
            !System.IO.File.Exists(doc.FilePath))
        {
            return NotFound(new { message = "Archivo no disponible." });
        }

        var fs = System.IO.File.OpenRead(doc.FilePath);
        var fileName = doc.FileName ?? Path.GetFileName(doc.FilePath);
        var mime = string.IsNullOrWhiteSpace(doc.MimeType)
            ? "application/octet-stream"
            : doc.MimeType;

        return File(fs, mime, fileName);
    }

    // ============================================
    // PUT: /api/applicationdocuments/{id}/status
    // ============================================
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Evaluador,Director")]
    public async Task<IActionResult> UpdateStatus(
        [FromRoute] int id,
        [FromBody] UpdateDocStatusReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Status))
            return BadRequest(new { message = "Status requerido." });

        var allowed = new[] { "PENDIENTE", "APROBADA", "RECHAZADA" };
        if (!allowed.Contains(req.Status))
            return BadRequest(new
            {
                message = $"Status inválido. Usa: {string.Join(", ", allowed)}"
            });

        var doc = await _db.ApplicationDocuments
            .Include(d => d.Application)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (doc is null) return NotFound();

        doc.Status = req.Status.Trim();
        doc.Notes = string.IsNullOrWhiteSpace(req.Notes)
            ? doc.Notes
            : req.Notes.Trim();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ============================================
    // DELETE: /api/applicationdocuments/{id}
    // ============================================
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var doc = await _db.ApplicationDocuments
            .Include(d => d.Application)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (doc is null) return NotFound();

        if (IsCandidato)
        {
            if (doc.Application.CandidateUserId != CurrentUserId())
                return Forbid();

            if (!string.Equals(doc.Status, "PENDIENTE", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Solo se puede eliminar cuando está PENDIENTE." });
        }
        else if (!(IsDirector || IsEvaluador))
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(doc.FilePath) &&
            System.IO.File.Exists(doc.FilePath))
        {
            try
            {
                System.IO.File.Delete(doc.FilePath);
            }
            catch
            {
                // si falla el delete físico, al menos borramos el registro
            }
        }

        _db.ApplicationDocuments.Remove(doc);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
