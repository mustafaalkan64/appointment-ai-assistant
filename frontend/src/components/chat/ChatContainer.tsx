"use client";

import React, { useState, useEffect, useRef } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/providers/providers";
import { appointmentApi, chatApi, organizationApi } from "@/lib/api/client";
import { ChatMessage, AgentResponseDto, Organization } from "@/types";
import { ChatHeader } from "./ChatHeader";
import { ChatSidebar, ConversationMeta } from "./ChatSidebar";
import { ChatMessageBubble } from "./ChatMessageBubble";
import { ChatComposer } from "./ChatComposer";
import { ChatEmptyState } from "./ChatEmptyState";
import { TypingIndicator } from "./TypingIndicator";
import { toast } from "sonner";

// Extracts "dd.MM.yyyy HH:mm - HH:mm" (slot LocalDisplay) from backend text
function parseSlotDisplay(text: string): { date?: string; time?: string; localDisplay?: string } {
  const m = text.match(/(\d{2}\.\d{2}\.\d{4})\s+(\d{2}:\d{2})(?:\s*-\s*(\d{2}:\d{2}))?/);
  if (!m) return {};
  return {
    date: m[1],
    time: m[3] ? `${m[2]} - ${m[3]}` : m[2],
    localDisplay: m[0],
  };
}

interface ChatContainerProps {
  initialOrgSlug?: string;
}

export function ChatContainer({ initialOrgSlug }: ChatContainerProps) {
  const { session } = useAuth();
  const queryClient = useQueryClient();
  const customerId = session?.id || "c63fa787-d893-4b6d-8372-f1abe6fe97aa";

  const [manualOrgId, setManualOrgId] = useState<string | null>(null);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [statusText, setStatusText] = useState("Asistan yanıt hazırlıyor...");
  const [conversations, setConversations] = useState<ConversationMeta[]>(() => {
    if (typeof window === "undefined") return [];
    try {
      const saved = localStorage.getItem("appointai_chat_conversations");
      return saved ? (JSON.parse(saved) as ConversationMeta[]) : [];
    } catch {
      return [];
    }
  });
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Fetch all organizations
  const { data: orgs } = useQuery({
    queryKey: ["chat-organizations"],
    queryFn: () => organizationApi.getAll(),
  });

  // Fetch customer's appointments to resolve the organization of the most recently booked appointment
  const { data: customerAppointments } = useQuery({
    queryKey: ["customer-appointments", customerId],
    queryFn: () => appointmentApi.getCustomerAppointments(customerId),
    enabled: !!customerId,
  });

  const activeOrg: Organization | null = React.useMemo(() => {
    if (!orgs || orgs.length === 0) return null;

    // 1. User manually switched organization in current session
    if (manualOrgId) {
      const manualFound = orgs.find((o) => o.id === manualOrgId);
      if (manualFound) return manualFound;
    }

    // 2. Explicit slug from URL (/app/chat/[organizationSlug])
    if (initialOrgSlug) {
      const slugFound = orgs.find((o) => o.slug === initialOrgSlug);
      if (slugFound) return slugFound;
    }

    // 3. Organization of the customer's most recently booked appointment
    if (customerAppointments && customerAppointments.length > 0) {
      const sorted = [...customerAppointments].sort(
        (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      );
      const lastBookedOrgId = sorted[0]?.organizationId;
      if (lastBookedOrgId) {
        const lastBookedOrg = orgs.find((o) => o.id === lastBookedOrgId);
        if (lastBookedOrg) return lastBookedOrg;
      }
    }

    // 4. Fallback to last booked org stored in localStorage if appointments are still loading
    if (typeof window !== "undefined") {
      try {
        const savedOrgId = localStorage.getItem("appointai_last_booked_org_id");
        if (savedOrgId) {
          const savedOrg = orgs.find((o) => o.id === savedOrgId);
          if (savedOrg) return savedOrg;
        }
      } catch {}
    }

    // 5. Fallback to first active organization
    return orgs[0];
  }, [orgs, manualOrgId, initialOrgSlug, customerAppointments]);

  // Save conversations to localStorage
  const saveConversationMeta = (convId: string, firstMsg: string) => {
    const newMeta: ConversationMeta = {
      id: convId,
      title: firstMsg.slice(0, 30) + (firstMsg.length > 30 ? "..." : ""),
      organizationId: activeOrg?.id,
      organizationName: activeOrg?.name,
      organizationSlug: activeOrg?.slug,
      updatedAt: "Şimdi",
      preview: firstMsg,
    };

    setConversations((prev) => {
      const filtered = prev.filter((c) => c.id !== convId);
      const updated = [newMeta, ...filtered].slice(0, 15);
      try {
        localStorage.setItem("appointai_chat_conversations", JSON.stringify(updated));
      } catch {}
      return updated;
    });
  };

  // Auto-scroll to latest message
  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages, isLoading]);

  const handleStartNewConversation = () => {
    setConversationId(null);
    setMessages([]);
    toast.info("Yeni sohbet başlatıldı.");
  };

  const handleSelectOrganization = (org: Organization) => {
    setManualOrgId(org.id);
    setConversationId(null);
    setMessages([]);
    try {
      localStorage.setItem("appointai_last_booked_org_id", org.id);
    } catch {}
    toast.info(`Aktif kurum: ${org.name}`);
  };

  const handleSelectConversation = async (id: string) => {
    setConversationId(id);
    const meta = conversations.find((c) => c.id === id);
    if (meta?.organizationId) {
      setManualOrgId(meta.organizationId);
    } else if (meta?.organizationSlug && orgs) {
      const matched = orgs.find((o) => o.slug === meta.organizationSlug);
      if (matched) setManualOrgId(matched.id);
    }

    try {
      const history = await chatApi.getConversation(id);
      if (history?.organizationId) {
        setManualOrgId(history.organizationId);
      }
      if (history?.messages) {
        setMessages(
          history.messages.map((m) => ({
            id: m.id,
            role: m.role,
            content: m.content,
            createdAt: new Date(m.createdAt).toLocaleTimeString("tr-TR", {
              hour: "2-digit",
              minute: "2-digit",
            }),
          }))
        );
      }
      toast.info("Görüşme yüklendi.");
    } catch {
      toast.info("Görüşme seçildi.");
    }
  };

  const handleSendMessage = async (userText: string) => {
    if (!userText.trim()) return;

    const orgId = activeOrg?.id || "5f8a475a-bcc0-4241-8955-c3d13acee320";

    // 1. Add user message locally
    const userMsg: ChatMessage = {
      id: "usr-" + Date.now(),
      role: "user",
      content: userText,
      createdAt: new Date().toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }),
    };

    setMessages((prev) => [...prev, userMsg]);
    setIsLoading(true);

    // Dynamic status indicators
    const lower = userText.toLowerCase();
    if (lower.includes("saat") || lower.includes("uygun") || lower.includes("yarın") || lower.includes("müsait") || lower.includes("gün")) {
      setStatusText("Uygun gün ve saatler kontrol ediliyor...");
    } else if (lower.includes("onay") || lower.includes("evet")) {
      setStatusText("İşleminiz onaylanıyor...");
    } else if (lower.includes("iptal")) {
      setStatusText("Randevu kayıtları kontrol ediliyor...");
    } else {
      setStatusText("Asistan yanıt hazırlıyor...");
    }

    try {
      const response: AgentResponseDto = await chatApi.sendMessage({
        organizationId: orgId,
        customerId: customerId,
        message: userText,
        conversationId: conversationId,
      });

      // Update conversation ID
      if (response.conversationId) {
        setConversationId(response.conversationId);
        saveConversationMeta(response.conversationId, userText);
      }

      // Check if response indicates success, confirmation, or cancellation
      let cardType: ChatMessage["cardType"] = "text";
      let successData: ChatMessage["successData"] = undefined;
      let confirmationData: ChatMessage["confirmationData"] = undefined;

      const respContent = response.content || "";
      const combinedText = `${response.summary || ""} ${respContent}`;
      const parsed = parseSlotDisplay(combinedText);
      const matchNumber = combinedText.match(/APT-[\w-]+/i);

      const isCancelledCompletion =
        (!response.requiresConfirmation && response.proposedAction === "cancel_appointment") ||
        respContent.includes("başarıyla iptal edildi") ||
        (response.summary || "").toLowerCase().includes("iptal edildi");

      const isConfirmedSuccess =
        !isCancelledCompletion &&
        !response.requiresConfirmation &&
        (response.isCompleted ||
          respContent.includes("başarıyla oluşturuldu") ||
          respContent.includes("onaylandı ve oluşturuldu") ||
          respContent.includes("Randevunuz oluşturuldu"));

      if (isConfirmedSuccess) {
        try {
          localStorage.setItem("appointai_last_booked_org_id", orgId);
        } catch {}
      }

      if (response.isCompleted || isConfirmedSuccess || isCancelledCompletion) {
        queryClient.invalidateQueries();
      }

      if (isConfirmedSuccess) {
        cardType = "appointment_success";
        successData = {
          appointmentNumber: matchNumber ? matchNumber[0] : "APT-ONAYLANDI",
          localDisplay: parsed.localDisplay || "Onaylandı",
        };
      } else if (response.requiresConfirmation) {
        if (response.proposedAction === "cancel_appointment") {
          cardType = "appointment_cancelled";
          confirmationData = {
            organizationId: orgId,
            organizationName: activeOrg?.name,
            appointmentNumber: matchNumber ? matchNumber[0] : undefined,
            date: parsed.date,
            time: parsed.time,
            localDisplay: parsed.localDisplay,
            customerName: session?.fullName,
          };
        } else {
          cardType = "appointment_confirmation";
          confirmationData = {
            organizationId: orgId,
            organizationName: activeOrg?.name,
            appointmentNumber: matchNumber ? matchNumber[0] : undefined,
            date: parsed.date,
            time: parsed.time,
            localDisplay: parsed.localDisplay,
            customerName: session?.fullName,
          };
        }
      }

      const assistantMsg: ChatMessage = {
        id: response.messageId || "ast-" + Date.now(),
        role: "assistant",
        content: respContent,
        createdAt: new Date().toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }),
        cardType,
        requiresConfirmation: response.requiresConfirmation,
        proposedAction: response.proposedAction,
        summary: response.summary,
        isCompleted: response.isCompleted,
        successData,
        confirmationData,
      };

      setMessages((prev) => [...prev, assistantMsg]);
    } catch (err: unknown) {
      const errorMessage = err instanceof Error ? err.message : "AI Asistan ile iletişim kurulamadı.";
      toast.error(errorMessage);
      setMessages((prev) => [
        ...prev,
        {
          id: "err-" + Date.now(),
          role: "assistant",
          content: "Üzgünüm, bir bağlantı hatası oluştu. Lütfen tekrar deneyiniz.",
          createdAt: new Date().toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }),
          cardType: "error",
        },
      ]);
    } finally {
      setIsLoading(false);
    }
  };

  // Slot selected from interactive buttons
  const handleSelectSlot = (slot: { id?: string; time: string; startUtc?: string }) => {
    handleSendMessage(`${slot.time} saatine randevu almak istiyorum.`);
  };

  // Confirm appointment button clicked
  const handleConfirmAppointment = () => {
    handleSendMessage("Evet, randevu bilgilerimi onaylıyorum.");
  };

  // Change time button clicked
  const handleSlotChangeTime = () => {
    handleSendMessage("Farklı bir saat seçmek istiyorum, diğer uygun saatleri listeler misin?");
  };

  // Cancel appointment button clicked
  const handleCancelAction = () => {
    handleSendMessage("Vazgeçtim, randevu oluşturma işlemini iptal et.");
  };

  return (
    <div className="flex h-[calc(100vh-4rem)] bg-background overflow-hidden relative">
      {/* Desktop History Sidebar */}
      <div className="hidden lg:block h-full">
        <ChatSidebar
          conversations={conversations}
          activeConversationId={conversationId}
          onSelectConversation={handleSelectConversation}
          onNewConversation={handleStartNewConversation}
          currentOrgName={activeOrg?.name}
          currentOrgSlug={activeOrg?.slug}
          organizations={orgs}
          currentOrgId={activeOrg?.id}
          onSelectOrganization={handleSelectOrganization}
        />
      </div>

      {/* Main Chat Interface */}
      <div className="flex-1 flex flex-col h-full overflow-hidden relative">
        {/* Header */}
        <ChatHeader
          organizationName={activeOrg?.name || "AppointAI Genel Asistan"}
          organizationSlug={activeOrg?.slug}
          onNewConversation={handleStartNewConversation}
        />

        {/* Message Thread Area */}
        <div className="flex-1 overflow-y-auto px-4 sm:px-6 py-6 space-y-4">
          {messages.length === 0 ? (
            <ChatEmptyState
              organizationName={activeOrg?.name}
              onSelectPrompt={(prompt) => handleSendMessage(prompt)}
            />
          ) : (
            messages.map((msg) => (
              <ChatMessageBubble
                key={msg.id}
                message={msg}
                organizationName={activeOrg?.name}
                onSelectSlot={handleSelectSlot}
                onConfirmAppointment={handleConfirmAppointment}
                onChangeTime={handleSlotChangeTime}
                onCancelAppointment={handleCancelAction}
                onConfirmCancellation={() => handleSendMessage("Evet, randevumu kesin olarak iptal et.")}
                onKeepAppointment={() => handleSendMessage("Hayır, randevum kalsın.")}
                isLoadingAction={isLoading}
              />
            ))
          )}

          {/* Typing Indicator */}
          {isLoading && <TypingIndicator statusText={statusText} />}

          <div ref={messagesEndRef} />
        </div>

        {/* Composer */}
        <ChatComposer
          onSendMessage={handleSendMessage}
          isLoading={isLoading}
          suggestedPills={[
            "Müsait günler ve saatler",
            "Yarın saat 16'da randevu almak istiyorum",
            "Çalışma saatleriniz nedir?",
            "Oluşturduğum ve iptal ettiğim randevular",
            "Randevumu iptal etmek istiyorum",
          ]}
          onSelectPill={(pill) => handleSendMessage(pill)}
        />
      </div>
    </div>
  );
}
