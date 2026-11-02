# AI Chat UI Architecture
# Conversational Booking Experience

## 1. Primary Customer Experience Principle

The AI Chat interface is the primary interaction surface for customers. Traditional datepickers, complex step-forms, and dense schedule tables are superseded by a natural language assistant that validates real-time availability against the backend and renders structured, interactive cards.

---

## 2. Component Pipeline

```text
User Message / Quick Prompt Pill
              │
              ▼
      ChatComposer.tsx
              │
              ▼
      ChatContainer.tsx  <────> Chat API (POST /api/chat/messages)
              │                     │
              ▼                     ▼
     ChatMessageBubble.tsx      AgentResponseDto
              │
      ┌───────┴───────────────────────────────┐
      ▼                                       ▼
Text Response                   Structured Action Cards
(Markdown)                     ├─ AvailableSlotCards.tsx
                               ├─ AppointmentConfirmationCard.tsx
                               ├─ AppointmentSuccessCard.tsx
                               ├─ AppointmentCancellationCard.tsx
                               └─ WaitingQueueCard.tsx
```

---

## 3. Discriminated Message & Card Contracts

```typescript
export type ChatCardType =
  | "text"
  | "appointment_slots"
  | "appointment_confirmation"
  | "appointment_success"
  | "appointment_cancelled"
  | "waiting_queue"
  | "tool_execution"
  | "error";
```

### Response Processing Rules:
1. **Never Parse Business State from Freeform Text**: When `response.requiresConfirmation` is true or `response.proposedAction` is present, the card enters the explicit confirmation state with the proposed slot.
2. **Interactive Slot Picking**: Extracted or provided slot times are rendered as 1-click selectable buttons. Clicking a slot initiates selection without requiring manual typing.
3. **Explicit Confirmation**: The user must explicitly click "Evet, Onaylıyorum" before a slot is booked. The frontend sends confirmation to the Chat API and does not display success until confirmed by the server.
4. **Rescheduling & Cancellation Safety**: When a cancellation or rescheduling is initiated, the original appointment remains intact until explicit confirmation is returned from the backend.

---

## 4. Mobile Ergonomics

- **Dedicated Viewport**: On `/app/chat` and `/app/chat/[slug]`, the mobile bottom navigation bar is automatically hidden so it never overlaps the fixed composer.
- **Auto-scroll**: A mutation observer and `useEffect` hook automatically scroll new assistant messages and cards into view (`messagesEndRef.current.scrollIntoView({ behavior: 'smooth' })`).
- **Dynamic Status**: While the backend processes tool calls, the chat renders animated tool execution state (`"Uygun saatler kontrol ediliyor..."`, `"Randevu kaydediliyor ve onaylanıyor..."`).
