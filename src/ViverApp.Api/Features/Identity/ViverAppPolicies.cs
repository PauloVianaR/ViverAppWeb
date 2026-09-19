namespace ViverApp.Api.Features.Identity;

public static class ViverAppPolicies
{
    public const string Patient = "role:patient";
    public const string Doctor = "role:doctor";
    public const string ClinicalProfessional = "role:clinical-professional";
    public const string Manager = "role:manager";
    public const string Administrator = "role:administrator";
    public const string ClinicalStaff = "role:clinical-staff";
    public const string Management = "role:management";
    public const string CashRead = "finance:cash-read";
    public const string CashWrite = "finance:cash-write";
    public const string CashClose = "finance:cash-close";
    public const string CashPrint = "finance:cash-print";
    public const string PaymentReverse = "finance:payment-reverse";
    public const string MedicalRecordRead = "medical-record:read";
    public const string MedicalRecordClinicalRead = "medical-record:clinical-read";
    public const string MedicalRecordWrite = "medical-record:write";
    public const string MedicalRecordDocument = "medical-record:document";
    public const string MedicalRecordExport = "medical-record:export";
    public const string MedicalRecordAudit = "medical-record:audit";
    public const string MfaEnrollment = "identity:mfa-enrollment";
    public const string MfaSatisfied = "identity:mfa-satisfied";
}
