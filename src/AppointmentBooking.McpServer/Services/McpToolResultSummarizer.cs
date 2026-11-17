using System.Text.Json;
using AppointmentBooking.McpServer.Protocol;

namespace AppointmentBooking.McpServer.Services;

/// <summary>
/// Produces short, user-facing Turkish summaries for structured MCP tool results.
/// This prevents raw JSON or technical field descriptions from leaking into chat replies.
/// </summary>
public static class McpToolResultSummarizer
{
    public static bool TrySummarize(string toolName, McpToolResult result, out string summary)
    {
        summary = string.Empty;
        if (result.IsError || result.Content.Count == 0)
            return false;

        var text = result.Content[0].Text;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            return toolName switch
            {
                "get_customer_appointments" => TrySummarizeAppointments(root, out summary),
                "get_available_slots" => TrySummarizeSlots(root, out summary),
                "get_available_dates" => TrySummarizeDates(root, out summary),
                "get_appointment" => TrySummarizeSingleAppointment(root, out summary),
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Detects replies that describe the JSON payload itself (fields, arrays, UTC values)
    /// instead of answering the user in natural language.
    /// </summary>
    public static bool LooksLikeTechnicalJsonExplanation(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return false;

        var lower = reply.ToLowerInvariant();
        var mentionsJson = lower.Contains("json") ||
                           lower.Contains("javascript object notation") ||
                           lower.Contains("```json") ||
                           lower.Contains("anahtar-değer") ||
                           lower.Contains("key-value") ||
                           (reply.Contains('{') && reply.Contains("\":", StringComparison.Ordinal));

        var technicalFieldHits = 0;
        foreach (var field in new[]
                 {
                     "StartUtc", "EndUtc", "LocalDisplay", "LocalStartTime", "LocalEndTime",
                     "AppointmentNumber", "SlotId", "StartTime", "EndTime"
                 })
        {
            if (reply.Contains(field, StringComparison.OrdinalIgnoreCase))
                technicalFieldHits++;
        }

        var describesStructure = lower.Contains("dizi") ||
                                 lower.Contains("array") ||
                                 lower.Contains("nesne") ||
                                 lower.Contains("object") ||
                                 lower.Contains("property") ||
                                 lower.Contains("properties") ||
                                 lower.Contains("alan adı") ||
                                 lower.Contains("alanları") ||
                                 lower.Contains("field");

        return mentionsJson ||
               technicalFieldHits >= 2 ||
               (describesStructure && technicalFieldHits >= 1);
    }

    private static bool TrySummarizeAppointments(JsonElement root, out string summary)
    {
        summary = string.Empty;
        var appointments = GetArray(root, "Appointments") ?? GetArray(root, "appointments");
        if (appointments is null && root.ValueKind == JsonValueKind.Array)
            appointments = root.EnumerateArray().ToList();

        if (appointments is null)
            return false;

        if (appointments.Count == 0)
        {
            summary = "Sistemde kayıtlı bir randevunuz bulunmuyor.";
            return true;
        }

        var lines = appointments
            .OrderBy(x => GetDateTimeOffset(x, "StartUtc") ?? DateTimeOffset.MaxValue)
            .Take(20)
            .Select(appointment =>
            {
                var number = GetString(appointment, "AppointmentNumber");
                var display = GetString(appointment, "LocalDisplay");
                if (string.IsNullOrWhiteSpace(display))
                {
                    var date = GetString(appointment, "Date");
                    var start = GetString(appointment, "LocalStartTime");
                    var end = GetString(appointment, "LocalEndTime");
                    display = string.Join(' ', new[] { date, start, end }.Where(x => !string.IsNullOrWhiteSpace(x)));
                }

                var status = TranslateStatus(GetString(appointment, "Status"));
                var prefix = string.IsNullOrWhiteSpace(number) ? "-" : $"- {number}:";
                return string.IsNullOrWhiteSpace(status)
                    ? $"{prefix} {display}".TrimEnd()
                    : $"{prefix} {display} ({status})".TrimEnd();
            })
            .ToList();

        var header = appointments.Count > lines.Count
            ? $"Kayıtlı randevularınız (ilk {lines.Count} tanesi):"
            : "Kayıtlı randevularınız:";

        summary = header + "\n" + string.Join("\n", lines);
        return true;
    }

    private static bool TrySummarizeSingleAppointment(JsonElement root, out string summary)
    {
        summary = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        var number = GetString(root, "AppointmentNumber");
        var display = GetString(root, "LocalDisplay");
        var status = TranslateStatus(GetString(root, "Status"));
        if (string.IsNullOrWhiteSpace(display) && string.IsNullOrWhiteSpace(number))
            return false;

        summary = string.IsNullOrWhiteSpace(number)
            ? $"Randevu: {display}" + (string.IsNullOrWhiteSpace(status) ? string.Empty : $" ({status})")
            : $"{number} numaralı randevunuz: {display}" +
              (string.IsNullOrWhiteSpace(status) ? string.Empty : $" ({status})");
        return true;
    }

    private static bool TrySummarizeSlots(JsonElement root, out string summary)
    {
        summary = string.Empty;
        var slots = GetArray(root, "Slots") ?? GetArray(root, "slots");
        if (slots is null)
            return false;

        var displays = slots
            .Select(slot => GetString(slot, "LocalDisplay"))
            .Where(display => !string.IsNullOrWhiteSpace(display))
            .Distinct()
            .Take(40)
            .ToList();

        summary = displays.Count == 0
            ? "Belirtilen tarih aralığında müsait randevu saati bulunamadı."
            : "Müsait randevu saatleri:\n" + string.Join("\n", displays.Select(display => $"• {display}"));
        return true;
    }

    private static bool TrySummarizeDates(JsonElement root, out string summary)
    {
        summary = string.Empty;
        var dates = GetArray(root, "AvailableDates") ?? GetArray(root, "availableDates");
        if (dates is null)
            return false;

        var values = dates
            .Select(date => date.ValueKind == JsonValueKind.String ? date.GetString() : date.ToString())
            .Where(date => !string.IsNullOrWhiteSpace(date))
            .Distinct()
            .Take(40)
            .ToList();

        summary = values.Count == 0
            ? "Belirtilen tarih aralığında müsait gün bulunamadı."
            : "Müsait günler:\n" + string.Join("\n", values.Select(date => $"• {date}"));
        return true;
    }

    private static List<JsonElement>? GetArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().ToList();
        }

        return null;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.TryGetDateTimeOffset(out var result))
        {
            return result;
        }

        return null;
    }

    private static string TranslateStatus(string status) =>
        status.ToLowerInvariant() switch
        {
            "pending" => "Onay Bekliyor",
            "confirmed" => "Onaylandı",
            "cancelled" => "İptal Edildi",
            "completed" => "Tamamlandı",
            "noshow" => "Gelinmedi",
            _ => status
        };
}
