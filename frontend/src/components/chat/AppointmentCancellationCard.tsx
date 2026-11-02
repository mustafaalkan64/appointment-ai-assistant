"use client";

import React from "react";
import { AlertTriangle, Calendar, Clock, Building2, Check, X } from "lucide-react";
import { Button } from "@/components/ui/button";

interface AppointmentCancellationCardProps {
  appointmentNumber?: string;
  organizationName?: string;
  dateTimeDisplay?: string;
  onConfirmCancel: () => void;
  onKeepAppointment: () => void;
  isLoading?: boolean;
}

export function AppointmentCancellationCard({
  appointmentNumber = "APT-001",
  organizationName = "Kurum",
  dateTimeDisplay = "Yarın, 10:30",
  onConfirmCancel,
  onKeepAppointment,
  isLoading = false,
}: AppointmentCancellationCardProps) {
  return (
    <div className="rounded-2xl border-2 border-rose-500/30 bg-rose-500/5 p-4 shadow-md space-y-3 max-w-md mt-3">
      <div className="flex items-center gap-2.5">
        <div className="w-8 h-8 rounded-lg bg-rose-500/10 text-rose-600 flex items-center justify-center">
          <AlertTriangle className="w-4 h-4" />
        </div>
        <div>
          <h4 className="text-xs font-bold text-foreground">Randevu İptal Onayı</h4>
          <p className="text-[10px] text-muted-foreground">Bu işlem geri alınamaz</p>
        </div>
      </div>

      <div className="p-3 rounded-xl bg-surface border border-border/80 space-y-1.5 text-xs">
        <div className="flex items-center justify-between pb-1 border-b border-border/40">
          <span className="text-muted-foreground">Randevu No:</span>
          <span className="font-mono font-semibold">{appointmentNumber}</span>
        </div>
        <div className="flex items-center justify-between pb-1 border-b border-border/40">
          <span className="text-muted-foreground">Kurum:</span>
          <span className="font-medium text-foreground">{organizationName}</span>
        </div>
        <div className="flex items-center justify-between">
          <span className="text-muted-foreground">Tarih & Saat:</span>
          <span className="font-semibold text-rose-600">{dateTimeDisplay}</span>
        </div>
      </div>

      <div className="flex flex-col sm:flex-row items-center gap-2 pt-1">
        <Button
          onClick={onConfirmCancel}
          disabled={isLoading}
          size="sm"
          variant="outline"
          className="w-full sm:flex-1 text-xs font-semibold gap-1.5 border-rose-500/40 text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-950/20"
        >
          <X className="w-3.5 h-3.5" />
          {isLoading ? "İptal Ediliyor..." : "Evet, İptal Et"}
        </Button>

        <Button
          onClick={onKeepAppointment}
          disabled={isLoading}
          size="sm"
          className="w-full sm:flex-1 text-xs font-semibold gap-1.5 bg-surface text-foreground hover:bg-muted border border-border"
        >
          <Check className="w-3.5 h-3.5" />
          Hayır, Randevum Kalsın
        </Button>
      </div>
    </div>
  );
}
