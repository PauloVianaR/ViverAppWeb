using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Infrastructure.Persistence.Generated;

public partial class ViverAppDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<Appointment>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<Clinic>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<ClinicWeeklyHour>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<DoctorProfile>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<DoctorWeeklyHour>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<Holiday>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<MedicalReport>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<AppointmentType>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<Payment>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<Specialty>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
    }
}
