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
        modelBuilder.Entity<Payment>()
            .Property(entity => entity.RowVersion)
            .IsConcurrencyToken();
    }
}
