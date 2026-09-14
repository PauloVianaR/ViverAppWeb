using System;
using System.Collections.Generic;

namespace ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

public partial class PrivateDocument
{
    public Guid Id { get; set; }

    public ulong OwnerAccountId { get; set; }

    public string ContentType { get; set; } = null!;

    public string OriginalFileName { get; set; } = null!;

    public uint SizeBytes { get; set; }

    public byte[] Sha256 { get; set; } = null!;

    public string StorageProviderCode { get; set; } = null!;

    public string? ObjectKey { get; set; }

    public string? StorageEtag { get; set; }

    public DateTime? MigratedAtUtc { get; set; }

    public DateTime? LastVerifiedAtUtc { get; set; }

    public DateTime? LegacyContentRetainedUntilUtc { get; set; }

    public ulong RowVersion { get; set; }

    public byte[]? ProtectedContent { get; set; }

    public string StatusCode { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public virtual MedicalRecordDocument? MedicalRecordDocument { get; set; }

    public virtual Account OwnerAccount { get; set; } = null!;

    public virtual ICollection<PremiumMembership> PremiumMembershipPrivateDocuments { get; set; } = new List<PremiumMembership>();

    public virtual ICollection<PremiumMembership> PremiumMembershipProofDocuments { get; set; } = new List<PremiumMembership>();
}
