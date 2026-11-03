// ==========================================
// Organization & Availability Types
// ==========================================

export interface WeeklyAvailabilityDto {
  dayOfWeek: number;
  dayName: string;
  startTime: string; // "09:00:00"
  endTime: string;   // "17:00:00"
  slotDurationMinutes: number;
  isActive: boolean;
}

export interface Organization {
  id: string;
  name: string;
  slug: string;
  email: string;
  phoneNumber: string;
  address: string;
  timeZone?: string;
  timeZoneId?: string;
  isActive: boolean;
  weeklyAvailabilities?: WeeklyAvailabilityDto[];
  description?: string;
  category?: string;
}

export interface AppointmentSlotDto {
  id: string;
  startUtc: string;
  endUtc: string;
  localStartTime: string;
  localEndTime: string;
  localDisplay: string;
  status: SlotStatus;
  isActive?: boolean;
  customerName?: string;
  appointmentNumber?: string;
}

export enum SlotStatus {
  Available = 1,
  Reserved = 2,
  Booked = 3,
  Blocked = 4,
  Cancelled = 5
}

export interface GetAvailableSlotsResponse {
  organizationId: string;
  timeZone: string;
  slots: AppointmentSlotDto[];
}

export interface SetWeeklyAvailabilityCommand {
  organizationId: string;
  schedules: {
    dayOfWeek: number;
    startTime: string;
    endTime: string;
    slotDurationMinutes: number;
  }[];
}

export interface GenerateSlotsCommand {
  organizationId: string;
  startDate: string; // "YYYY-MM-DD"
  endDate: string;   // "YYYY-MM-DD"
  skipExisting?: boolean;
}

export interface GenerateSlotsResponse {
  organizationId: string;
  totalCreatedSlots: number;
  startDate: string;
  endDate: string;
}

// ==========================================
// Appointment Types
// ==========================================

export enum AppointmentStatus {
  Pending = 1,
  Confirmed = 2,
  Completed = 3,
  Cancelled = 4,
  NoShow = 5
}

export interface AppointmentDetailDto {
  id: string;
  organizationId: string;
  organizationName?: string;
  customerId: string;
  customerName?: string;
  customerPhone?: string;
  customerEmail?: string;
  slotId: string;
  appointmentNumber: string;
  startUtc: string;
  endUtc: string;
  localDisplay: string;
  status: AppointmentStatus | string;
  description?: string | null;
  createdAt: string;
  cancelledAt?: string | null;
  cancellationReason?: string | null;
}

export interface CreateAppointmentCommand {
  organizationId: string;
  customerId: string;
  slotId: string;
  idempotencyKey: string;
  note?: string;
}

export interface CreateAppointmentResult {
  appointmentId?: string;
  appointmentNumber?: string;
  queueEntryId?: string;
  queuePosition?: number;
  status: string;
  message?: string;
}

export interface CancelAppointmentCommand {
  organizationId: string;
  appointmentId: string;
  customerId: string;
  reason?: string;
  idempotencyKey: string;
}

// ==========================================
// Waiting Queue Types
// ==========================================

export enum QueueStatus {
  Waiting = 1,
  Processing = 2,
  Reserved = 3,
  Completed = 4,
  Failed = 5,
  Expired = 6,
  Cancelled = 7
}

export interface QueueStatusResponse {
  queueEntryId: string;
  slotId: string;
  customerId: string;
  position: number;
  totalInQueue: number;
  status: string;
  reservationExpiresAt?: string | null;
  estimatedWaitMinutes?: number | null;
  createdAt: string;
}

// ==========================================
// Customer Types
// ==========================================

export interface Customer {
  id: string;
  fullName: string;
  phoneNumber: string;
  email: string;
  externalCustomerId?: string;
  isPhoneVerified: boolean;
  createdAt: string;
  appointmentCount?: number;
  lastAppointmentAt?: string;
}

export interface RegisterCustomerCommand {
  organizationId?: string;
  fullName: string;
  phoneNumber: string;
  email: string;
}

// ==========================================
// Auth & User Types
// ==========================================

export type UserRole = "SuperAdmin" | "OrganizationAdmin" | "OrganizationStaff" | "Customer";

export interface UserSession {
  id: string;
  email: string;
  fullName: string;
  role: UserRole;
  organizationId?: string;
  organizationSlug?: string;
  token?: string;
}

// ==========================================
// Primary AI Chat Response Contract
// ==========================================

export interface AgentResponseDto {
  conversationId: string;
  messageId: string;
  role: "user" | "assistant" | "system";
  content: string;
  requiresConfirmation: boolean;
  proposedAction?: "create_appointment" | "cancel_appointment" | "join_queue" | null;
  summary?: string | null;
  isCompleted: boolean;
}

export type ChatCardType =
  | "text"
  | "appointment_slots"
  | "appointment_confirmation"
  | "appointment_success"
  | "appointment_cancelled"
  | "waiting_queue"
  | "tool_execution"
  | "error";

export interface ChatMessage {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  createdAt: string;
  cardType?: ChatCardType;
  requiresConfirmation?: boolean;
  proposedAction?: string | null;
  summary?: string | null;
  isCompleted?: boolean;
  slotsData?: AppointmentSlotDto[];
  confirmationData?: {
    organizationId: string;
    organizationName?: string;
    slotId?: string;
    localDisplay?: string;
    date?: string;
    time?: string;
    note?: string;
    customerName?: string;
  };
  successData?: {
    appointmentId?: string;
    appointmentNumber?: string;
    localDisplay?: string;
  };
  queueData?: {
    position?: number;
    localDisplay?: string;
  };
}

export interface ConversationHistoryDto {
  id: string;
  organizationId: string;
  customerId: string;
  status: string;
  createdAt: string;
  messages: {
    id: string;
    role: "user" | "assistant";
    content: string;
    intent?: string | null;
    createdAt: string;
  }[];
}

// ==========================================
// Admin Dashboard Analytics Types
// ==========================================

export interface DashboardStats {
  totalAppointmentsToday: number;
  upcomingAppointments: number;
  completedAppointments: number;
  cancelledAppointments: number;
  availableSlotsCount: number;
  waitingQueueCount: number;
  bookingConversionRate: number;
  dailyTrends: { date: string; bookings: number; cancellations: number }[];
}
