"use client";

import React from "react";
import { Clock, Check } from "lucide-react";
import { AppointmentSlotDto } from "@/types";

interface AvailableSlotCardsProps {
  slots?: AppointmentSlotDto[];
  rawTimes?: string[];
  selectedSlotId?: string;
  onSelectSlot: (slot: { id?: string; time: string; startUtc?: string }) => void;
  disabled?: boolean;
}

export function AvailableSlotCards({
  slots,
  rawTimes,
  selectedSlotId,
  onSelectSlot,
  disabled = false,
}: AvailableSlotCardsProps) {
  // If structured AppointmentSlotDto array is provided
  if (slots && slots.length > 0) {
    return (
      <div className="space-y-2 mt-3">
        <div className="text-[11px] font-semibold text-muted-foreground flex items-center gap-1.5">
          <Clock className="w-3.5 h-3.5 text-primary" />
          <span>Uygun Saat Dilimleri (Seçim Yapın)</span>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
          {slots.map((slot) => {
            const isSelected = selectedSlotId === slot.id;
            return (
              <button
                key={slot.id}
                type="button"
                disabled={disabled}
                onClick={() =>
                  onSelectSlot({
                    id: slot.id,
                    time: slot.localStartTime || slot.localDisplay,
                    startUtc: slot.startUtc,
                  })
                }
                className={`flex items-center justify-between px-3 py-2 rounded-xl border text-xs font-semibold transition-all ${
                  isSelected
                    ? "border-primary bg-primary/10 text-primary ring-2 ring-primary/20 shadow-xs"
                    : "border-border bg-surface hover:border-primary/50 hover:bg-primary/5 text-foreground"
                } ${disabled ? "opacity-60 cursor-not-allowed" : "cursor-pointer"}`}
              >
                <span>{slot.localStartTime || slot.localDisplay}</span>
                {isSelected && <Check className="w-3.5 h-3.5 text-primary" />}
              </button>
            );
          })}
        </div>
      </div>
    );
  }

  // If raw string times are parsed from AI text (e.g. ["10:00", "10:30", "11:00", "11:30"])
  if (rawTimes && rawTimes.length > 0) {
    return (
      <div className="space-y-2 mt-3">
        <div className="text-[11px] font-semibold text-muted-foreground flex items-center gap-1.5">
          <Clock className="w-3.5 h-3.5 text-primary" />
          <span>Önerilen Saatler</span>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-2">
          {rawTimes.map((time, idx) => (
            <button
              key={idx}
              type="button"
              disabled={disabled}
              onClick={() => onSelectSlot({ time })}
              className="flex items-center justify-center px-3 py-2 rounded-xl border border-border bg-surface hover:border-primary/50 hover:bg-primary/5 text-xs font-semibold text-foreground transition-all cursor-pointer shadow-2xs hover:shadow-xs active:scale-98"
            >
              <span>{time}</span>
            </button>
          ))}
        </div>
      </div>
    );
  }

  return null;
}
