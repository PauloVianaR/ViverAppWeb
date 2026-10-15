using System.Linq.Expressions;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.PatientScheduling;

public static class AppointmentPaymentPolicy
{
    public static readonly Expression<Func<Appointment, bool>> Pending =
        a => a.StatusCode != "canceled" && a.RequiresPayment && (a.CurrentPayment == null || a.CurrentPayment.StatusCode != "paid");

    public static IQueryable<Appointment> FilterPending(IQueryable<Appointment> query, bool? pending) =>
        pending switch
        {
            true => query.Where(Pending),
            false => query.Where(a => !a.RequiresPayment || a.CurrentPayment != null && a.CurrentPayment.StatusCode == "paid"),
            _ => query,
        };

    public static bool DeferredOutstanding(Appointment a) =>
        a.PaymentDeferred && a.RequiresPayment && a.CurrentPayment?.StatusCode != "paid";

    public static bool CanReceiveManually(Appointment a) => a.RequiresPayment && a.PriceAmount > 0
        && a.PaymentLocationCode == "clinic"
        && (a.PaymentDeferred || a.ModalityCode == "in_person" && a.StatusCode is "pending" or "confirmed")
        && a.CurrentPayment?.StatusCode is not ("paid" or "reversal_pending");
}
