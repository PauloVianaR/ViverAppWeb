namespace ViverApp.Web;

public sealed record AppointmentPrintRow(ulong Number, string PatientName, string DoctorName, string Services,
    string CategoryCode, string Specialty, DateTime LocalStartsAt, string BillingFormCode,
    decimal BasePriceAmount, decimal PriceAmount, bool RequiresPayment, bool IsPaid, bool PaymentDeferred,
    string? Notes, string StatusCode);
public sealed record AppointmentPrintData(string ClinicName, string IssuedBy, DateTime LocalIssuedAt,
    string TimezoneName, string FilterDescription, IReadOnlyList<AppointmentPrintRow> Rows);
