# Frontend Architecture Documentation
# AI-Powered Appointment Booking Platform

## 1. Executive Summary

The frontend is built using **Next.js 16 (App Router)** with React 19, TypeScript strict mode, and Tailwind CSS v4. It delivers two distinct user experiences sharing a unified design token system:
1. **Organization Admin Dashboard** (`/admin/*`) – High-density administrative workspace for schedule configuration, slot generation, capacity toggles, customer management, and waiting queues.
2. **Customer Application** (`/app/*`) – Mobile-first, conversational AI-first interface where customers discover organizations, check real-time availability, and complete full appointment lifecycles via natural language chat.

---

## 2. Directory Structure

```text
frontend/src/
├── app/
│   ├── (public)/                 # Public discovery & SaaS marketing
│   │   ├── layout.tsx            # Public sticky navbar & footer
│   │   ├── page.tsx              # High-conversion SaaS landing page
│   │   ├── organizations/        # Organization directory & search
│   │   └── organization/[slug]/  # Organization public profile
│   ├── (auth)/                   # Authentication flows
│   │   ├── layout.tsx            # Clean auth card container
│   │   ├── login/page.tsx        # Customer & Admin 1-click login
│   │   └── register/page.tsx     # Customer & Organization onboarding
│   ├── (admin)/                  # Administrative workspace
│   │   └── admin/
│   │       ├── layout.tsx        # Collapsible sidebar, mobile drawer, org pill
│   │       ├── dashboard/        # KPIs, Recharts booking activity, quick actions
│   │       ├── appointments/     # Data table, search, filters, cancel dialog
│   │       ├── calendar/         # Day/Week calendar, slot lock/unlock toggles
│   │       ├── availability/     # Weekly schedule editor & batch slot generator
│   │       ├── customers/        # Customer directory & phone verification
│   │       ├── waiting-queue/    # Real-time queue positions & cancellation
│   │       └── settings/         # Organization profile, timezone, team members
│   ├── (customer)/               # Customer conversational portal
│   │   └── app/
│   │       ├── layout.tsx        # Top-nav on desktop, bottom-nav on mobile
│   │       ├── home/             # Customer home & quick discovery
│   │       ├── chat/             # Primary Conversational AI Experience
│   │       │   ├── page.tsx      # General AI booking chat
│   │       │   └── [slug]/       # Organization-specific conversational booking
│   │       ├── my-appointments/  # Status tabs, details, cancellation dialog
│   │       ├── organizations/    # Customer org discovery
│   │       └── profile/          # Profile form & session switcher
├── components/
│   ├── ui/                       # Design system primitives (Button, Card, Input, Badge, Dialog, Tabs)
│   ├── chat/                     # Specialized conversational AI components
│   │   ├── ChatContainer.tsx     # Full conversation state orchestrator
│   │   ├── ChatHeader.tsx        # Organization context & online status
│   │   ├── ChatSidebar.tsx       # Conversation history & org context
│   │   ├── ChatMessageBubble.tsx # Typed cards renderer (Slots, Confirmation, Success)
│   │   ├── ChatComposer.tsx      # Fixed safe-area text composer with quick prompts
│   │   ├── ChatEmptyState.tsx    # Welcoming screen with suggested prompts
│   │   ├── AvailableSlotCards.tsx# Interactive 1-click slot picker
│   │   ├── AppointmentConfirmationCard.tsx # Explicit Confirm/Change/Cancel actions
│   │   ├── AppointmentSuccessCard.tsx      # Confirmed badge, ICS download, details
│   │   ├── AppointmentCancellationCard.tsx # Explicit cancellation confirmations
│   │   ├── WaitingQueueCard.tsx  # Queue position & status tracker
│   │   └── TypingIndicator.tsx   # Real-time tool execution state
├── lib/
│   ├── api/client.ts             # Modular typed API clients (org, appt, cust, chat)
│   └── utils.ts                  # Tailwind clsx/merge & date-fns helpers
├── providers/
│   └── providers.tsx             # React Query, next-themes, Sonner, AuthContext
└── types/
    └── index.ts                  # Comprehensive TypeScript contracts & discriminated unions
```

---

## 3. Technology Highlights

- **Turbopack & Partial Prerendering (PPR)**: Blazing fast compilation with all 21 routes prerendered cleanly.
- **TanStack Query (v5)**: Automated background refetching, cache invalidation on mutations, and optimistic loading states.
- **Tailwind CSS v4 with CSS Variables**: Semantic color tokens (`--primary`, `--surface`, `--border`, `--muted`) seamlessly adapting between Light and Dark mode.
- **Lucide React**: Crisp, modern icon set consistent across all screens.
- **Sonner**: Non-blocking toast notifications for system feedback.
- **date-fns**: Standardized localized dates (`tr-TR`).
