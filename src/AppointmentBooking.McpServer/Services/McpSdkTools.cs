using System.ComponentModel;
using System.Text.Json;
using System;
using AppointmentBooking.McpServer.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AppointmentBooking.McpServer;

[McpServerToolType]
public sealed class McpSdkTools(McpToolRegistry registry)
{
    [McpServerTool(Name = "get_organization_info", ReadOnly = true, OpenWorld = false)]
    [Description("Kurum adı, adresi, iletişim bilgileri, saat dilimi, çalışma saatleri ve randevu ayarlarını getirir. Kullanıcı kurum bilgisi, konum, iletişim veya çalışma saatlerini sorarsa kullan. organization_id ya da slug değerlerinden en az biri gerekir; aktif kurum bağlamı varsa organization_id kullan.")]
    public Task<string> GetOrganizationInfo(
        [Description("Kurumun URL uyumlu slug değeri. organization_id verilmediyse kullan.")] string? slug = null,
        [Description("Kurum GUID'i; aktif kurum bağlamı varsa onu kullan.")] string? organization_id = null,
        CancellationToken cancellationToken = default) =>
        Execute("get_organization_info", new { slug, organization_id }, cancellationToken);

    [McpServerTool(Name = "get_available_dates", ReadOnly = true, OpenWorld = false)]
    [Description("Belirtilen kurum ve tarih aralığında en az bir boş randevu slotu bulunan günleri döndürür. Kullanıcı uygun günleri soruyor ancak saatleri istemiyorsa tercih et; saat/slot seçimi için get_available_slots kullan.")]
    public Task<string> GetAvailableDates(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Başlangıç tarihi (YYYY-MM-DD). Boş bırakılırsa bugün kullanılır.")] string? start_date = null,
        [Description("Bitiş tarihi (YYYY-MM-DD). Boş bırakılırsa kısa bir varsayılan aralık kullanılır.")] string? end_date = null,
        CancellationToken cancellationToken = default) =>
        Execute("get_available_dates", new { organization_id, start_date, end_date }, cancellationToken);

    [McpServerTool(Name = "get_available_slots", ReadOnly = true, OpenWorld = false)]
    [Description("Belirtilen kurum ve tarih aralığındaki gerçekten boş randevu slotlarını slot_id değerleriyle döndürür. Kullanıcı müsait saat sorarsa veya randevu almak istiyorsa önce çağır; randevu oluştururken sonuçtaki slot_id'yi kullan ve slot_id uydurma.")]
    public Task<string> GetAvailableSlots(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Başlangıç tarihi (YYYY-MM-DD).")] string? start_date = null,
        [Description("Bitiş tarihi (YYYY-MM-DD).")] string? end_date = null,
        CancellationToken cancellationToken = default) =>
        Execute("get_available_slots", new { organization_id, start_date, end_date }, cancellationToken);

    [McpServerTool(Name = "create_appointment", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Seçilen boş slotta müşteri adına kalıcı randevu oluşturur. Yalnızca kullanıcı açıkça randevu oluşturmayı/onaylamayı istediğinde çağır; önce get_available_slots ile gerçek slot_id edin. Her mantıksal rezervasyon için sabit benzersiz idempotency_key kullan.")]
    public Task<string> CreateAppointment(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Müşteri GUID'i.")] string customer_id,
        [Description("get_available_slots sonucundan alınmış gerçek slot GUID'i.")] string slot_id,
        [Description("Yeniden denemelerde aynı kalması gereken benzersiz işlem anahtarı.")] string idempotency_key,
        [Description("Randevu için isteğe bağlı not.")] string? note = null,
        CancellationToken cancellationToken = default) =>
        Execute("create_appointment", new { organization_id, customer_id, slot_id, idempotency_key, note }, cancellationToken);

    [McpServerTool(Name = "get_appointment", ReadOnly = true, OpenWorld = false)]
    [Description("appointment_id ile tek bir randevunun durum ve zaman dahil ayrıntılarını getirir.")]
    public Task<string> GetAppointment(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Randevu GUID'i.")] string appointment_id,
        CancellationToken cancellationToken) =>
        Execute("get_appointment", new { organization_id, appointment_id }, cancellationToken);

    [McpServerTool(Name = "get_customer_appointments", ReadOnly = true, OpenWorld = false)]
    [Description("Müşterinin yaklaşan ve geçmiş randevularını listeler. İptal/onay hedefi belirsizse önce bu aracı kullanıp kullanıcıdan hedefi netleştir.")]
    public Task<string> GetCustomerAppointments(
        [Description("Müşteri GUID'i.")] string customer_id,
        [Description("Kurum GUID'i; verilmezse müşterinin tüm kurum randevuları aranır.")] string? organization_id = null,
        CancellationToken cancellationToken = default) =>
        Execute("get_customer_appointments", new { organization_id, customer_id }, cancellationToken);

    [McpServerTool(Name = "cancel_appointment", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Bir randevuyu iptal eder. Kalıcı işlemdir; yalnızca kullanıcının açık talebiyle çağır. Hedef belirsizse önce get_customer_appointments çağır ve netleştir.")]
    public Task<string> CancelAppointment(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Müşteri GUID'i.")] string customer_id,
        [Description("İptal edilecek randevu GUID'i; yalnızca tek aktif randevu olduğunda atlanabilir.")] string? appointment_id = null,
        [Description("İptal nedeni.")] string? reason = null,
        [Description("Yeniden denemelerde aynı kalması gereken benzersiz işlem anahtarı.")] string? idempotency_key = null,
        CancellationToken cancellationToken = default) =>
        Execute("cancel_appointment", new { organization_id, customer_id, appointment_id, reason, idempotency_key }, cancellationToken);

    [McpServerTool(Name = "confirm_appointment", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Mevcut bekleyen bir randevuyu onaylar. Yeni randevu oluşturmak için kullanma; kullanıcı açıkça onaylamalıdır.")]
    public Task<string> ConfirmAppointment(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Onaylanacak randevu GUID'i.")] string appointment_id,
        [Description("Müşteri GUID'i.")] string customer_id,
        [Description("Benzersiz işlem anahtarı.")] string? idempotency_key = null,
        CancellationToken cancellationToken = default) =>
        Execute("confirm_appointment", new { organization_id, appointment_id, customer_id, idempotency_key }, cancellationToken);

    [McpServerTool(Name = "get_queue_status", ReadOnly = true, OpenWorld = false)]
    [Description("Belirli bir dolu slot için müşterinin bekleme sırası konumunu ve durumunu sorgular.")]
    public Task<string> GetQueueStatus(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Slot GUID'i.")] string slot_id,
        [Description("Müşteri GUID'i veya request_id alanlarından biri.")] string? customer_id = null,
        [Description("Bekleme kuyruğu talep kimliği.")] string? request_id = null,
        CancellationToken cancellationToken = default) =>
        Execute("get_queue_status", new { organization_id, slot_id, customer_id, request_id }, cancellationToken);

    [McpServerTool(Name = "cancel_queue_request", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Müşterinin belirli slot için bekleme kuyruğu kaydını siler. Kalıcı işlemdir; kullanıcının açık isteği gerekir.")]
    public Task<string> CancelQueueRequest(
        [Description("Kurum GUID'i.")] string organization_id,
        [Description("Slot GUID'i.")] string slot_id,
        [Description("Müşteri GUID'i.")] string? customer_id = null,
        [Description("Bekleme kuyruğu talep kimliği.")] string? request_id = null,
        CancellationToken cancellationToken = default) =>
        Execute("cancel_queue_request", new { organization_id, slot_id, customer_id, request_id }, cancellationToken);

    private async Task<string> Execute(string name, object arguments, CancellationToken cancellationToken)
    {
        var element = JsonSerializer.SerializeToElement(arguments);
        var result = await registry.ExecuteToolAsync(name, element, cancellationToken);
        var text = result.Content.FirstOrDefault()?.Text ?? string.Empty;
        if (result.IsError)
            throw new McpException(text);
        return text;
    }
}

[McpServerResourceType]
public sealed class AppointmentResources
{
    [McpServerResource(UriTemplate = "appointment://system/booking-guidelines")]
    [Description("Slot doğrulama, kullanıcı teyidi ve güvenli rezervasyon/iptal ilkeleri.")]
    public static string BookingGuidelines() => """
        Önce gerçek müsaitliği sorgula ve yalnızca sorgu sonucunda dönen slot_id'yi kullan.
        Randevu oluşturma, iptal, onay ve kuyruktan çıkma kalıcı işlemlerdir; açık kullanıcı isteği/teyidi olmadan yapma.
        Hedef randevu belirsizse önce randevuları listele ve kullanıcıdan netleştirmesini iste.
        Yalnızca araç sonuçlarında bulunan bilgileri aktar; başarı veya müsaitlik uydurma.
        """;

    [McpServerResource(UriTemplate = "appointment://system/tool-selection")]
    [Description("Tool seçiminde niyet ve input schema kullanımı için rehber.")]
    public static string ToolSelection() => """
        Araçları kullanıcı isteğine, araç açıklamalarına ve parametre şemalarına göre seç.
        Gerekli değerleri bağlamdan sağla; eksik olanları kullanıcıdan sor.
        Önce sorgula, sonra açık kullanıcı isteğiyle gerekli yazma işlemini gerçekleştir.
        """;
}

[McpServerPromptType]
public sealed class AppointmentPrompts
{
    [McpServerPrompt(Name = "find_availability")]
    [Description("Kullanıcının belirttiği tarih aralığında uygun randevu gün ve saatlerini bul.")]
    public static string FindAvailability(
        [Description("İstenen tarih veya tarih aralığı.")] string date_range,
        [Description("Aktif kurum bağlamı yoksa kurum GUID'i.")] string? organization_id = null) =>
        $"Aşağıdaki isteğe göre gerçek müsait gün ve saatleri bul: {date_range}. Kurum: {organization_id ?? "aktif kurum bağlamı"}. Araç açıklamalarına göre seçim yap.";

    [McpServerPrompt(Name = "book_appointment")]
    [Description("Gerçek müsaitliği kontrol edip seçilen slot için kullanıcı teyidiyle randevu oluştur.")]
    public static string BookAppointment(
        [Description("Kullanıcının istediği tarih/saat.")] string requested_time,
        [Description("Aktif kurum bağlamı yoksa kurum GUID'i.")] string? organization_id = null,
        [Description("Aktif müşteri bağlamı yoksa müşteri GUID'i.")] string? customer_id = null) =>
        $"Randevu isteği: {requested_time}. Kurum: {organization_id ?? "aktif kurum bağlamı"}. Müşteri: {customer_id ?? "aktif müşteri bağlamı"}. Gerçek slot seçilmeden rezervasyon oluşturma.";

    [McpServerPrompt(Name = "manage_appointments")]
    [Description("Müşterinin randevularını listele veya belirtilen randevuyu yönet.")]
    public static string ManageAppointments(
        [Description("Kullanıcının randevu yönetimi isteği.")] string request,
        [Description("Aktif müşteri bağlamı yoksa müşteri GUID'i.")] string? customer_id = null) =>
        $"Randevu isteği: {request}. Müşteri: {customer_id ?? "aktif müşteri bağlamı"}. Hedef belirsizse kalıcı işlem yapma; önce listele ve netleştir.";
}
