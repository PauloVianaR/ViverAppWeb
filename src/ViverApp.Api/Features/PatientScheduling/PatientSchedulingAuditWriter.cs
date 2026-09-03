using ViverApp.Api.Features.Identity;

namespace ViverApp.Api.Features.PatientScheduling;

public interface IPatientSchedulingAuditWriter
{
    Task WriteAsync(
        string eventCode,
        ulong actorAccountId,
        ulong appointmentId,
        IReadOnlyDictionary<string, string>? safeData,
        CancellationToken cancellationToken);
}

internal sealed class PatientSchedulingAuditWriter(IdentityAuditWriter auditWriter) : IPatientSchedulingAuditWriter
{
    public Task WriteAsync(
        string eventCode,
        ulong actorAccountId,
        ulong appointmentId,
        IReadOnlyDictionary<string, string>? safeData,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            eventCode,
            actorAccountId,
            "appointment",
            appointmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            safeData,
            cancellationToken);
}
