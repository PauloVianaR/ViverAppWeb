using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Features.ManagerExperience;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

public sealed class BookingWorkspaceContractTests
{
    [Fact]
    public void OperationalReviewAndAttachments_RequireProtectedStaffWrites()
    {
        var type = typeof(AppointmentWorkspaceOperationsController);
        var roles = Assert.Single(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal(["administrator", "manager", "doctor", "psychologist"], roles.Roles!.Split(','));
        Assert.Contains(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ServiceFilterAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.ServiceFilterAttribute>(), x => x.ServiceType == typeof(ViverApp.Api.Features.AdministratorExperience.AdministratorStepUpFilter));
        foreach (var method in new[] { nameof(AppointmentWorkspaceOperationsController.SaveReview), nameof(AppointmentWorkspaceOperationsController.Upload), nameof(AppointmentWorkspaceOperationsController.Delete) })
            Assert.Single(type.GetMethod(method)!.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OperationalReviewWrite>(
            """{"rating":5,"feedback":"teste","rowVersion":0,"updatedByAccountId":1}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Workspace_RejectsPatientRoleAndKeepsAdministrativeStepUpAndWriteLimits()
    {
        var type = typeof(BookingWorkspaceController);
        var authorization = Assert.Single(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal(["administrator", "manager", "doctor", "psychologist"], authorization.Roles!.Split(','));
        Assert.Contains(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ServiceFilterAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.ServiceFilterAttribute>(), filter => filter.ServiceType == typeof(ViverApp.Api.Features.AdministratorExperience.AdministratorStepUpFilter));
        foreach (var name in new[] { nameof(BookingWorkspaceController.Update), nameof(BookingWorkspaceController.UpdatePatient) })
            Assert.Single(type.GetMethod(name)!.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), false));
    }

    [Theory]
    [InlineData("private", true, true)]
    [InlineData("private", false, false)]
    [InlineData("insurance", true, false)]
    [InlineData("insurance", false, false)]
    public void BillingForm_PreservesCatalogRule_ExceptInsuranceIsNeverCharged(string form, bool catalog, bool expected)
        => Assert.Equal(expected, PatientSchedulingService.ApplyBillingForm(form, catalog));

    [Theory]
    [InlineData("free")]
    [InlineData("")]
    public void BillingForm_RejectsUnknownValues(string form)
        => Assert.Throws<SchedulingRuleException>(() => PatientSchedulingService.ApplyBillingForm(form, true));

    [Fact]
    public void LegacyBooking_DefaultsToParticular()
    {
        var request = new ManagerAppointmentCreateRequest(1, 2, 3, "in_person", new(2026, 10, 12), new(10, 0), null);
        Assert.Equal("private", request.BillingFormCode);
        Assert.Null(request.SpecialtyId);
    }

    [Fact]
    public void MetadataCommand_RequiresVersionAndTypedBillingForm()
    {
        var constructor = typeof(BookingWorkspaceUpdate).GetConstructors()[0];
        var values = new object[] { "anything", (uint)0, (ulong)0 };
        Assert.All(constructor.GetParameters().Select((parameter, index) => (parameter, index)), item =>
            Assert.Contains(item.parameter.GetCustomAttributes(typeof(ValidationAttribute), false).Cast<ValidationAttribute>(),
                attribute => !attribute.IsValid(values[item.index])));
    }

    [Fact]
    public void MetadataCommand_RejectsMassAssignment()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BookingWorkspaceUpdate>(
            """{"billingFormCode":"insurance","specialtyId":1,"rowVersion":1,"priceAmount":0,"statusCode":"confirmed"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
