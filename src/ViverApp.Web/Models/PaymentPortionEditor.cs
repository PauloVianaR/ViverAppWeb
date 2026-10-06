namespace ViverApp.Web;

public sealed class PaymentPortionEditor
{
    public string MethodCode { get; set; } = "pix";
    public decimal Amount { get; set; }
    public string CardLastFour { get; set; } = "";
    public string AuthorizationReference { get; set; } = "";
    public bool IsCard => MethodCode is "credit_card" or "debit_card";

    public static string? Validate(IReadOnlyList<PaymentPortionEditor> portions, decimal expected)
    {
        if (portions.Count is < 1 or > 10 || portions.Any(x => x.Amount <= 0))
            return "Informe de uma a dez parcelas com valores positivos.";
        if (portions.Sum(x => x.Amount) != expected)
            return $"A soma das formas de pagamento deve ser exatamente {ManagerLabels.Money(expected)}.";
        if (portions.Any(x => x.IsCard && (x.CardLastFour.Length != 4 || !x.CardLastFour.All(char.IsAsciiDigit)
            || string.IsNullOrWhiteSpace(x.AuthorizationReference))))
            return "Informe quatro dígitos e autorização em cada parcela paga com cartão.";
        return null;
    }
}
