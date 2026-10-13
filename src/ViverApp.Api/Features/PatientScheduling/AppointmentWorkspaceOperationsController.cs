using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientScheduling;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OperationalReviewWrite([param: Range(1, 5)] byte Rating,
    [param: StringLength(2000)] string? Feedback, ulong RowVersion);
public sealed record OperationalReviewVersionResponse(ulong Version, byte Rating, string? Feedback,
    string EditorName, string EditorRole, DateTime CreatedAtUtc);
public sealed record OperationalReviewResponse(byte? Rating, string? Feedback, ulong RowVersion,
    IReadOnlyList<OperationalReviewVersionResponse> Versions);
public sealed record WorkspaceAttachment(ulong Id, string Name, ulong SizeBytes, ulong RowVersion, bool CanDelete);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShortenAppointmentRequest([param: Range(5, 720)] int DurationMinutes,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

[ApiController, Route("api/v1/booking-workspace/appointments/{id:long}")]
[Authorize(Roles = "administrator,manager,doctor,psychologist")]
[ServiceFilter(typeof(AdministratorExceptionFilter)), ServiceFilter(typeof(AdministratorStepUpFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AppointmentWorkspaceOperationsController(ViverAppDbContext database,
    PrivateDocumentStore documents, IdentityAuditWriter audit, TimeProvider clock) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    private bool Professional => User.IsInRole("doctor") || User.IsInRole("psychologist");
    private async Task<Appointment> RequireAppointment(ulong id, CancellationToken ct) =>
        await database.Appointments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id
            && (!Professional || x.ProfessionalAccountId == Actor), ct)
        ?? throw new AdministratorRuleException(404, "Atendimento não encontrado.");

    [HttpPut("duration"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> ShortenDuration(ulong id, ShortenAppointmentRequest request, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var appointment = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (appointment is null || (Professional && appointment.ProfessionalAccountId != Actor))
            throw new AdministratorRuleException(404, "Atendimento não encontrado.");
        if (appointment.RowVersion != request.RowVersion)
            throw new AdministratorRuleException(409, "O atendimento foi alterado. Atualize antes de salvar a duração.");
        var previousMinutes = (appointment.EndsAtUtc - appointment.StartsAtUtc).TotalMinutes;
        if (appointment.StatusCode != "pending")
            throw new AdministratorRuleException(409, "Só é possível reduzir a duração enquanto o atendimento aguarda pagamento.");
        if (request.DurationMinutes < 5 || request.DurationMinutes >= previousMinutes)
            throw new AdministratorRuleException(400, "Informe uma duração de pelo menos 5 minutos, menor que a duração atual.");
        // Reducing the existing interval cannot introduce an availability conflict.
        // Keep catalog/service snapshots and every financial value unchanged.
        var endsAt = appointment.StartsAtUtc.AddMinutes(request.DurationMinutes);
        var updated = await database.Appointments.Where(a => a.Id == id && a.RowVersion == request.RowVersion && a.StatusCode == "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.EndsAtUtc, endsAt)
                .SetProperty(a => a.UpdatedAtUtc, clock.GetUtcNow().UtcDateTime)
                .SetProperty(a => a.RowVersion, a => a.RowVersion + 1), ct);
        if (updated != 1) throw new AdministratorRuleException(409, "O atendimento foi alterado. Atualize antes de salvar a duração.");
        await audit.WriteAsync("appointment.duration.shortened", Actor, "appointment", id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string>
            {
                ["previousMinutes"] = previousMinutes.ToString(CultureInfo.InvariantCulture),
                ["durationMinutes"] = request.DurationMinutes.ToString(CultureInfo.InvariantCulture),
            }, ct);
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("operational-review")]
    public async Task<OperationalReviewResponse> Review(ulong id, CancellationToken ct)
    {
        var appointment = await RequireAppointment(id, ct);
        if (appointment.StatusCode != "completed")
            throw new AdministratorRuleException(409, "A avaliação operacional está disponível após a conclusão.");
        var review = await database.AppointmentOperationalReviews.AsNoTracking().SingleOrDefaultAsync(x => x.AppointmentId == id, ct);
        var versions = await database.AppointmentOperationalReviewVersions.AsNoTracking().Where(x => x.AppointmentId == id)
            .OrderByDescending(x => x.VersionNumber).Select(x => new OperationalReviewVersionResponse(x.VersionNumber,
                x.Rating, x.Feedback, x.EditorAccount != null ? x.EditorAccount.FullName : "Autor preservado na auditoria",
                x.EditorRoleCode, x.CreatedAtUtc)).ToArrayAsync(ct);
        return new(review?.Rating, review?.Feedback, review?.RowVersion ?? 0, versions);
    }

    [HttpPut("operational-review"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<OperationalReviewResponse> SaveReview(ulong id, OperationalReviewWrite request, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var appointment = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (appointment is null || (Professional && appointment.ProfessionalAccountId != Actor))
            throw new AdministratorRuleException(404, "Atendimento não encontrado.");
        if (appointment.StatusCode != "completed")
            throw new AdministratorRuleException(409, "Conclua o atendimento antes de registrar uma avaliação operacional.");
        var review = await database.AppointmentOperationalReviews.SingleOrDefaultAsync(x => x.AppointmentId == id, ct);
        if ((review?.RowVersion ?? 0) != request.RowVersion)
            throw new AdministratorRuleException(409, "A avaliação foi alterada por outra sessão. Atualize antes de salvar.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (review is null)
        {
            review = new AppointmentOperationalReview { AppointmentId = id };
            database.AppointmentOperationalReviews.Add(review);
        }
        review.Rating = request.Rating;
        review.Feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim();
        review.UpdatedByAccountId = Actor;
        review.UpdatedAtUtc = now;
        review.RowVersion++;
        database.AppointmentOperationalReviewVersions.Add(new AppointmentOperationalReviewVersion
        {
            AppointmentId = id,
            VersionNumber = review.RowVersion,
            Rating = review.Rating,
            Feedback = review.Feedback,
            EditorAccountId = Actor,
            EditorRoleCode = User.FindFirstValue(ClaimTypes.Role)!,
            CreatedAtUtc = now,
        });
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("appointment.operational_review.saved", Actor, "appointment", id.ToString(), null, ct);
        await transaction.CommitAsync(ct);
        return await Review(id, ct);
    }

    [HttpGet("documents")]
    public async Task<IReadOnlyList<WorkspaceAttachment>> Attachments(ulong id, CancellationToken ct)
    {
        await RequireAppointment(id, ct);
        return await database.AppointmentDocuments.AsNoTracking().Where(x => x.AppointmentId == id && x.StatusCode == "available")
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => new WorkspaceAttachment(x.Id, x.OriginalFileName,
                x.SizeBytes, x.RowVersion, x.UploadedByAccountId == Actor)).ToArrayAsync(ct);
    }

    [HttpPost("documents"), EnableRateLimiting(SecurityPolicyNames.UploadRateLimit)]
    [RequestSizeLimit(10_600_000), RequestFormLimits(MultipartBodyLengthLimit = 10_600_000)]
    public async Task<WorkspaceAttachment> Upload(ulong id, IFormFile file, CancellationToken ct)
    {
        var appointment = await RequireAppointment(id, ct);
        if (appointment.StatusCode is not ("confirmed" or "arrived" or "in_progress" or "completed"))
            throw new AdministratorRuleException(409, "Anexos exigem um atendimento confirmado, em atendimento ou concluído.");
        var document = await documents.PrepareClinicalAsync(appointment.PatientAccountId, file, ct);
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var locked = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {id} FOR UPDATE").AsNoTracking().SingleAsync(ct);
        if ((Professional && locked.ProfessionalAccountId != Actor) || locked.StatusCode is not ("confirmed" or "arrived" or "in_progress" or "completed"))
            throw new AdministratorRuleException(409, "O atendimento foi alterado durante o envio. Atualize antes de anexar.");
        database.PrivateDocuments.Add(document);
        var link = new AppointmentDocument
        {
            AppointmentId = id,
            UploadedByAccountId = Actor,
            CategoryCode = "attachment",
            ObjectKey = document.Id.ToString("D"),
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            SizeBytes = document.SizeBytes,
            Sha256 = document.Sha256,
            StatusCode = "available",
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            AvailableAtUtc = clock.GetUtcNow().UtcDateTime,
            RowVersion = 1,
        };
        database.AppointmentDocuments.Add(link);
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("appointment.document.uploaded", Actor, "appointment_document", link.Id.ToString(), null, ct);
        await transaction.CommitAsync(ct);
        return new(link.Id, link.OriginalFileName, link.SizeBytes, link.RowVersion, true);
    }

    [HttpGet("documents/{documentId:long}")]
    public async Task<IActionResult> Download(ulong id, ulong documentId, CancellationToken ct)
    {
        await RequireAppointment(id, ct);
        var link = await database.AppointmentDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId
            && x.AppointmentId == id && x.StatusCode == "available", ct);
        if (link is null || !Guid.TryParse(link.ObjectKey, out var key))
            throw new AdministratorRuleException(404, "Anexo indisponível.");
        var result = await documents.DownloadAuthorizedClinicalAsync(key, ct);
        await audit.WriteAsync("appointment.document.downloaded", Actor, "appointment_document", documentId.ToString(), null, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Content, result.Mime, result.Name, enableRangeProcessing: false);
    }

    [HttpDelete("documents/{documentId:long}"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Delete(ulong id, ulong documentId, ulong rowVersion, CancellationToken ct)
    {
        await RequireAppointment(id, ct);
        var link = await database.AppointmentDocuments.SingleOrDefaultAsync(x => x.Id == documentId
            && x.AppointmentId == id && x.StatusCode == "available", ct)
            ?? throw new AdministratorRuleException(404, "Anexo indisponível.");
        if (link.UploadedByAccountId != Actor) throw new AdministratorRuleException(403, "Somente o autor pode remover o anexo.");
        if (link.RowVersion != rowVersion) throw new AdministratorRuleException(409, "O anexo foi alterado. Atualize antes de remover.");
        link.StatusCode = "deleted"; link.DeletedByAccountId = Actor; link.DeletedAtUtc = clock.GetUtcNow().UtcDateTime; link.RowVersion++;
        if (Guid.TryParse(link.ObjectKey, out var key))
        {
            var document = await database.PrivateDocuments.SingleOrDefaultAsync(x => x.Id == key, ct);
            if (document is not null) document.StatusCode = "deleted";
        }
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("appointment.document.deleted", Actor, "appointment_document", documentId.ToString(), null, ct);
        return NoContent();
    }
}
