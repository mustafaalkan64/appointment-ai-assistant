"use client";

import React from "react";
import { Calendar, Clock, Building2, User, CheckCircle2, RotateCcw, X } from "lucide-react";
import { Button } from "@/components/ui/button";

interface AppointmentConfirmationCardProps {
  organizationName?: string;
  date?: string;
  time?: string;
  customerName?: string;
  onConfirm: () => void;
  onChangeTime: () => void;
  onCancel: () => void;
  isLoading?: boolean;
}

export function AppointmentConfirmationCard({
  organizationName = "Kurum",
  date = "Yarın",
  time = "10:30",
  customerName = "Değerli Müşterimiz",
  onConfirm,
  onChangeTime,
  onCancel,
  isLoading = false,
}: AppointmentConfirmationCardProps) {
  return (
    <div className="rounded-2xl border-2 border-primary/30 bg-surface shadow-md overflow-hidden mt-3 max-w-md">
      {/* Header */}
      <div className="bg-primary/10 px-4 py-3 border-b border-primary/20 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Calendar className="w-4 h-4 text-primary" />
          <span className="text-xs font-bold text-foreground">Randevu Onay Bilgileri</span>
        </div>
        <span className="text-[10px] uppercase font-semibold text-primary tracking-wider bg-primary/10 px-2 py-0.5 rounded-md">
          Onay Bekliyor
        </span>
      </div>

      {/* Details */}
      <div className="p-4 space-y-2.5 text-xs">
        <div className="flex items-center justify-between text-muted-foreground pb-1 border-b border-border/50">
          <span className="flex items-center gap-1.5">
            <Building2 className="w-3.5 h-3.5 text-primary" /> Kurum:
          </span>
          <span className="font-semibold text-foreground">{organizationName}</span>
        </div>

        <div className="flex items-center justify-between text-muted-foreground pb-1 border-b border-border/50">
          <span className="flex items-center gap-1.5">
            <Calendar className="w-3.5 h-3.5 text-primary" /> Tarih:
          </span>
          <span className="font-semibold text-foreground">{date}</span>
        </div>

        <div className="flex items-center justify-between text-muted-foreground pb-1 border-b border-border/50">
          <span className="flex items-center gap-1.5">
            <Clock className="w-3.5 h-3.5 text-primary" /> Saat:
          </span>
          <span className="font-bold text-primary text-sm">{time}</span>
        </div>

        <div className="flex items-center justify-between text-muted-foreground">
          <span className="flex items-center gap-1.5">
            <User className="w-3.5 h-3.5 text-primary" /> Müşteri:
          </span>
          <span className="font-medium text-foreground">{customerName}</span>
        </div>
      </div>

      {/* Actions */}
      <div className="p-3 bg-muted/30 border-t border-border flex flex-col sm:flex-row items-center gap-2">
        <Button
          onClick={onConfirm}
          disabled={isLoading}
          size="sm"
          className="w-full sm:flex-1 text-xs font-bold gap-1.5 shadow-xs bg-emerald-600 hover:bg-emerald-700 text-white"
        >
          <CheckCircle2 className="w-4 h-4" />
          {isLoading ? "Onaylanıyor..." : "Evet, Onaylıyorum"}
        </Button>

        <Button
          onClick={onChangeTime}
          disabled={isLoading}
          variant="outline"
          size="sm"
          className="w-full sm:w-auto text-xs gap-1.5"
        >
          <RotateCcw className="w-3.5 h-3.5" />
          Saat Değiştir
        </Button>

        <Button
          onClick={onCancel}
          disabled={isLoading}
          variant="ghost"
          size="sm"
          className="w-full sm:w-auto text-xs text-rose-600 hover:text-rose-700 hover:bg-rose-50 dark:hover:bg-rose-950/20"
        >
          <X className="w-3.5 h-3.5" />
          Vazgeç
        </Button>
      </div>
    </div>
  );
}
