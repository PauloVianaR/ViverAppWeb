using System.Globalization;
using System.Text;

namespace ViverApp.Api.Features.MedicalRecords;

public sealed class ClinicalPdfRenderer
{
    public byte[] Render(MedicalRecordPdfSnapshot snapshot)
    {
        var lines = BuildLines(snapshot);
        var pages = lines.Chunk(48).Select(x => x.ToArray()).ToArray();
        if (pages.Length == 0) pages = [[]];
        return BuildPdf(pages);
    }

    private static IReadOnlyList<string> BuildLines(MedicalRecordPdfSnapshot x)
    {
        var lines = new List<string>
        {
            "ViverApp - Centro Medico Viver",
            "PRONTUARIO ELETRONICO - DOCUMENTO CONFIDENCIAL",
            $"Paciente: {x.Patient.FullName}",
            $"Nascimento: {x.Patient.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Nao informado"}",
            $"Periodo: {x.From:dd/MM/yyyy} a {x.To:dd/MM/yyyy}",
            $"Emitido em: {Local(x.GeneratedAtUtc):dd/MM/yyyy HH:mm}",
            string.Empty,
        };
        if (x.Entries.Count > 0)
        {
            lines.Add("REGISTROS CLINICOS");
            foreach (var entry in x.Entries)
            {
                lines.Add($"Atendimento #{entry.AppointmentNumber} - {Local(entry.AppointmentAtUtc):dd/MM/yyyy HH:mm}");
                lines.Add($"Medico: {entry.DoctorName} - {entry.LicenseLabel} - versao {entry.CurrentVersionNumber}");
                var content = entry.Versions?.FirstOrDefault()?.Content;
                Add(lines, "Queixa principal", content?.ChiefComplaint);
                Add(lines, "Historia da doenca atual", content?.PresentIllnessHistory);
                Add(lines, "Antecedentes pessoais", content?.PersonalHistory);
                Add(lines, "Antecedentes familiares", content?.FamilyHistory);
                Add(lines, "Alergias", content?.Allergies);
                Add(lines, "Medicamentos", content?.Medications);
                Add(lines, "Habitos", content?.RelevantHabits);
                Add(lines, "Exame fisico", content?.PhysicalExamination);
                Add(lines, "Hipoteses/diagnosticos", content?.DiagnosticHypotheses);
                Add(lines, "Conduta e orientacoes", content?.ConductAndGuidance);
                Add(lines, "Plano de acompanhamento", content?.FollowUpPlan);
                Add(lines, "Evolucao", content?.ClinicalEvolution);
                Add(lines, "Observacoes", content?.AdditionalNotes);
                if (content is not null)
                    lines.Add($"Sinais vitais: PA {content.SystolicPressureMmhg?.ToString() ?? "-"}/{content.DiastolicPressureMmhg?.ToString() ?? "-"} mmHg; FC {content.HeartRateBpm?.ToString() ?? "-"} bpm; Temp. {content.TemperatureCelsius?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"} C; Peso {content.WeightKg?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-"} kg; Altura {content.HeightCm?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-"} cm");
                lines.Add(string.Empty);
            }
        }
        if (x.Timeline.Count > 0)
        {
            lines.Add("LINHA DO TEMPO");
            foreach (var item in x.Timeline)
                lines.Add($"{Local(item.OccurredAtUtc):dd/MM/yyyy HH:mm} - {item.Title}{(item.AppointmentNumber.HasValue ? $" - Atendimento #{item.AppointmentNumber}" : string.Empty)}");
            lines.Add(string.Empty);
        }
        if (x.Financial is not null)
        {
            lines.Add("RESUMO FINANCEIRO");
            foreach (var item in x.Financial.Items)
                lines.Add($"Atendimento #{item.AppointmentNumber} - {item.MethodLabel} - {item.StatusCode} - R$ {item.Amount:0.00}");
            lines.Add($"Recebido: R$ {x.Financial.Received:0.00} | Estornado: R$ {x.Financial.Reversed:0.00} | Liquido: R$ {x.Financial.Net:0.00}");
            lines.Add(string.Empty);
        }
        lines.Add("Este documento contem dados pessoais sensiveis. Acesso e compartilhamento sao restritos.");
        lines.Add($"Identificador: PEP-{x.Patient.PatientAccountId}-{x.GeneratedAtUtc:yyyyMMddHHmmss}");
        return Wrap(lines, 104);
    }

    private static void Add(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {value}");
    }

    private static List<string> Wrap(IEnumerable<string> source, int width)
    {
        var result = new List<string>();
        foreach (var original in source)
        {
            var remaining = original.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (remaining.Length == 0) { result.Add(string.Empty); continue; }
            while (remaining.Length > width)
            {
                var split = remaining.LastIndexOf(' ', width);
                if (split < width / 2) split = width;
                result.Add(remaining[..split]);
                remaining = remaining[split..].TrimStart();
            }
            result.Add(remaining);
        }
        return result;
    }

    private static byte[] BuildPdf(IReadOnlyList<string[]> pages)
    {
        var objectCount = 3 + pages.Count * 2;
        var objects = new byte[objectCount + 1][];
        objects[1] = Bytes("<< /Type /Catalog /Pages 2 0 R >>");
        var pageIds = Enumerable.Range(0, pages.Count).Select(i => 4 + i * 2).ToArray();
        objects[2] = Bytes($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>");
        objects[3] = Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < pages.Count; i++)
        {
            var pageId = pageIds[i];
            var contentId = pageId + 1;
            objects[pageId] = Bytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>");
            var stream = BuildContent(pages[i], i + 1, pages.Count);
            objects[contentId] = Bytes($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(Bytes("\nendstream")).ToArray();
        }
        using var output = new MemoryStream();
        Write(output, "%PDF-1.7\n%\u00e2\u00e3\u00cf\u00d3\n");
        var offsets = new long[objectCount + 1];
        for (var id = 1; id <= objectCount; id++)
        {
            offsets[id] = output.Position;
            Write(output, $"{id} 0 obj\n"); output.Write(objects[id]); Write(output, "\nendobj\n");
        }
        var xref = output.Position;
        Write(output, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
        for (var id = 1; id <= objectCount; id++) Write(output, $"{offsets[id]:0000000000} 00000 n \n");
        Write(output, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return output.ToArray();
    }

    private static byte[] BuildContent(IReadOnlyList<string> lines, int page, int total)
    {
        var text = new StringBuilder("BT\n/F1 9 Tf\n48 800 Td\n13 TL\n");
        foreach (var line in lines) text.Append('(').Append(Escape(line)).Append(") Tj\nT*\n");
        text.Append("ET\nBT\n/F1 8 Tf\n48 24 Td\n(Pagina ").Append(page).Append(" de ").Append(total).Append(") Tj\nET");
        return Bytes(text.ToString());
    }

    private static string Escape(string value) => new(value.Select(ch => ch switch
    {
        '\\' => "\\\\",
        '(' => "\\(",
        ')' => "\\)",
        >= ' ' and <= '\u00ff' => ch.ToString(),
        _ => "?",
    }).SelectMany(x => x).ToArray());

    private static byte[] Bytes(string value) => Encoding.Latin1.GetBytes(value);
    private static void Write(Stream stream, string value) => stream.Write(Bytes(value));
    private static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
