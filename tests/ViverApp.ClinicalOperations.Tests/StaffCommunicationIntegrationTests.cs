using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;
using ViverApp.Api.Features.StaffCommunication;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using Xunit;

namespace ViverApp.ClinicalOperations.Tests;

public sealed class StaffCommunicationIntegrationTests
{
    [Fact]
    public async Task Chat_IsRestrictedToActiveStaff_AndPersistsReadState()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var doctor = NewAccount("doctor", "Dr. Chat");
        var manager = NewAccount("manager", "Gestora Chat");
        var patient = NewAccount("patient", "Paciente Chat Sobrenome");
        db.Accounts.AddRange(doctor, manager, patient);
        await db.SaveChangesAsync();
        var presence = new StaffPresence();
        presence.Add(manager.Id);
        var service = NewService(db, presence);

        var contacts = await service.ContactsAsync(doctor.Id, CancellationToken.None);
        Assert.Contains(contacts, c => c.Id == manager.Id && c.Online);
        Assert.DoesNotContain(contacts, c => c.Id == doctor.Id || c.Id == patient.Id);
        var denied = await Assert.ThrowsAsync<StaffRuleException>(() => service.SendAsync(
            doctor.Id, new SendStaffMessageRequest(patient.Id, Guid.NewGuid(), "Mensagem"), CancellationToken.None));
        Assert.Equal(404, denied.Status);

        var nonce = Guid.NewGuid();
        var sent = await service.SendAsync(doctor.Id, new SendStaffMessageRequest(manager.Id, nonce, "  Bom dia  "), CancellationToken.None);
        var replay = await service.SendAsync(doctor.Id, new SendStaffMessageRequest(manager.Id, nonce, "Bom dia"), CancellationToken.None);
        Assert.Equal(sent.Id, replay.Id);
        Assert.Equal("Bom dia", sent.Body);
        Assert.Single(await service.ConversationAsync(manager.Id, doctor.Id, CancellationToken.None));
        Assert.Equal(1, (await service.ContactsAsync(manager.Id, CancellationToken.None)).Single(c => c.Id == doctor.Id).Unread);
        await service.ReadConversationAsync(manager.Id, doctor.Id, CancellationToken.None);
        Assert.Equal(0, (await service.ContactsAsync(manager.Id, CancellationToken.None)).Single(c => c.Id == doctor.Id).Unread);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AppointmentAlert_IsQueuedForEveryActiveManager_AndCanBeMarkedRead()
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var managerOne = NewAccount("manager", "Gestora Um");
        var managerTwo = NewAccount("manager", "Gestora Dois");
        var doctor = NewAccount("doctor", "Dr. Chat");
        var patient = NewAccount("patient", "Paciente Chat Sobrenome");
        db.Accounts.AddRange(managerOne, managerTwo, doctor, patient);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.ProfessionalProfiles.Add(new ProfessionalProfile
        {
            AccountId = doctor.Id,
            ProfessionalTitle = "Dr.",
            LicenseTypeCode = "CRM",
            LicenseStateCode = "MG",
            LicenseNumber = "TESTE-CHAT",
            DefaultAppointmentDurationMinutes = 10,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        });
        var type = new AppointmentType
        {
            Name = "__staff_chat_test__" + Guid.NewGuid().ToString("N"),
            CategoryCode = "consultation",
            Description = "Teste sintético",
            ModalityCode = "in_person",
            DurationMinutes = 10,
            PriceAmount = 0,
            RequiresPayment = false,
            IsActive = true,
            DisplayOrder = 999,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        db.AppointmentTypes.Add(type);
        await db.SaveChangesAsync();
        var appointment = new Appointment
        {
            AppointmentNumber = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray()) | (1UL << 63),
            PatientAccountId = patient.Id,
            ProfessionalAccountId = doctor.Id,
            AppointmentTypeId = type.Id,
            CreatedByAccountId = managerOne.Id,
            StatusCode = "confirmed",
            ModalityCode = "in_person",
            StartsAtUtc = now.AddDays(1),
            EndsAtUtc = now.AddDays(1).AddMinutes(10),
            PriceAmount = 0,
            BasePriceAmount = 0,
            RequiresPayment = false,
            DiscountPercent = 0,
            PaymentLocationCode = "clinic",
            CurrencyCode = "BRL",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = 1
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        var service = NewService(db, new StaffPresence());
        var queued = await service.QueueManagerAlertsAsync(appointment, "appointment_started", CancellationToken.None);
        await service.PublishManagerAlertsAsync(queued);
        var activeManagers = await db.Accounts.CountAsync(a => a.RoleCode == "manager" && a.StatusCode == "active");
        Assert.Equal(activeManagers, queued.Count(a => a.AppointmentId == appointment.Id));
        Assert.All(queued, a => Assert.Equal("Paciente Chat", a.PatientName));
        Assert.Single(await service.ManagerAlertsAsync(managerOne.Id, CancellationToken.None));
        Assert.Single(await service.ManagerAlertsAsync(managerTwo.Id, CancellationToken.None));
        Assert.All(await service.ManagerAlertsAsync(managerTwo.Id, CancellationToken.None), a => Assert.Equal("Paciente Chat", a.PatientName));
        await service.ReadManagerAlertsAsync(managerOne.Id, CancellationToken.None);
        Assert.All(await service.ManagerAlertsAsync(managerOne.Id, CancellationToken.None), a => Assert.True(a.IsRead));
        Assert.All(await service.ManagerAlertsAsync(managerTwo.Id, CancellationToken.None), a => Assert.False(a.IsRead));
        await transaction.RollbackAsync();
    }

    private static StaffCommunicationService NewService(ViverAppDbContext db, StaffPresence presence) =>
        new(db, presence, TimeProvider.System, new NullHubContext(), NullLogger<StaffCommunicationService>.Instance);
    private static ViverAppDbContext CreateContext()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<StaffCommunicationIntegrationTests>().Build();
        var value = configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("LocalConnection ausente.");
        var builder = new MySqlConnectionStringBuilder(value);
        if (builder.Database != "viverappweb") throw new InvalidOperationException("Database recusado.");
        return new ViverAppDbContext(new DbContextOptionsBuilder<ViverAppDbContext>().UseMySQL(builder.ConnectionString).Options);
    }
    private static Account NewAccount(string role, string name)
    {
        var email = $"staff-{Guid.NewGuid():N}@phase31.example.test";
        return new Account
        {
            RoleCode = role,
            StatusCode = "active",
            FullName = name,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailVerified = true,
            SecurityStamp = RandomNumberGenerator.GetBytes(32),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = 1
        };
    }
    private sealed class NullHubContext : IHubContext<StaffChatHub>
    {
        public IHubClients Clients { get; } = new NullHubClients();
        public IGroupManager Groups { get; } = new NullGroupManager();
    }
    private sealed class NullHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NullClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> ids) => Proxy;
        public IClientProxy Client(string id) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> ids) => Proxy;
        public IClientProxy Group(string name) => Proxy;
        public IClientProxy GroupExcept(string name, IReadOnlyList<string> ids) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> names) => Proxy;
        public IClientProxy User(string id) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> ids) => Proxy;
    }
    private sealed class NullClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string id, string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string id, string name, CancellationToken ct = default) => Task.CompletedTask;
    }
}
