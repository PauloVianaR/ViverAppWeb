namespace ViverApp.Web.Models;

public sealed record AvailabilityInterval(TimeOnly StartsAt, TimeOnly EndsAt, string ModalityCode);
public sealed record AvailabilityDay(DateOnly Date, IReadOnlyList<AvailabilityInterval> Intervals);
public sealed record ProfessionalAvailabilityPlan(string Mode, ulong RowVersion, bool OnlineEnabled,
    IReadOnlyList<AvailabilityDay> Days, IReadOnlyList<AvailabilityDay> ClinicDefaults,
    IReadOnlyList<DateOnly> Holidays, IReadOnlyList<DateOnly> BookedDates, IReadOnlyList<DateOnly> ConflictDates);
public sealed record AvailabilityImpact(int AffectedAppointments, IReadOnlyList<ulong> AppointmentNumbers,
    IReadOnlyList<DateOnly> AffectedDates);
