namespace ViverApp.Api.Features.Payments;

internal sealed record PaymentTransition(
    string TargetStatus,
    bool Apply,
    string? IgnoredReason);

internal static class PaymentStateMachine
{
    public static string Normalize(string providerStatus, long? totalCents = null, long? refundedCents = null)
    {
        var status = providerStatus.Trim().ToUpperInvariant();
        if (status == "CANCELED" && totalCents is > 0 && refundedCents >= totalCents)
        {
            return "refunded";
        }

        return status switch
        {
            "ACTIVE" or "WAITING" => "pending",
            "AUTHORIZED" or "IN_ANALYSIS" => "authorized",
            "PAID" => "paid",
            "DECLINED" => "failed",
            "INACTIVE" or "EXPIRED" or "CANCELED" => "canceled",
            _ => throw new PaymentRuleException(StatusCodes.Status422UnprocessableEntity, "Status PagBank não reconhecido."),
        };
    }

    public static PaymentTransition Decide(
        string current,
        string target,
        DateTime? currentProviderTime,
        DateTime? incomingProviderTime)
    {
        if (current == target)
        {
            return new(target, false, "duplicate_status");
        }

        if (current == "refunded")
        {
            return new(target, false, "terminal_refunded");
        }

        if (current == "paid" && target != "refunded")
        {
            return new(target, false, "terminal_paid");
        }

        if (currentProviderTime.HasValue
            && incomingProviderTime.HasValue
            && incomingProviderTime < currentProviderTime
            && target is not ("paid" or "refunded"))
        {
            return new(target, false, "out_of_order");
        }

        return new(target, true, null);
    }
}

