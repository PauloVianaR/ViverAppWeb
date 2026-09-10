using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class DoctorProfile
{
    public ulong AccountId { get; set; }

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

    public virtual ICollection<DoctorAvailabilityException> DoctorAvailabilityExceptions { get; set; } = new List<DoctorAvailabilityException>();

    public virtual ICollection<DoctorNotification> DoctorNotifications { get; set; } = new List<DoctorNotification>();

    public virtual ICollection<DoctorPatientLink> DoctorPatientLinks { get; set; } = new List<DoctorPatientLink>();

    public virtual DoctorPreference? DoctorPreference { get; set; }

    public virtual ICollection<DoctorService> DoctorServices { get; set; } = new List<DoctorService>();

    public virtual ICollection<DoctorSpecialty> DoctorSpecialties { get; set; } = new List<DoctorSpecialty>();

    public virtual ICollection<DoctorWeeklyHour> DoctorWeeklyHours { get; set; } = new List<DoctorWeeklyHour>();

    public virtual ICollection<MedicalReportVersion> MedicalReportVersions { get; set; } = new List<MedicalReportVersion>();

    public virtual ICollection<MedicalReport> MedicalReports { get; set; } = new List<MedicalReport>();
}
