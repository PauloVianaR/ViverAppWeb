namespace ViverApp.Web;

public static class AppointmentFilterOptions
{
    public static async Task<IReadOnlyList<WebSpecialty>> SpecialtiesAsync(
        Func<string, Task<WebPage<WebSpecialty>?>> load)
    {
        var result = new List<WebSpecialty>();
        for (var page = 1; ; page++)
        {
            // Include inactive specialties so historical appointments remain filterable.
            var data = await load($"api/v1/catalog/specialties?page={page}&pageSize=100&includeInactive=true");
            if (data is null || data.Items.Count == 0) break;
            result.AddRange(data.Items);
            if (result.Count >= data.TotalCount) break;
        }
        return result;
    }
}
