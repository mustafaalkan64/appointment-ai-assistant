using System.Text.Json;
using AppointmentBooking.Application.Features.Appointments.CancelAppointment;
using AppointmentBooking.Application.Features.Appointments.ConfirmAppointment;
using AppointmentBooking.Application.Features.Appointments.CreateAppointment;
using AppointmentBooking.Application.Features.Appointments.GetAppointment;
using AppointmentBooking.Application.Features.Appointments.GetCustomerAppointments;
using AppointmentBooking.Application.Features.Availability.GetAvailableSlots;
using AppointmentBooking.Application.Features.Organizations.GetOrganization;
using AppointmentBooking.Application.Features.WaitingQueue.CancelQueueRequest;
using AppointmentBooking.Application.Features.WaitingQueue.GetQueueStatus;
using AppointmentBooking.McpServer.Protocol;
using MediatR;

namespace AppointmentBooking.McpServer.Services;

public sealed class McpToolRegistry
{
    private readonly ISender _sender;
    private readonly ILogger<McpToolRegistry> _logger;

    public McpToolRegistry(ISender sender, ILogger<McpToolRegistry> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public static IReadOnlyList<McpToolDefinition> GetTools()
    {
        return new List<McpToolDefinition>
        {
            new(
                "get_organization_info",
                "Retrieves organization details, working hours, and settings by its unique slug or GUID.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["slug"] = new("string", "The URL-friendly slug of the organization (e.g. 'kadikoy-poliklinigi')."),
                    ["organization_id"] = new("string", "The GUID of the organization (optional if slug is provided).")
                }, new List<string>())
            ),

            new(
                "get_available_dates",
                "Returns distinct calendar dates that have open appointment slots within a date range.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["start_date"] = new("string", "ISO-8601 start date (e.g. '2026-11-01')."),
                    ["end_date"] = new("string", "ISO-8601 end date (e.g. '2026-11-07').")
                }, new List<string> { "organization_id" })
            ),

            new(
                "get_available_slots",
                "Retrieves open appointment slots for a specific date or date range.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["start_date"] = new("string", "ISO-8601 start date (e.g. '2026-11-01T09:00:00Z')."),
                    ["end_date"] = new("string", "ISO-8601 end date (e.g. '2026-11-01T18:00:00Z').")
                }, new List<string> { "organization_id" })
            ),

            new(
                "create_appointment",
                "Books an appointment slot for a customer with distributed lock and idempotency protection.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["customer_id"] = new("string", "The customer GUID."),
                    ["slot_id"] = new("string", "The appointment slot GUID to book."),
                    ["idempotency_key"] = new("string", "Unique idempotency key preventing duplicate bookings."),
                    ["note"] = new("string", "Optional client note or reason for the appointment.")
                }, new List<string> { "organization_id", "customer_id", "slot_id", "idempotency_key" })
            ),

            new(
                "get_appointment",
                "Gets detailed information about an appointment by its ID.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["appointment_id"] = new("string", "The appointment GUID.")
                }, new List<string> { "organization_id", "appointment_id" })
            ),

            new(
                "get_customer_appointments",
                "Lists all appointments (upcoming and past) for a specific customer.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID (optional)."),
                    ["customer_id"] = new("string", "The customer GUID.")
                }, new List<string> { "customer_id" })
            ),

            new(
                "cancel_appointment",
                "Cancels an existing appointment, releases the slot, and advances waiting queue.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["appointment_id"] = new("string", "The appointment GUID to cancel (optional if customer has only one active appointment)."),
                    ["customer_id"] = new("string", "The customer GUID."),
                    ["reason"] = new("string", "The reason for cancellation (optional)."),
                    ["idempotency_key"] = new("string", "Unique idempotency key for this cancellation request (optional).")
                }, new List<string> { "organization_id", "customer_id" })
            ),

            new(
                "confirm_appointment",
                "Confirms a pending appointment for a customer.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["appointment_id"] = new("string", "The appointment GUID to confirm."),
                    ["customer_id"] = new("string", "The customer GUID."),
                    ["idempotency_key"] = new("string", "Unique idempotency key for this confirmation request (optional).")
                }, new List<string> { "organization_id", "appointment_id", "customer_id" })
            ),

            new(
                "get_queue_status",
                "Checks the position, status, and reservation expiration of a waiting queue entry.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["slot_id"] = new("string", "The slot GUID."),
                    ["customer_id"] = new("string", "The customer GUID (optional if request_id provided)."),
                    ["request_id"] = new("string", "The queue request identifier (optional if customer_id provided).")
                }, new List<string> { "organization_id", "slot_id" })
            ),

            new(
                "cancel_queue_request",
                "Removes a customer from the waiting queue.",
                new McpToolInputSchema("object", new Dictionary<string, McpToolPropertyDefinition>
                {
                    ["organization_id"] = new("string", "The organization GUID."),
                    ["slot_id"] = new("string", "The slot GUID."),
                    ["customer_id"] = new("string", "The customer GUID."),
                    ["request_id"] = new("string", "The queue request identifier.")
                }, new List<string> { "organization_id", "slot_id" })
            )
        };
    }

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement? arguments, CancellationToken ct)
    {
        _logger.LogInformation("Executing MCP Tool: {ToolName}", toolName);

        try
        {
            var args = arguments ?? JsonDocument.Parse("{}").RootElement;

            switch (toolName)
            {
                case "get_organization_info":
                {
                    var slug = args.TryGetProperty("slug", out var s) ? s.GetString() : null;
                    var orgIdStr = args.TryGetProperty("organization_id", out var o) ? o.GetString() : null;

                    object? result;
                    if (!string.IsNullOrWhiteSpace(slug))
                    {
                        result = await _sender.Send(new GetOrganizationQuery(null, slug), ct);
                    }
                    else if (Guid.TryParse(orgIdStr, out var orgId))
                    {
                        result = await _sender.Send(new GetOrganizationQuery(orgId, null), ct);
                    }
                    else
                    {
                        return FormatError("Either 'slug' or 'organization_id' must be specified.");
                    }

                    return FormatSuccess(result ?? new { });
                }

                case "get_available_dates":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    DateOnly startDate = args.TryGetProperty("start_date", out var s) && DateOnly.TryParse(s.GetString(), out var sd)
                        ? sd : DateOnly.FromDateTime(DateTime.UtcNow);
                    DateOnly endDate = args.TryGetProperty("end_date", out var e) && DateOnly.TryParse(e.GetString(), out var ed)
                        ? ed : startDate.AddDays(14);

                    var slotsResponse = await _sender.Send(new GetAvailableSlotsQuery(orgId, null, startDate, endDate), ct);
                    var dates = slotsResponse.Slots
                        .Select(x => x.StartUtc.ToString("yyyy-MM-dd"))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();

                    return FormatSuccess(new { OrganizationId = orgId, AvailableDates = dates });
                }

                case "get_available_slots":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    DateOnly startDate = args.TryGetProperty("start_date", out var s) && DateOnly.TryParse(s.GetString(), out var sd)
                        ? sd : DateOnly.FromDateTime(DateTime.UtcNow);
                    DateOnly endDate = args.TryGetProperty("end_date", out var e) && DateOnly.TryParse(e.GetString(), out var ed)
                        ? ed : startDate.AddDays(7);

                    var slotsResponse = await _sender.Send(new GetAvailableSlotsQuery(orgId, null, startDate, endDate), ct);
                    return FormatSuccess(slotsResponse);
                }

                case "create_appointment":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    var custId = Guid.Parse(args.GetProperty("customer_id").GetString()!);
                    var slotId = Guid.Parse(args.GetProperty("slot_id").GetString()!);
                    var idemKey = args.GetProperty("idempotency_key").GetString()!;
                    var note = args.TryGetProperty("note", out var n) ? n.GetString() : null;

                    var command = new CreateAppointmentCommand(orgId, custId, slotId, idemKey, note);
                    var result = await _sender.Send(command, ct);
                    return FormatSuccess(result);
                }

                case "get_appointment":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    var apptId = Guid.Parse(args.GetProperty("appointment_id").GetString()!);

                    var result = await _sender.Send(new GetAppointmentQuery(orgId, apptId), ct);
                    return FormatSuccess(result ?? new object());
                }

                case "get_customer_appointments":
                {
                    var custId = Guid.Parse(args.GetProperty("customer_id").GetString()!);
                    Guid? orgId = args.TryGetProperty("organization_id", out var o) && Guid.TryParse(o.GetString(), out var parsedOrgId)
                        ? parsedOrgId
                        : null;

                    var result = await _sender.Send(new GetCustomerAppointmentsQuery(CustomerId: custId, OrganizationId: orgId), ct);
                    return FormatSuccess(result ?? new object());
                }

                case "cancel_appointment":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    var custId = Guid.Parse(args.GetProperty("customer_id").GetString()!);

                    Guid apptId = Guid.Empty;
                    if (args.TryGetProperty("appointment_id", out var a) && Guid.TryParse(a.GetString(), out var parsedApptId) && parsedApptId != Guid.Empty)
                    {
                        apptId = parsedApptId;
                    }
                    else
                    {
                        // Auto-resolve active appointment if appointment_id was omitted
                        var customerAppointments = await _sender.Send(new GetCustomerAppointmentsQuery(CustomerId: custId, OrganizationId: orgId), ct);
                        var activeAppointments = customerAppointments
                            .Where(x => !string.Equals(x.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(x.Status, "NoShow", StringComparison.OrdinalIgnoreCase) &&
                                        x.StartUtc > DateTimeOffset.UtcNow)
                            .ToList();

                        if (activeAppointments.Count == 1)
                        {
                            apptId = activeAppointments[0].Id;
                        }
                        else if (activeAppointments.Count == 0)
                        {
                            return FormatError("İptal edilecek aktif bir randevunuz bulunmamaktadır.");
                        }
                        else
                        {
                            var apptList = string.Join(", ", activeAppointments.Select(x => $"{x.AppointmentNumber} ({x.LocalDisplay})"));
                            return FormatError($"Birden fazla aktif randevunuz bulunmaktadır ({apptList}). Lütfen iptal etmek istediğiniz randevunun numarasını belirtiniz.");
                        }
                    }

                    var reason = args.TryGetProperty("reason", out var r) && !string.IsNullOrWhiteSpace(r.GetString())
                        ? r.GetString()!
                        : "Müşteri iptal talebi";

                    var idemKey = args.TryGetProperty("idempotency_key", out var ik) && !string.IsNullOrWhiteSpace(ik.GetString())
                        ? ik.GetString()!
                        : $"cancel-{apptId:N}-{DateTime.UtcNow.Ticks}";

                    var command = new CancelAppointmentCommand(orgId, apptId, custId, reason, idemKey);
                    var result = await _sender.Send(command, ct);
                    return FormatSuccess(result);
                }

                case "confirm_appointment":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    var apptId = Guid.Parse(args.GetProperty("appointment_id").GetString()!);
                    var custId = Guid.Parse(args.GetProperty("customer_id").GetString()!);
                    var idemKey = args.TryGetProperty("idempotency_key", out var k) && !string.IsNullOrWhiteSpace(k.GetString())
                        ? k.GetString()!
                        : Guid.NewGuid().ToString("N");

                    var command = new ConfirmAppointmentCommand(orgId, apptId, custId, idemKey);
                    var result = await _sender.Send(command, ct);
                    return FormatSuccess(result);
                }

                case "get_queue_status":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    var slotId = Guid.Parse(args.GetProperty("slot_id").GetString()!);
                    Guid? custId = args.TryGetProperty("customer_id", out var c) && Guid.TryParse(c.GetString(), out var cid) ? cid : null;
                    string? reqId = args.TryGetProperty("request_id", out var r) ? r.GetString() : null;

                    var query = new GetQueueStatusQuery(orgId, slotId, custId, reqId);
                    var result = await _sender.Send(query, ct);
                    return FormatSuccess(result);
                }

                case "cancel_queue_request":
                {
                    var orgId = Guid.Parse(args.GetProperty("organization_id").GetString()!);
                    var slotId = Guid.Parse(args.GetProperty("slot_id").GetString()!);
                    Guid? custId = args.TryGetProperty("customer_id", out var c) && Guid.TryParse(c.GetString(), out var cid) ? cid : null;
                    string? reqId = args.TryGetProperty("request_id", out var r) ? r.GetString() : null;

                    var command = new CancelQueueRequestCommand(orgId, slotId, custId, reqId);
                    var result = await _sender.Send(command, ct);
                    return FormatSuccess(result);
                }

                default:
                    return FormatError($"Unknown tool: '{toolName}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while executing MCP tool {ToolName}.", toolName);
            return FormatError($"Execution failed: {ex.Message}");
        }
    }

    private static McpToolResult FormatSuccess(object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return new McpToolResult(new List<McpContentItem>
        {
            new("text", json)
        }, IsError: false);
    }

    private static McpToolResult FormatError(string errorMessage)
    {
        return new McpToolResult(new List<McpContentItem>
        {
            new("text", errorMessage)
        }, IsError: true);
    }
}
