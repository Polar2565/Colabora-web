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
public class ApplicationCommentsController : ControllerBase
{
    private readonly ColaboraDbContext _db;
    public ApplicationCommentsController(ColaboraDbContext db) => _db = db;

    // DTOs
    public record CommentItem(int Id, int ApplicationId, int? AuthorUserId, string Text, DateTime CreatedAt);
    public record CreateCommentReq(int ApplicationId, string Text);

    // Helpers
    private int CurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(sub, out var uid) ? uid : -1;
    }
    private bool IsDirector => User.IsInRole("Director");
    private bool IsEvaluador => User.IsInRole("Evaluador");
    private bool IsCandidato => User.IsInRole("Candidato");

    // GET: /api/applicationcomments/by-application/123?page=1&pageSize=20
    [HttpGet("by-application/{applicationId:int}")]
    public async Task<ActionResult<IEnumerable<CommentItem>>> List(
        [FromRoute] int applicationId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var app = await _db.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId);
        if (app is null) return NotFound();

        // Candidato solo ve los suyos
        if (IsCandidato && app.CandidateUserId != CurrentUserId())
            return Forbid();

        var items = await _db.ApplicationComments
            .AsNoTracking()
            .Where(c => c.ApplicationId == applicationId)
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CommentItem(c.Id, c.ApplicationId, c.AuthorUserId, c.Text, c.CreatedAt))
            .ToListAsync();

        return Ok(items);
    }

    // POST: /api/applicationcomments
    [HttpPost]
    public async Task<ActionResult<CommentItem>> Create([FromBody] CreateCommentReq req)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { message = "El comentario no puede estar vacío." });

        var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == req.ApplicationId);
        if (app is null) return NotFound(new { message = "Expediente no encontrado." });

        // Candidato solo comenta en su propio expediente
        if (IsCandidato && app.CandidateUserId != CurrentUserId())
            return Forbid();

        var now = DateTime.UtcNow;
        var comment = new ApplicationComment
        {
            ApplicationId = req.ApplicationId,
            AuthorUserId = CurrentUserId(),
            Text = req.Text.Trim(),
            CreatedAt = now
        };

        _db.ApplicationComments.Add(comment);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId(),
            Action = "APP_COMMENT_CREATE",
            CreatedAt = now,
            Payload = $"{{\"applicationId\":{req.ApplicationId}}}"
        });

        await _db.SaveChangesAsync();

        var dto = new CommentItem(comment.Id, comment.ApplicationId, comment.AuthorUserId, comment.Text, comment.CreatedAt);
        return Ok(dto);
    }

    // DELETE: /api/applicationcomments/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var c = await _db.ApplicationComments
            .Include(x => x.Application)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (c is null) return NotFound();

        var uid = CurrentUserId();
        var isAuthor = c.AuthorUserId == uid;

        if (!(IsDirector || isAuthor))
            return Forbid();

        _db.ApplicationComments.Remove(c);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = uid,
            Action = "APP_COMMENT_DELETE",
            CreatedAt = DateTime.UtcNow,
            Payload = $"{{\"commentId\":{id}}}"
        });

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
