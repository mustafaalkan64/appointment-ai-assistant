"use client";

import React from "react";
import Link from "next/link";
import { CheckCircle2, Calendar, Download, ArrowRight, Building2, Clock } from "lucide-react";
import { Button } from "@/components/ui/button";

interface AppointmentSuccessCardProps {
  appointmentNumber?: string;
  organizationName?: string;
  dateTimeDisplay?: string;
  onNewConversation?: () => void;
}

export function AppointmentSuccessCard({
  appointmentNumber = "APT-20261102-001",
  organizationName = "Kurum",
  dateTimeDisplay = "2 Kasım Pazartesi, 10:30",
  onNewConversation,
}: AppointmentSuccessCardProps) {
  // Simple ICS file generator for Add to Calendar
  const downloadIcs = () => {
    const icsData = [
      "BEGIN:VCALENDAR",
      "VERSION:2.0",
      "PRODID:-//AppointAI//AppointmentBooking//EN",
      "BEGIN:VEVENT",
      `SUMMARY:Randevu - ${organizationName}`,
      `DESCRIPTION:Randevu No: ${appointmentNumber}`,
      "STATUS:CONFIRMED",
      "END:VEVENT",
      "END:VCALENDAR",
    ].join("\r\n");

    const blob = new Blob([icsData], { type: "text/calendar;charset=utf-8" });
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.setAttribute("download", `randevu-${appointmentNumber}.ics`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  return (
    <div className="rounded-2xl border border-emerald-500/30 bg-gradient-to-b from-emerald-500/10 to-surface p-5 shadow-lg space-y-4 max-w-md mt-3">
      <div className="flex items-center gap-3">
        <div className="w-10 h-10 rounded-xl bg-emerald-500 text-white flex items-center justify-center shrink-0 shadow-sm">
          <CheckCircle2 className="w-6 h-6" />
        </div>
        <div>
          <h3 className="text-sm font-bold text-foreground">
            Randevunuz Başarıyla Oluşturuldu! 🎉
          </h3>
          <p className="text-[11px] text-muted-foreground mt-0.5">
            Sistemde kesinleştirildi ve onaylandı.
          </p>
        </div>
      </div>

      <div className="p-3.5 rounded-xl bg-surface/80 border border-border space-y-2 text-xs">
        <div className="flex items-center justify-between pb-1.5 border-b border-border/50">
          <span className="text-muted-foreground">Randevu Numarası:</span>
          <span className="font-mono font-bold text-primary">{appointmentNumber}</span>
        </div>
        <div className="flex items-center justify-between pb-1.5 border-b border-border/50">
          <span className="text-muted-foreground flex items-center gap-1.5">
            <Building2 className="w-3.5 h-3.5 text-primary" /> Kurum:
          </span>
          <span className="font-semibold text-foreground">{organizationName}</span>
        </div>
        <div className="flex items-center justify-between">
          <span className="text-muted-foreground flex items-center gap-1.5">
            <Clock className="w-3.5 h-3.5 text-primary" /> Tarih & Saat:
          </span>
          <span className="font-semibold text-foreground">{dateTimeDisplay}</span>
        </div>
      </div>

      <div className="flex flex-col sm:flex-row items-center gap-2 pt-1">
        <Link href="/app/my-appointments" className="w-full sm:flex-1">
          <Button size="sm" className="w-full text-xs font-semibold gap-1.5">
            <Calendar className="w-3.5 h-3.5" />
            Randevularıma Git
          </Button>
        </Link>
        <Button
          onClick={downloadIcs}
          variant="outline"
          size="sm"
          className="w-full sm:w-auto text-xs gap-1.5"
        >
          <Download className="w-3.5 h-3.5" />
          Takvime Ekle
        </Button>
      </div>
    </div>
  );
}
