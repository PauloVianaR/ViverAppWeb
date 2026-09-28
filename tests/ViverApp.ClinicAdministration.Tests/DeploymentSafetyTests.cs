using Microsoft.Extensions.Configuration;
using ViverApp.Api.Features.PatientExperience;
using Xunit;

namespace ViverApp.ClinicAdministration.Tests;

public sealed class DeploymentSafetyTests
{
    [Fact]
    public void Production_refuses_missing_malware_scanner_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.Throws<InvalidOperationException>(() =>
            WindowsDocumentMalwareScanner.AssertProductionReady(configuration, AppContext.BaseDirectory));
    }

    [Fact]
    public void Production_refuses_relative_scanner_paths()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:MalwareScan:ExecutablePath"] = "MpCmdRun.exe",
            ["Security:MalwareScan:WorkDirectory"] = ".\\scan",
        }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            WindowsDocumentMalwareScanner.AssertProductionReady(configuration, AppContext.BaseDirectory));
    }
}
