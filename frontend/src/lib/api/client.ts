import {
  Organization,
  GetAvailableSlotsResponse,
  SetWeeklyAvailabilityCommand,
  GenerateSlotsCommand,
  GenerateSlotsResponse,
  CreateAppointmentCommand,
  CreateAppointmentResult,
  CancelAppointmentCommand,
  AppointmentDetailDto,
  Customer,
  RegisterCustomerCommand,
  QueueStatusResponse,
  AgentResponseDto,
  ConversationHistoryDto,
} from "@/types";

const CORE_API_URL = process.env.NEXT_PUBLIC_API_BASE_URL || "http://localhost:5099";
const CHAT_API_URL = process.env.NEXT_PUBLIC_CHAT_API_URL || "http://localhost:5111";

export class ApiError extends Error {
  constructor(public status: number, message: string, public details?: unknown) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(baseUrl: string, endpoint: string, options: RequestInit = {}): Promise<T> {
  const url = `${baseUrl}${endpoint}`;
  const headers = new Headers(options.headers || {});

  if (!headers.has("Content-Type") && options.body && typeof options.body === "string") {
    headers.set("Content-Type", "application/json");
  }

  // Inject session token if stored in browser
  if (typeof window !== "undefined") {
    const sessionStr = localStorage.getItem("ab_session");
    if (sessionStr) {
      try {
        const session = JSON.parse(sessionStr);
        if (session.token) {
          headers.set("Authorization", `Bearer ${session.token}`);
        }
        if (session.organizationId) {
          headers.set("X-Organization-Id", session.organizationId);
        }
      } catch {
        // Ignore invalid session json
      }
    }
  }

  const response = await fetch(url, {
    ...options,
    headers,
  });

  if (!response.ok) {
    let errorMsg = `HTTP Error ${response.status}: ${response.statusText}`;
    let details: unknown;
    try {
      const errorJson = await response.json();
      details = errorJson;
      errorMsg = errorJson.detail || errorJson.error || errorJson.message || errorJson.title || errorMsg;
    } catch {
      // Body is not json
    }
    throw new ApiError(response.status, errorMsg, details);
  }

  // Handle 204 No Content
  if (response.status === 204) {
    return {} as T;
  }

  return response.json();
}

// ==========================================
// Organization API Module
// ==========================================
export const organizationApi = {
  async getAll(): Promise<Organization[]> {
    try {
      return await request<Organization[]>(CORE_API_URL, "/api/organizations");
    } catch {
      try {
        const primary = await request<Organization>(CORE_API_URL, "/api/organizations/by-slug/conc-org-6f88d9d7");
        return [primary];
      } catch {
        return [];
      }
    }
  },

  async getById(id: string): Promise<Organization> {
    return request<Organization>(CORE_API_URL, `/api/organizations/${id}`);
  },

  async getBySlug(slug: string): Promise<Organization> {
    return request<Organization>(CORE_API_URL, `/api/organizations/by-slug/${slug}`);
  },

  async update(id: string, data: {
    name: string;
    email: string;
    phoneNumber: string;
    address: string;
    timeZone?: string;
  }): Promise<Organization> {
    return request<Organization>(CORE_API_URL, `/api/organizations/${id}`, {
      method: "PUT",
      body: JSON.stringify(data),
    });
  },

  async register(data: {
    name: string;
    slug: string;
    email: string;
    phoneNumber: string;
    address: string;
    timeZone?: string;
    adminEmail: string;
    adminPassword: string;
    adminFullName: string;
  }): Promise<{ id: string; name: string; slug: string }> {
    return request(CORE_API_URL, "/api/organizations", {
      method: "POST",
      body: JSON.stringify(data),
    });
  },

  async setWeeklyAvailability(command: any): Promise<{ organizationId: string; updatedDaysCount: number }> {
    const rawDays = command.days || command.schedules || [];
    const formattedDays = rawDays.map((d: any) => ({
      dayOfWeek: d.dayOfWeek,
      startTime: typeof d.startTime === "string" && d.startTime.length === 5 ? d.startTime + ":00" : d.startTime,
      endTime: typeof d.endTime === "string" && d.endTime.length === 5 ? d.endTime + ":00" : d.endTime,
      slotDurationMinutes: d.slotDurationMinutes,
      isClosed: d.isClosed ?? (d.isActive !== undefined ? !d.isActive : false),
    }));

    return request(CORE_API_URL, `/api/organizations/${command.organizationId}/availability/weekly`, {
      method: "POST",
      body: JSON.stringify({
        organizationId: command.organizationId,
        days: formattedDays,
      }),
    });
  },

  async generateSlots(command: GenerateSlotsCommand): Promise<GenerateSlotsResponse> {
    return request<GenerateSlotsResponse>(
      CORE_API_URL,
      `/api/organizations/${command.organizationId}/slots/generate`,
      {
        method: "POST",
        body: JSON.stringify(command),
      }
    );
  },

  async toggleSlot(organizationId: string, slotId: string, isActive?: boolean, force = false): Promise<{ slotId: string; isActive: boolean; status?: string }> {
    return request(
      CORE_API_URL,
      `/api/organizations/${organizationId}/slots/${slotId}/status`,
      {
        method: "PUT",
        body: JSON.stringify({ isActive, force }),
      }
    );
  },

  async updateSlotStatus(organizationId: string, slotId: string, isActive: boolean, force = false): Promise<{ slotId: string; isActive: boolean; status?: string }> {
    return request(
      CORE_API_URL,
      `/api/organizations/${organizationId}/slots/${slotId}/status`,
      {
        method: "PUT",
        body: JSON.stringify({ isActive, force }),
      }
    );
  },
};

// ==========================================
// Appointment API Module
// ==========================================
export const appointmentApi = {
  async getAvailableSlots(
    organizationId: string,
    startDate?: string,
    endDate?: string,
    date?: string,
    includeAll?: boolean
  ): Promise<GetAvailableSlotsResponse> {
    const query = new URLSearchParams();
    if (date) query.append("date", date);
    if (startDate) query.append("startDate", startDate);
    if (endDate) query.append("endDate", endDate);
    if (includeAll) query.append("includeAll", "true");

    const qs = query.toString() ? `?${query.toString()}` : "";
    return request<GetAvailableSlotsResponse>(
      CORE_API_URL,
      `/api/organizations/${organizationId}/slots/available${qs}`
    );
  },

  async getOrganizationAppointments(organizationId: string): Promise<AppointmentDetailDto[]> {
    return request<AppointmentDetailDto[]>(
      CORE_API_URL,
      `/api/organizations/${organizationId}/appointments`
    );
  },

  async getCustomerAppointments(
    customerId: string,
    organizationId?: string,
    status?: number
  ): Promise<AppointmentDetailDto[]> {
    const query = new URLSearchParams();
    if (organizationId) query.append("organizationId", organizationId);
    if (status !== undefined) query.append("status", status.toString());

    const qs = query.toString() ? `?${query.toString()}` : "";
    return request<AppointmentDetailDto[]>(
      CORE_API_URL,
      `/api/customers/${customerId}/appointments${qs}`
    );
  },

  async getById(appointmentId: string): Promise<AppointmentDetailDto> {
    return request<AppointmentDetailDto>(CORE_API_URL, `/api/appointments/${appointmentId}`);
  },

  async create(command: CreateAppointmentCommand): Promise<CreateAppointmentResult> {
    return request<CreateAppointmentResult>(CORE_API_URL, "/api/appointments", {
      method: "POST",
      body: JSON.stringify(command),
    });
  },

  async cancel(command: CancelAppointmentCommand): Promise<{ success: boolean; message: string }> {
    return request(CORE_API_URL, `/api/appointments/${command.appointmentId}/cancel`, {
      method: "POST",
      body: JSON.stringify(command),
    });
  },
};

// ==========================================
// Customer API Module
// ==========================================
export const customerApi = {
  async register(command: RegisterCustomerCommand): Promise<Customer> {
    return request<Customer>(CORE_API_URL, "/api/customers", {
      method: "POST",
      body: JSON.stringify(command),
    });
  },

  async getById(customerId: string): Promise<Customer> {
    return request<Customer>(CORE_API_URL, `/api/customers/${customerId}`);
  },

  async getOrganizationCustomers(organizationId: string): Promise<Customer[]> {
    return request<Customer[]>(CORE_API_URL, `/api/organizations/${organizationId}/customers`);
  },
};

// ==========================================
// Waiting Queue API Module
// ==========================================
export const waitingQueueApi = {
  async getQueueStatus(
    organizationId: string,
    slotId: string,
    customerId?: string,
    requestId?: string
  ): Promise<QueueStatusResponse> {
    const query = new URLSearchParams({ organizationId, slotId });
    if (customerId) query.append("customerId", customerId);
    if (requestId) query.append("requestId", requestId);

    return request<QueueStatusResponse>(CORE_API_URL, `/api/queue/status?${query.toString()}`);
  },

  async cancel(data: {
    organizationId: string;
    slotId: string;
    customerId?: string;
    requestId?: string;
  }): Promise<{ success: boolean; message: string }> {
    return request(CORE_API_URL, "/api/queue/cancel", {
      method: "POST",
      body: JSON.stringify(data),
    });
  },
};

// ==========================================
// Chat API Module
// ==========================================
export const chatApi = {
  async startConversation(organizationId: string, customerId: string): Promise<{
    conversationId: string;
    organizationId: string;
    customerId: string;
    status: string;
    message: string;
  }> {
    return request(CHAT_API_URL, "/api/chat/conversations", {
      method: "POST",
      body: JSON.stringify({ organizationId, customerId }),
    });
  },

  async sendMessage(
    payload: {
      organizationId: string;
      customerId: string;
      message: string;
      conversationId?: string | null;
    } | string,
    customerId?: string,
    message?: string,
    conversationId?: string | null
  ): Promise<AgentResponseDto> {
    let bodyObj;
    if (typeof payload === "object") {
      bodyObj = payload;
    } else {
      bodyObj = {
        organizationId: payload,
        customerId: customerId!,
        message: message!,
        conversationId: conversationId || null,
      };
    }

    return request<AgentResponseDto>(CHAT_API_URL, "/api/chat/messages", {
      method: "POST",
      body: JSON.stringify(bodyObj),
    });
  },

  async getConversation(conversationId: string): Promise<ConversationHistoryDto> {
    return request<ConversationHistoryDto>(CHAT_API_URL, `/api/chat/conversations/${conversationId}`);
  },
};

// Backward-compatible alias
export const coreApi = {
  getOrganizations: organizationApi.getAll,
  getOrganizationById: organizationApi.getById,
  getOrganizationBySlug: organizationApi.getBySlug,
  updateOrganization: organizationApi.update,
  registerOrganization: organizationApi.register,
  getAvailableSlots: (orgId: string, params?: { date?: string; startDate?: string; endDate?: string }) =>
    appointmentApi.getAvailableSlots(orgId, params?.startDate, params?.endDate, params?.date),
  setWeeklyAvailability: organizationApi.setWeeklyAvailability,
  generateSlots: organizationApi.generateSlots,
  toggleSlotStatus: organizationApi.toggleSlot,
  createAppointment: appointmentApi.create,
  getAppointment: appointmentApi.getById,
  getCustomerAppointments: appointmentApi.getCustomerAppointments,
  cancelAppointment: (id: string, cmd: CancelAppointmentCommand) => appointmentApi.cancel(cmd),
  registerCustomer: customerApi.register,
  getQueueStatus: waitingQueueApi.getQueueStatus,
  cancelQueue: waitingQueueApi.cancel,
};
