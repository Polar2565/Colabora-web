using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
public class ApplicationsController : ControllerBase
{
    private readonly ColaboraDbContext _db;
    public ApplicationsController(ColaboraDbContext db) => _db = db;

    // ====== DTOs ======
    public record ApplicationListItem(
        int Id,
        string? Folio,
        string? Status,
        int CandidateUserId,
        DateTime CreatedAt,
        DateTime UpdatedAt
    );

    public record ApplicationDetail(
        int Id,
        string? Folio,
        string? Status,
        int CandidateUserId,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        List<AppDocItem> Documents,
        List<AppCommentItem> Comments
    );

    public record AppDocItem(
        int Id,
        string? Type,
        string? FileName,
        string? MimeType,
        long? SizeBytes,
        string? Status,
        string? Notes,
        DateTime CreatedAt
    );

    public record AppCommentItem(
        int Id,
        int? AuthorUserId,
        string Text,
        DateTime CreatedAt
    );

    public record CreateApplicationReq(string? Folio);
    public record UpdateStatusReq(string Status, string? Comment);

    // ====== Helpers ======
    private int CurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(sub, out var uid) ? uid : -1;
    }

    private bool IsDirector => User.IsInRole("Director");
    private bool IsEvaluador => User.IsInRole("Evaluador");
    private bool IsCandidato => User.IsInRole("Candidato");

    // ====== GET: /api/applications?status=&q=&mine=true&page=1&pageSize=20 ======
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationListItem>>> GetList(
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] bool? mine,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Applications.AsNoTracking().AsQueryable();

        // Filtro por rol
        if (IsCandidato && !IsDirector && !IsEvaluador)
        {
            var uid = CurrentUserId();
            query = query.Where(a => a.CandidateUserId == uid);
        }
        else
        {
            // mine=true para que evaluador/director vean solo propios si lo requieren
            if (mine == true)
            {
                var uid = CurrentUserId();
                query = query.Where(a => a.CandidateUserId == uid);
            }
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(a =>
                (a.Folio != null && a.Folio.Contains(term)) ||
                a.Id.ToString() == term);
        }

        var items = await query
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new ApplicationListItem(
                a.Id, a.Folio, a.Status, a.CandidateUserId, a.CreatedAt, a.UpdatedAt))
            .ToListAsync();

        return Ok(items);
    }

    // ====== GET: /api/applications/{id} ======
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApplicationDetail>> GetById([FromRoute] int id)
    {
        var app = await _db.Applications
            .AsNoTracking()
            .Include(a => a.Documents)
            .Include(a => a.Comments)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (app is null) return NotFound();

        // Autorización por rol
        if (IsCandidato && app.CandidateUserId != CurrentUserId())
            return Forbid();

        var dto = new ApplicationDetail(
            app.Id,
            app.Folio,
            app.Status,
            app.CandidateUserId,
            app.CreatedAt,
            app.UpdatedAt,
            app.Documents
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new AppDocItem(d.Id, d.Type, d.FileName, d.MimeType, d.SizeBytes, d.Status, d.Notes, d.CreatedAt))
                .ToList(),
            app.Comments
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new AppCommentItem(c.Id, c.AuthorUserId, c.Text, c.CreatedAt))
                .ToList()
        );

        return Ok(dto);
    }

    // ====== POST: /api/applications  ======
    [HttpPost]
    [Authorize(Roles = "Candidato,Director")]
    public async Task<ActionResult<ApplicationListItem>> Create([FromBody] CreateApplicationReq req)
    {
        int candidateId = CurrentUserId();
        if (IsDirector && Request.Query.ContainsKey("candidateId"))
        {
            if (int.TryParse(Request.Query["candidateId"], out var tmp)) candidateId = tmp;
        }

        var now = DateTime.UtcNow;

        var app = new Application
        {
            CandidateUserId = candidateId,
            Folio = string.IsNullOrWhiteSpace(req.Folio) ? null : req.Folio!.Trim(),
            Status = "PENDIENTE",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Applications.Add(app);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId(),
            Action = "APP_CREATE",
            CreatedAt = now,
            Payload = $"{{\"applicationId\":{app.Id}}}"
        });

        await _db.SaveChangesAsync();

        var dto = new ApplicationListItem(app.Id, app.Folio, app.Status, app.CandidateUserId, app.CreatedAt, app.UpdatedAt);
        return CreatedAtAction(nameof(GetById), new { id = app.Id }, dto);
    }

    // ====== PUT: /api/applications/{id}/status  ======
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Evaluador,Director")]
    public async Task<IActionResult> UpdateStatus([FromRoute] int id, [FromBody] UpdateStatusReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Status))
            return BadRequest(new { message = "Status requerido." });

        var allowed = new[] { "PENDIENTE", "EN_REVISION", "APROBADO", "RECHAZADO", "CERRADO" };
        if (!allowed.Contains(req.Status))
            return BadRequest(new { message = $"Status inválido. Usa: {string.Join(", ", allowed)}" });

        var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == id);
        if (app is null) return NotFound();

        app.Status = req.Status.Trim();
        app.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(req.Comment))
        {
            _db.ApplicationComments.Add(new ApplicationComment
            {
                ApplicationId = app.Id,
                AuthorUserId = CurrentUserId(),
                Text = req.Comment!.Trim(),
                CreatedAt = DateTime.UtcNow
            });
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId(),
            Action = "APP_STATUS_UPDATE",
            CreatedAt = DateTime.UtcNow,
            Payload = $"{{\"applicationId\":{app.Id},\"status\":\"{app.Status}\"}}"
        });

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ====== GET: /api/applications/mine  ======
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<ApplicationListItem>>> GetMine([FromQuery] int? candidateId)
    {
        var uid = CurrentUserId();

        int ownerId = uid;
        if ((IsDirector || IsEvaluador) && candidateId.HasValue)
            ownerId = candidateId.Value;

        var items = await _db.Applications.AsNoTracking()
            .Where(a => a.CandidateUserId == ownerId)
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new ApplicationListItem(a.Id, a.Folio, a.Status, a.CandidateUserId, a.CreatedAt, a.UpdatedAt))
            .ToListAsync();

        return Ok(items);
    }

    // ====== GET: /api/applications/stats  ======
    [HttpGet("stats")]
    public async Task<ActionResult<object>> GetStats([FromQuery] int? candidateId)
    {
        var uid = CurrentUserId();

        var query = _db.Applications.AsNoTracking().AsQueryable();

        if (IsCandidato && !IsDirector && !IsEvaluador)
            query = query.Where(a => a.CandidateUserId == uid);
        else if ((IsDirector || IsEvaluador) && candidateId.HasValue)
            query = query.Where(a => a.CandidateUserId == candidateId.Value);

        var totals = await query
            .GroupBy(a => a.Status ?? "SIN_STATUS")
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var dict = totals.ToDictionary(x => x.Status, x => x.Count, StringComparer.OrdinalIgnoreCase);
        string[] expected = { "PENDIENTE", "EN_REVISION", "APROBADO", "RECHAZADO", "CERRADO", "SIN_STATUS" };
        var result = expected.ToDictionary(k => k, k => dict.ContainsKey(k) ? dict[k] : 0, StringComparer.OrdinalIgnoreCase);

        result["TOTAL"] = result.Values.Sum();
        return Ok(result);
    }
}
