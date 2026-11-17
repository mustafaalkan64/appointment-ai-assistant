using AppointmentBooking.McpServer.Protocol;
using AppointmentBooking.McpServer.Services;
using FluentAssertions;
using Xunit;

namespace AppointmentBooking.UnitTests.Mcp;

public class McpToolResultSummarizerTests
{
    [Fact]
    public void SummarizeAppointments_ShouldReturnReadableTurkishList()
    {
        var result = CreateResult("""
            {
              "Appointments": [
                {
                  "Id": "11111111-1111-1111-1111-111111111111",
                  "AppointmentNumber": "APT-20261118-ABC123",
                  "LocalDisplay": "18.11.2026 10:00 - 10:30",
                  "Status": "Confirmed"
                },
                {
                  "Id": "22222222-2222-2222-2222-222222222222",
                  "AppointmentNumber": "APT-20261120-DEF456",
                  "LocalDisplay": "20.11.2026 14:00 - 14:30",
                  "Status": "Pending"
                }
              ]
            }
            """);

        var success = McpToolResultSummarizer.TrySummarize(
            "get_customer_appointments", result, out var summary);

        success.Should().BeTrue();
        summary.Should().Contain("Kayıtlı randevularınız:");
        summary.Should().Contain("APT-20261118-ABC123: 18.11.2026 10:00 - 10:30 (Onaylandı)");
        summary.Should().Contain("APT-20261120-DEF456: 20.11.2026 14:00 - 14:30 (Onay Bekliyor)");
        summary.Should().NotContain("StartUtc");
        summary.Should().NotContain("11111111");
    }

    [Fact]
    public void SummarizeSlots_ShouldReturnReadableTimeList()
    {
        var result = CreateResult("""
            {
              "Slots": [
                { "Id": "33333333-3333-3333-3333-333333333333", "LocalDisplay": "18.11.2026 09:00 - 09:30" },
                { "Id": "44444444-4444-4444-4444-444444444444", "LocalDisplay": "18.11.2026 10:00 - 10:30" }
              ]
            }
            """);

        var success = McpToolResultSummarizer.TrySummarize("get_available_slots", result, out var summary);

        success.Should().BeTrue();
        summary.Should().Contain("• 18.11.2026 09:00 - 09:30");
        summary.Should().Contain("• 18.11.2026 10:00 - 10:30");
        summary.Should().NotContain("Id");
    }

    [Fact]
    public void LooksLikeTechnicalJsonExplanation_ShouldDetectRawFieldDescriptions()
    {
        const string technicalReply =
            "This is a JSON object that represents an array of appointments. Each has Id, StartUtc, EndUtc and LocalDisplay properties.";

        McpToolResultSummarizer.LooksLikeTechnicalJsonExplanation(technicalReply).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeTechnicalJsonExplanation_ShouldAllowNormalTurkishReply()
    {
        const string naturalReply =
            "18.11.2026 saat 10:00 randevunuz onaylanmış durumda. Başka nasıl yardımcı olabilirim?";

        McpToolResultSummarizer.LooksLikeTechnicalJsonExplanation(naturalReply).Should().BeFalse();
    }

    private static McpToolResult CreateResult(string json) =>
        new(
            new List<McpContentItem>
            {
                new("text", json)
            },
            IsError: false);
}
