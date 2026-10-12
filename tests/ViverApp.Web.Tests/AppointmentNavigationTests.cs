using Xunit;
namespace ViverApp.Web.Tests;
public sealed class AppointmentNavigationTests
{
    [Theory]
    [InlineData(2026,10,11,2026,10,11,2026,10,18)]
    [InlineData(2026,10,17,2026,10,11,2026,10,18)]
    [InlineData(2026,12,31,2026,12,27,2027,1,3)]
    public void WeekIncludesSundayThroughNextSunday(int y,int m,int d,int sy,int sm,int sd,int ey,int em,int ed)
    {
        var day=new DateTime(y,m,d);
        Assert.Equal(new DateTime(sy,sm,sd),AppointmentNavigation.WeekStart(day));
        Assert.Equal(new DateTime(ey,em,ed),AppointmentNavigation.WeekEnd(day));
    }
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("//example.com")]
    [InlineData("/administracao/agenda?view=day")]
    [InlineData("/gestao/agenda?view=day\nheader")]
    public void ReturnCannotEscapeCurrentArea(string destination)
        =>Assert.Equal("/gestao/agenda",AppointmentNavigation.AgendaReturn("/gestao",destination));
    [Fact]
    public void HistoryStateKeepsContextWithoutAddingQueryParameters()
    {
        var context = new AppointmentNavigationContext("/gestao", "day", new(2026, 10, 19), 42, "09:10");
        var state = AppointmentNavigation.State(context);
        Assert.Equal(context, AppointmentNavigation.Restore("/gestao", state));
        var fallback=AppointmentNavigation.QueryFallback("/gestao/agenda",context);
        Assert.Equal(context with {Time=null},AppointmentNavigation.LegacyContext("/gestao",fallback));
        Assert.Contains("time=09:10",AppointmentNavigation.QueryFallback("/gestao/agendar",context));
        Assert.Null(AppointmentNavigation.Restore("/medico", state));
        Assert.Null(AppointmentNavigation.Restore("/gestao", "not json"));
        Assert.Null(AppointmentNavigation.Restore("/gestao", AppointmentNavigation.State(context with { Time = "wrong" })));
        Assert.Null(AppointmentNavigation.Restore("/gestao", AppointmentNavigation.State(context with { View = "wrong" })));
        Assert.Null(AppointmentNavigation.Restore("/gestao", new string('x', 1001)));
    }
    [Fact]
    public void OldLinksCanBecomeSameAreaHistoryContext()
    {
        Assert.Equal(new AppointmentNavigationContext("/gestao", "day", new(2026, 10, 19), 42),
            AppointmentNavigation.LegacyContext("/gestao", "/gestao/agenda?view=day&date=2026-10-19&professional=42"));
        Assert.Null(AppointmentNavigation.LegacyContext("/gestao", "//example.com?date=2026-10-19"));
        Assert.Null(AppointmentNavigation.LegacyContext("/gestao", "/medico/agenda?view=day&date=2026-10-19"));
    }
    [Fact]
    public async Task SpecialtyOptionsIncludeAllPagesAndHistoricalInactiveSpecialties()
    {
        var urls = new List<string>();
        var specialties = await AppointmentFilterOptions.SpecialtiesAsync(path =>
        {
            urls.Add(path);
            var page = urls.Count;
            return Task.FromResult<WebPage<WebSpecialty>?>(new([new((uint)page, $"Especialidade {page}", page == 1, 1)], page, 1, 2));
        });
        Assert.Equal(2, specialties.Count);
        Assert.False(specialties[1].IsActive);
        Assert.All(urls, path => Assert.Contains("includeInactive=true", path));
    }
    [Fact]
    public void ReturnPreservesDateProfessionalAndView()
    {
        const string path="/gestao/agenda?view=day&date=2026-10-19&professional=42";
        Assert.Equal(path,AppointmentNavigation.AgendaReturn("/gestao",path));
    }
}
