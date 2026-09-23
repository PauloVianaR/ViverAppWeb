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
        modelBuilder.Entity<AccountUiPreference>()
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
        modelBuilder.Entity<ProfessionalProfile>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<ProfessionalWeeklyHour>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<ProfessionalPreference>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<ProfessionalVariableHour>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<Holiday>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<MedicalReport>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<ElectronicHealthRecord>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<MedicalRecordDraft>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<MedicalRecordDocument>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<AppointmentType>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<Payment>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        // O scaffold Oracle não identifica a coluna gerada que sustenta a unicidade do pagamento ativo.
        modelBuilder.Entity<Payment>()
            .Property(entity => entity.ActiveAppointmentId)
            .ValueGeneratedOnAddOrUpdate();
        modelBuilder.Entity<PaymentReversal>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<PremiumMembership>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        // O scaffold Oracle não identifica esta coluna gerada no MySQL (migration 0014).
        modelBuilder.Entity<PremiumMembership>()
            .Property(entity => entity.OpenAccountId)
            .ValueGeneratedOnAddOrUpdate();
        modelBuilder.Entity<Specialty>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<AdministratorNotification>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<ProfessionalNotification>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<ApplicationSetting>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
        modelBuilder.Entity<PremiumPlan>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
    }
}
