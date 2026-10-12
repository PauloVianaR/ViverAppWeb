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
    public void ReturnPreservesDateProfessionalAndView()
    {
        const string path="/gestao/agenda?view=day&date=2026-10-19&professional=42";
        Assert.Equal(path,AppointmentNavigation.AgendaReturn("/gestao",path));
    }
}
