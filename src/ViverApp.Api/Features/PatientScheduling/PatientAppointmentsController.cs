using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.Identity;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientScheduling;

[ApiController]
[Route("api/v1/patient")]
[Authorize(Policy = ViverAppPolicies.Patient)]
public sealed class PatientAppointmentsController(PatientSchedulingService scheduling) : ControllerBase
{
    [HttpGet("booking/professionals")]
    public Task<ActionResult<SchedulingPage<BookingProfessionalResponse>>> SearchProfessionals(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] uint? specialtyId = null,
        [FromQuery] uint? appointmentTypeId = null,
        [FromQuery] string? modality = null,
        [FromQuery] uint[]? additionalAppointmentTypeIds = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => scheduling.SearchProfessionalsAsync(
            page,
            pageSize,
            search,
            specialtyId,
            appointmentTypeId,
            modality,
            cancellationToken, additionalAppointmentTypeIds));

    [HttpGet("booking/slots")]
    [EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)]
    public Task<ActionResult<IReadOnlyList<AvailableSlotResponse>>> GetAvailableSlots(
        [FromQuery] ulong doctorAccountId,
        [FromQuery] uint appointmentTypeId,
        [FromQuery] string modality,
        [FromQuery] DateOnly from,
        [FromQuery] int days = 14,
        [FromQuery] uint[]? additionalAppointmentTypeIds = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => scheduling.GetAvailableSlotsAsync(
            ActorId,
            doctorAccountId,
            appointmentTypeId,
            modality,
            from,
            days,
            cancellationToken, additionalAppointmentTypeIds));

    [HttpGet("booking/available-dates")]
    [EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)]
    public Task<ActionResult<IReadOnlyList<DateOnly>>> GetAvailableDates(
        [FromQuery] ulong doctorAccountId, [FromQuery] uint appointmentTypeId,
        [FromQuery] string modality, [FromQuery] DateOnly from,
        [FromQuery] int days = 31, [FromQuery] uint[]? additionalAppointmentTypeIds = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => scheduling.GetAvailableDatesAsync(ActorId, doctorAccountId,
            appointmentTypeId, modality, from, days, cancellationToken, additionalAppointmentTypeIds));

    [HttpGet("appointments")]
    public Task<ActionResult<SchedulingPage<AppointmentResponse>>> GetAppointments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string view = "future",
        [FromQuery] string? modality = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => scheduling.GetAppointmentsAsync(
            ActorId,
            page,
            pageSize,
            view,
            modality,
            cancellationToken));

    [HttpGet("appointments/{id:long}")]
    public Task<ActionResult<AppointmentResponse>> GetAppointment(
        ulong id,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => scheduling.GetAppointmentAsync(ActorId, id, cancellationToken));

    [HttpPost("appointments")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<ActionResult<AppointmentResponse>> CreateAppointment(
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromBody] AppointmentCreateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await scheduling.CreateAsync(ActorId, idempotencyKey, request, cancellationToken);
            if (result.Replayed)
            {
                Response.Headers["Idempotent-Replayed"] = "true";
                return Ok(result.Response);
            }

            return CreatedAtAction(nameof(GetAppointment), new { id = result.Response.Id }, result.Response);
        }
        catch (SchedulingRuleException exception)
        {
            return RuleProblem(exception);
        }
    }

    [HttpPost("appointments/{id:long}/cancel")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ActionResult<AppointmentResponse>> CancelAppointment(
        ulong id,
        [FromBody] AppointmentCancelRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => scheduling.CancelAsync(ActorId, id, request, cancellationToken));

    [HttpPost("appointments/{id:long}/reschedule")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<ActionResult<AppointmentResponse>> RescheduleAppointment(
        ulong id,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromBody] AppointmentRescheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await scheduling.RescheduleAsync(ActorId, id, idempotencyKey, request, cancellationToken);
            if (result.Replayed)
            {
                Response.Headers["Idempotent-Replayed"] = "true";
            }

            return Ok(result.Response);
        }
        catch (SchedulingRuleException exception)
        {
            return RuleProblem(exception);
        }
    }

    private ulong ActorId
    {
        get
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new InvalidOperationException("authenticated_account_id_missing");
        }
    }

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (SchedulingRuleException exception)
        {
            return RuleProblem(exception);
        }
    }

    private ObjectResult RuleProblem(SchedulingRuleException exception) =>
        Problem(statusCode: exception.StatusCode, title: exception.Message);
}
