using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.PatientExperience;

// O banco é a fonte de verdade de capacidade e presença entre instâncias da API.
public sealed class TeleconsultationPresence(ViverAppDbContext database, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<string[]> JoinAsync(ulong appointmentId, ulong? accountId, Guid? guestId, string connectionId, CancellationToken ct)
    {
        if ((accountId is null) == (guestId is null)) throw new ArgumentException("Identidade inválida.");
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id={appointmentId} FOR UPDATE")
            .AsNoTracking().ToListAsync(ct);
        await database.TeleconsultationPeers.Where(x => x.AppointmentId == appointmentId && x.ExpiresAtUtc <= Now)
            .ExecuteDeleteAsync(ct);
        if (guestId is not null && !await database.TeleconsultationPeers.AnyAsync(x => x.AppointmentId == appointmentId
            && x.AccountId == x.Appointment.ProfessionalAccountId && x.ExpiresAtUtc > Now, ct))
            throw new HubException("Aguarde o profissional abrir a reunião.");
        if (accountId is not null)
            await database.TeleconsultationPeers.Where(x => x.AppointmentId == appointmentId && x.AccountId == accountId)
                .ExecuteDeleteAsync(ct);
        else
        {
            var bytes = guestId!.Value.ToByteArray();
            await database.TeleconsultationPeers.Where(x => x.AppointmentId == appointmentId && x.GuestId != null && x.GuestId.SequenceEqual(bytes))
                .ExecuteDeleteAsync(ct);
        }
        var existing = await database.TeleconsultationPeers.AsNoTracking().Where(x => x.AppointmentId == appointmentId)
            .Select(x => x.ConnectionId).ToArrayAsync(ct);
        if (existing.Length >= 4) throw new HubException("A sala atingiu o limite de quatro participantes.");
        database.TeleconsultationPeers.Add(new TeleconsultationPeer
        {
            AppointmentId = appointmentId, AccountId = accountId, GuestId = guestId?.ToByteArray(),
            ConnectionId = connectionId, ExpiresAtUtc = Now.AddSeconds(45)
        });
        await database.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return existing;
    }

    public Task<int> HeartbeatAsync(ulong appointmentId, string connectionId, CancellationToken ct) =>
        database.TeleconsultationPeers.Where(x => x.AppointmentId == appointmentId && x.ConnectionId == connectionId && x.ExpiresAtUtc > Now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAtUtc, Now.AddSeconds(45)), ct);

    public Task<bool> ContainsAsync(ulong appointmentId, string connectionId, CancellationToken ct) =>
        database.TeleconsultationPeers.AnyAsync(x => x.AppointmentId == appointmentId && x.ConnectionId == connectionId
            && x.ExpiresAtUtc > Now, ct);

    public async Task<string[]> LeaveAsync(ulong appointmentId, string connectionId, CancellationToken ct)
    {
        await database.TeleconsultationPeers.Where(x => x.AppointmentId == appointmentId && x.ConnectionId == connectionId)
            .ExecuteDeleteAsync(ct);
        return await database.TeleconsultationPeers.AsNoTracking().Where(x => x.AppointmentId == appointmentId && x.ExpiresAtUtc > Now)
            .Select(x => x.ConnectionId).ToArrayAsync(ct);
    }
}
