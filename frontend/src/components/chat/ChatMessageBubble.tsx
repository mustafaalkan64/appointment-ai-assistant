"use client";

import React, { useState } from "react";
import { Sparkles, Bot, User, Copy, Check } from "lucide-react";
import { ChatMessage } from "@/types";
import { AvailableSlotCards } from "./AvailableSlotCards";
import { AppointmentConfirmationCard } from "./AppointmentConfirmationCard";
import { AppointmentSuccessCard } from "./AppointmentSuccessCard";
import { AppointmentCancellationCard } from "./AppointmentCancellationCard";
import { WaitingQueueCard } from "./WaitingQueueCard";

interface ChatMessageBubbleProps {
  message: ChatMessage;
  organizationName?: string;
  onSelectSlot: (slot: { id?: string; time: string; startUtc?: string }) => void;
  onConfirmAppointment: () => void;
  onChangeTime: () => void;
  onCancelAppointment: () => void;
  onConfirmCancellation: () => void;
  onKeepAppointment: () => void;
  isLoadingAction?: boolean;
}

export function ChatMessageBubble({
  message,
  organizationName,
  onSelectSlot,
  onConfirmAppointment,
  onChangeTime,
  onCancelAppointment,
  onConfirmCancellation,
  onKeepAppointment,
  isLoadingAction = false,
}: ChatMessageBubbleProps) {
  const [copied, setCopied] = useState(false);
  const isUser = message.role === "user";

  const handleCopy = () => {
    navigator.clipboard.writeText(message.content);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  // Helper to extract HH:MM times from assistant text if slots are mentioned in freeform text
  const extractTimes = (text: string): string[] => {
    const timeRegex = /\b([01]?\d|2[0-3]):[0-5]\d\b/g;
    const matches = text.match(timeRegex);
    if (!matches) return [];
    // Deduplicate
    return Array.from(new Set(matches)).slice(0, 8);
  };

  const parsedTimes = !isUser && (!message.slotsData || message.slotsData.length === 0)
    ? extractTimes(message.content)
    : [];

  if (isUser) {
    return (
      <div className="flex justify-end items-end gap-2 group animate-in fade-in slide-in-from-bottom-2 duration-200">
        <div className="flex flex-col items-end max-w-[85%] sm:max-w-[70%]">
          <div className="bg-primary text-primary-foreground rounded-2xl rounded-tr-xs px-4 py-2.5 shadow-xs text-xs sm:text-sm leading-relaxed whitespace-pre-wrap break-words">
            {message.content}
          </div>
          <span className="text-[10px] text-muted-foreground mt-1 px-1">
            {message.createdAt}
          </span>
        </div>
        <div className="w-7 h-7 rounded-full bg-primary/20 text-primary flex items-center justify-center shrink-0 text-xs font-bold mb-4">
          <User className="w-3.5 h-3.5" />
        </div>
      </div>
    );
  }

  // Assistant Message
  return (
    <div className="flex items-start gap-2.5 max-w-full sm:max-w-[90%] group animate-in fade-in slide-in-from-bottom-2 duration-200">
      <div className="w-8 h-8 rounded-xl bg-primary/10 text-primary flex items-center justify-center shrink-0 mt-0.5 shadow-2xs">
        <Bot className="w-4 h-4" />
      </div>

      <div className="flex-1 space-y-2 overflow-hidden">
        {/* Text Bubble */}
        <div className="bg-surface border border-border/80 text-foreground rounded-2xl rounded-tl-xs px-4 py-3 shadow-xs relative">
          <div className="text-xs sm:text-sm leading-relaxed whitespace-pre-wrap break-words">
            {message.content}
          </div>

          <div className="flex items-center justify-between mt-2 pt-2 border-t border-border/40 text-[10px] text-muted-foreground">
            <span>{message.createdAt}</span>
            <button
              onClick={handleCopy}
              className="opacity-0 group-hover:opacity-100 transition-opacity hover:text-foreground flex items-center gap-1"
              title="Mesajı Kopyala"
            >
              {copied ? <Check className="w-3 h-3 text-emerald-500" /> : <Copy className="w-3 h-3" />}
              <span>{copied ? "Kopyalandı" : "Kopyala"}</span>
            </button>
          </div>
        </div>

        {/* 1. Interactive Slot Cards */}
        {(message.slotsData && message.slotsData.length > 0) ? (
          <AvailableSlotCards
            slots={message.slotsData}
            onSelectSlot={onSelectSlot}
            disabled={isLoadingAction}
          />
        ) : parsedTimes.length > 0 && message.content.toLowerCase().includes("uygun") ? (
          <AvailableSlotCards
            rawTimes={parsedTimes}
            onSelectSlot={onSelectSlot}
            disabled={isLoadingAction}
          />
        ) : null}

        {/* 2. Confirmation Card */}
        {(message.requiresConfirmation || message.cardType === "appointment_confirmation") && (
          <AppointmentConfirmationCard
            organizationName={message.confirmationData?.organizationName || organizationName}
            date={message.confirmationData?.date || "-"}
            time={message.confirmationData?.time || "-"}
            customerName={message.confirmationData?.customerName}
            onConfirm={onConfirmAppointment}
            onChangeTime={onChangeTime}
            onCancel={onCancelAppointment}
            isLoading={isLoadingAction}
          />
        )}

        {/* 3. Success Card */}
        {(message.cardType === "appointment_success" || message.successData) && (
          <AppointmentSuccessCard
            appointmentNumber={message.successData?.appointmentNumber}
            organizationName={organizationName}
            dateTimeDisplay={message.successData?.localDisplay}
          />
        )}

        {/* 4. Cancellation Card */}
        {message.cardType === "appointment_cancelled" && (
          <AppointmentCancellationCard
            organizationName={organizationName}
            onConfirmCancel={onConfirmCancellation}
            onKeepAppointment={onKeepAppointment}
            isLoading={isLoadingAction}
          />
        )}

        {/* 5. Waiting Queue Card */}
        {(message.cardType === "waiting_queue" || message.queueData) && (
          <WaitingQueueCard
            position={message.queueData?.position}
            requestedTime={message.queueData?.localDisplay}
          />
        )}
      </div>
    </div>
  );
}
