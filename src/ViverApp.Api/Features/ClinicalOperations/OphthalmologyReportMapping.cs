using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.ClinicalOperations;

internal static class OphthalmologyReportMapping
{
    public static Task<bool> IsOphthalmologyAsync(
        ViverAppDbContext database, ulong professionalAccountId, CancellationToken ct) =>
        database.ProfessionalSpecialties.AsNoTracking().AnyAsync(link =>
            link.ProfessionalAccountId == professionalAccountId
            && link.Specialty.NormalizedName == "OFTALMOLOGIA", ct);

    public static OphthalmologyReportFields Normalize(OphthalmologyReportFields fields) =>
        new(Clean(fields.OphthalmicHistory), Clean(fields.VisualAcuity), Clean(fields.Refraction),
            Clean(fields.Biomicroscopy), Clean(fields.Tonometry), Clean(fields.FundusExam));

    public static bool HasContent(OphthalmologyReportFields fields) =>
        Fields(fields).Any(field => field.Value is not null);

    public static bool WithinLimit(OphthalmologyReportFields fields) =>
        Fields(fields).All(field => field.Value is null || field.Value.Length <= 12000);

    public static string LegacySummary(OphthalmologyReportFields fields)
    {
        var text = "Registro oftalmológico estruturado.\n" +
            string.Join("\n", Fields(fields).Where(field => field.Value is not null)
                .Select(field => $"{field.Label}: {field.Value}"));
        return text.Length <= 12000 ? text : text[..12000];
    }

    public static OphthalmologyReportFields? From(MedicalReport report) =>
        Result(report.OphthalmicHistory, report.VisualAcuity, report.Refraction,
            report.Biomicroscopy, report.Tonometry, report.FundusExam);

    public static OphthalmologyReportFields? From(MedicalReportVersion version) =>
        Result(version.OphthalmicHistory, version.VisualAcuity, version.Refraction,
            version.Biomicroscopy, version.Tonometry, version.FundusExam);

    public static void Assign(MedicalReport report, OphthalmologyReportFields fields)
    {
        report.OphthalmicHistory = fields.OphthalmicHistory;
        report.VisualAcuity = fields.VisualAcuity;
        report.Refraction = fields.Refraction;
        report.Biomicroscopy = fields.Biomicroscopy;
        report.Tonometry = fields.Tonometry;
        report.FundusExam = fields.FundusExam;
    }

    public static void Assign(MedicalReportVersion version, OphthalmologyReportFields fields)
    {
        version.OphthalmicHistory = fields.OphthalmicHistory;
        version.VisualAcuity = fields.VisualAcuity;
        version.Refraction = fields.Refraction;
        version.Biomicroscopy = fields.Biomicroscopy;
        version.Tonometry = fields.Tonometry;
        version.FundusExam = fields.FundusExam;
    }

    private static OphthalmologyReportFields? Result(
        string? history, string? acuity, string? refraction,
        string? biomicroscopy, string? tonometry, string? fundus)
    {
        var fields = new OphthalmologyReportFields(history, acuity, refraction, biomicroscopy, tonometry, fundus);
        return HasContent(fields) ? fields : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<(string Label, string? Value)> Fields(OphthalmologyReportFields fields)
    {
        yield return ("História oftalmológica", fields.OphthalmicHistory);
        yield return ("Acuidade visual", fields.VisualAcuity);
        yield return ("Refração", fields.Refraction);
        yield return ("Biomicroscopia", fields.Biomicroscopy);
        yield return ("Tonometria", fields.Tonometry);
        yield return ("Fundo de olho", fields.FundusExam);
    }
}
