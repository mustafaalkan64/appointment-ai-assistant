"use client";

import React, { useState, useEffect, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "@/providers/providers";
import { chatApi, organizationApi } from "@/lib/api/client";
import { ChatMessage, AgentResponseDto, Organization } from "@/types";
import { ChatHeader } from "./ChatHeader";
import { ChatSidebar, ConversationMeta } from "./ChatSidebar";
import { ChatMessageBubble } from "./ChatMessageBubble";
import { ChatComposer } from "./ChatComposer";
import { ChatEmptyState } from "./ChatEmptyState";
import { TypingIndicator } from "./TypingIndicator";
import { toast } from "sonner";

interface ChatContainerProps {
  initialOrgSlug?: string;
}

export function ChatContainer({ initialOrgSlug }: ChatContainerProps) {
  const { session } = useAuth();
  const [activeOrg, setActiveOrg] = useState<Organization | null>(null);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [statusText, setStatusText] = useState("Asistan yanıt hazırlıyor...");
  const [conversations, setConversations] = useState<ConversationMeta[]>([]);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Fetch all organizations to match slug or select default
  const { data: orgs } = useQuery({
    queryKey: ["chat-organizations"],
    queryFn: () => organizationApi.getAll(),
  });

  useEffect(() => {
    if (!orgs || orgs.length === 0) return;
    if (initialOrgSlug) {
      const found = orgs.find((o) => o.slug === initialOrgSlug);
      if (found) setActiveOrg(found);
    } else if (!activeOrg) {
      // Default to first active org (e.g. Concurrency Clinic)
      setActiveOrg(orgs[0]);
    }
  }, [orgs, initialOrgSlug, activeOrg]);

  // Load conversation history from local storage on mount
  useEffect(() => {
    try {
      const saved = localStorage.getItem("appointai_chat_conversations");
      if (saved) {
        const parsed = JSON.parse(saved);
        setConversations(parsed);
      }
    } catch {
      // Ignore
    }
  }, []);

  // Save conversations to localStorage
  const saveConversationMeta = (convId: string, firstMsg: string) => {
    const newMeta: ConversationMeta = {
      id: convId,
      title: firstMsg.slice(0, 30) + (firstMsg.length > 30 ? "..." : ""),
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

  const handleSendMessage = async (userText: string) => {
    if (!userText.trim()) return;

    const orgId = activeOrg?.id || "5f8a475a-bcc0-4241-8955-c3d13acee320";
    const customerId = session?.id || "c63fa787-d893-4b6d-8372-f1abe6fe97aa";

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
    if (lower.includes("saat") || lower.includes("uygun") || lower.includes("yarın") || lower.includes("müsait")) {
      setStatusText("Uygun saatler kontrol ediliyor...");
    } else if (lower.includes("onay") || lower.includes("evet")) {
      setStatusText("Randevu kaydediliyor ve onaylanıyor...");
    } else if (lower.includes("iptal")) {
      setStatusText("İptal koşulları kontrol ediliyor...");
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
      const isConfirmedSuccess =
        response.isCompleted ||
        respContent.includes("başarıyla oluşturuldu") ||
        respContent.includes("onaylandı") ||
        respContent.includes("Randevunuz oluşturuldu");

      if (isConfirmedSuccess) {
        cardType = "appointment_success";
        // Extract appointment number if present, e.g. APT-...
        const matchNumber = respContent.match(/APT-[\w-]+/i);
        successData = {
          appointmentNumber: matchNumber ? matchNumber[0] : "APT-" + Math.floor(100000 + Math.random() * 900000),
          localDisplay: "Onaylandı",
        };
      } else if (response.requiresConfirmation) {
        cardType = "appointment_confirmation";
        confirmationData = {
          organizationId: orgId,
          organizationName: activeOrg?.name,
          date: "Seçilen Gün",
          time: "Belirlenen Saat",
        };
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
          onSelectConversation={(id) => {
            setConversationId(id);
            toast.info("Görüşme seçildi.");
          }}
          onNewConversation={handleStartNewConversation}
          currentOrgName={activeOrg?.name}
          currentOrgSlug={activeOrg?.slug}
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
            "Yarın için müsait saatler",
            "Mevcut randevularım",
            "Randevumu onayla",
            "Randevumu iptal et",
          ]}
          onSelectPill={(pill) => handleSendMessage(pill)}
        />
      </div>
    </div>
  );
}
