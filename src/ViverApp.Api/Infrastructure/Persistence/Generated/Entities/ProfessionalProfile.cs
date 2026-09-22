using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class ProfessionalProfile
{
    public ulong AccountId { get; set; }

    public string LicenseTypeCode { get; set; } = null!;

    public string ProfessionalTitle { get; set; } = null!;

    public string LicenseStateCode { get; set; } = null!;

    public string LicenseNumber { get; set; } = null!;

    public string? Biography { get; set; }

    public ushort YearsExperience { get; set; }

    public ushort DefaultAppointmentDurationMinutes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ulong RowVersion { get; set; }

    public virtual Account Account { get; set; } = null!;

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<MedicalReportVersion> MedicalReportVersions { get; set; } = new List<MedicalReportVersion>();

    public virtual ICollection<MedicalReport> MedicalReports { get; set; } = new List<MedicalReport>();

    public virtual ICollection<ProfessionalAvailabilityException> ProfessionalAvailabilityExceptions { get; set; } = new List<ProfessionalAvailabilityException>();

    public virtual ICollection<ProfessionalNotification> ProfessionalNotifications { get; set; } = new List<ProfessionalNotification>();

    public virtual ICollection<ProfessionalPatientLink> ProfessionalPatientLinks { get; set; } = new List<ProfessionalPatientLink>();

    public virtual ProfessionalPreference? ProfessionalPreference { get; set; }

    public virtual ICollection<ProfessionalService> ProfessionalServices { get; set; } = new List<ProfessionalService>();

    public virtual ICollection<ProfessionalSpecialty> ProfessionalSpecialties { get; set; } = new List<ProfessionalSpecialty>();

    public virtual ICollection<ProfessionalVariableHour> ProfessionalVariableHours { get; set; } = new List<ProfessionalVariableHour>();

    public virtual ICollection<ProfessionalWeeklyHour> ProfessionalWeeklyHours { get; set; } = new List<ProfessionalWeeklyHour>();
}
