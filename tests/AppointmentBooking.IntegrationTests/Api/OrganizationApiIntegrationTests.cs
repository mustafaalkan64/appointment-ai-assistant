using System.Net;
using System.Net.Http.Json;
using AppointmentBooking.Application.Features.Availability.GenerateSlots;
using AppointmentBooking.Application.Features.Availability.GetAvailableSlots;
using AppointmentBooking.Application.Features.Organizations.GetOrganization;
using AppointmentBooking.Application.Features.Organizations.RegisterOrganization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AppointmentBooking.IntegrationTests.Api;

public class OrganizationApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public OrganizationApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_ShouldReturnHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Organization_FullLifecycle_Register_GenerateSlots_QueryAvailability()
    {
        // 1. Register a new Organization
        var uniqueSlug = "beauty-center-" + Guid.NewGuid().ToString("N")[..6];
        var regCommand = new RegisterOrganizationCommand(
            Name: "Elite Beauty Center",
            Slug: uniqueSlug,
            Email: $"{uniqueSlug}@beauty.com",
            PhoneNumber: "+905553334455",
            Address: "Nisantasi, Istanbul",
            TimeZone: "Europe/Istanbul",
            AdminEmail: $"admin@{uniqueSlug}.com",
            AdminPassword: "SuperSecurePassword123!",
            AdminFullName: "Admin User");

        var regResponse = await _client.PostAsJsonAsync("/api/organizations", regCommand);
        regResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var regContent = await regResponse.Content.ReadFromJsonAsync<RegisterOrganizationResponse>();
        regContent.Should().NotBeNull();
        regContent!.Slug.Should().Be(uniqueSlug);
        var orgId = regContent.Id;

        // 2. Fetch Organization by Id
        var getByIdResponse = await _client.GetAsync($"/api/organizations/{orgId}");
        getByIdResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var orgDto = await getByIdResponse.Content.ReadFromJsonAsync<GetOrganizationResponse>();
        orgDto.Should().NotBeNull();
        orgDto!.Name.Should().Be("Elite Beauty Center");
        orgDto.WeeklyAvailabilities.Should().NotBeEmpty();

        // 3. Fetch Organization by Slug
        var getBySlugResponse = await _client.GetAsync($"/api/organizations/by-slug/{uniqueSlug}");
        getBySlugResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Generate Slots for next week
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var endDate = startDate.AddDays(3);

        var genCommand = new GenerateSlotsCommand(orgId, startDate, endDate, SkipExisting: true);
        var genResponse = await _client.PostAsJsonAsync($"/api/organizations/{orgId}/slots/generate", genCommand);
        genResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var genResult = await genResponse.Content.ReadFromJsonAsync<GenerateSlotsResponse>();
        genResult.Should().NotBeNull();
        genResult!.TotalCreatedSlots.Should().BeGreaterThan(0);

        // 5. Query Available Slots
        var availResponse = await _client.GetAsync($"/api/organizations/{orgId}/slots/available?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
        availResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var availResult = await availResponse.Content.ReadFromJsonAsync<GetAvailableSlotsResponse>();
        availResult.Should().NotBeNull();
        availResult!.Slots.Should().NotBeEmpty();
        availResult.TimeZone.Should().Be("Europe/Istanbul");

        var firstSlot = availResult.Slots.First();
        firstSlot.LocalStartTime.Should().NotBeNullOrWhiteSpace();
        firstSlot.LocalDisplay.Should().Contain(":");
    }
}
