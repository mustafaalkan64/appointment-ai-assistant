"use client";

import React, { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { organizationApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  Clock,
  Calendar,
  Save,
  Plus,
  Sparkles,
  Copy,
  CheckCircle2,
  AlertCircle,
  RefreshCw,
  Loader2,
} from "lucide-react";
import { toast } from "sonner";

interface DaySchedule {
  dayOfWeek: number;
  dayName: string;
  isActive: boolean;
  startTime: string;
  endTime: string;
  slotDurationMinutes: number;
}

const DEFAULT_SCHEDULE: DaySchedule[] = [
  { dayOfWeek: 1, dayName: "Pazartesi", isActive: true, startTime: "09:00", endTime: "17:00", slotDurationMinutes: 30 },
  { dayOfWeek: 2, dayName: "Salı", isActive: true, startTime: "09:00", endTime: "17:00", slotDurationMinutes: 30 },
  { dayOfWeek: 3, dayName: "Çarşamba", isActive: true, startTime: "09:00", endTime: "17:00", slotDurationMinutes: 30 },
  { dayOfWeek: 4, dayName: "Perşembe", isActive: true, startTime: "09:00", endTime: "17:00", slotDurationMinutes: 30 },
  { dayOfWeek: 5, dayName: "Cuma", isActive: true, startTime: "09:00", endTime: "16:00", slotDurationMinutes: 30 },
  { dayOfWeek: 6, dayName: "Cumartesi", isActive: true, startTime: "10:00", endTime: "14:00", slotDurationMinutes: 30 },
  { dayOfWeek: 0, dayName: "Pazar", isActive: false, startTime: "10:00", endTime: "14:00", slotDurationMinutes: 30 },
];

export default function AvailabilityPage() {
  const { session } = useAuth();
  const queryClient = useQueryClient();
  const orgId = session?.organizationId || "5f8a475a-bcc0-4241-8955-c3d13acee320";

  const [schedule, setSchedule] = useState<DaySchedule[]>(DEFAULT_SCHEDULE);
  const [generateStartDate, setGenerateStartDate] = useState("2026-11-01");
  const [generateEndDate, setGenerateEndDate] = useState("2026-11-15");

  useEffect(() => {
    try {
      setGenerateStartDate(new Date().toISOString().split("T")[0]);
      setGenerateEndDate(new Date(Date.now() + 14 * 86400000).toISOString().split("T")[0]);
    } catch {}
  }, []);

  // Fetch current organization details to populate working hours if available
  const { data: orgData, isLoading: loadingOrg } = useQuery({
    queryKey: ["org-details", orgId],
    queryFn: () => organizationApi.getById(orgId),
  });

  // Sync state when orgData is fetched from server
  useEffect(() => {
    const weekly = orgData?.weeklyAvailabilities;
    if (weekly && weekly.length > 0) {
      setSchedule((prev) =>
        prev.map((d) => {
          const match = weekly.find(
            (w: any) => w.dayOfWeek === d.dayOfWeek
          );
          if (match) {
            return {
              ...d,
              isActive: match.isActive,
              startTime: match.startTime.slice(0, 5),
              endTime: match.endTime.slice(0, 5),
              slotDurationMinutes: match.slotDurationMinutes || 30,
            };
          }
          return d;
        })
      );
    }
  }, [orgData]);

  // Save Weekly Schedule Mutation
  const saveScheduleMutation = useMutation({
    mutationFn: async () => {
      const allSchedules = schedule.map((d) => ({
        dayOfWeek: d.dayOfWeek,
        startTime: d.startTime.length === 5 ? d.startTime + ":00" : d.startTime,
        endTime: d.endTime.length === 5 ? d.endTime + ":00" : d.endTime,
        slotDurationMinutes: d.slotDurationMinutes,
        isClosed: !d.isActive,
      }));

      return organizationApi.setWeeklyAvailability({
        organizationId: orgId,
        days: allSchedules,
      });
    },
    onSuccess: () => {
      toast.success("Haftalık çalışma saatleri başarıyla kaydedildi.");
      queryClient.invalidateQueries({ queryKey: ["org-details", orgId] });
    },
    onError: (err: unknown) => {
      const errorMsg = err instanceof Error ? err.message : "Kayıt işlemi başarısız.";
      toast.error(errorMsg);
    },
  });

  // Generate Slots Mutation
  const generateSlotsMutation = useMutation({
    mutationFn: async () => {
      return organizationApi.generateSlots({
        organizationId: orgId,
        startDate: generateStartDate,
        endDate: generateEndDate,
        skipExisting: true,
      });
    },
    onSuccess: (data) => {
      toast.success(
        `Başarılı! ${data.totalCreatedSlots || 0} adet yeni randevu slotu oluşturuldu.`
      );
      queryClient.invalidateQueries({ queryKey: ["admin-available-slots"] });
    },
    onError: (err: unknown) => {
      const errorMsg = err instanceof Error ? err.message : "Slot oluşturma başarısız.";
      toast.error(errorMsg);
    },
  });

  const handleDayToggle = (dayOfWeek: number) => {
    setSchedule((prev) =>
      prev.map((d) => (d.dayOfWeek === dayOfWeek ? { ...d, isActive: !d.isActive } : d))
    );
  };

  const handleTimeChange = (
    dayOfWeek: number,
    field: "startTime" | "endTime" | "slotDurationMinutes",
    value: string | number
  ) => {
    setSchedule((prev) =>
      prev.map((d) => (d.dayOfWeek === dayOfWeek ? { ...d, [field]: value } : d))
    );
  };

  const handleCopyToAll = (sourceDayOfWeek: number) => {
    const source = schedule.find((d) => d.dayOfWeek === sourceDayOfWeek);
    if (!source) return;

    setSchedule((prev) =>
      prev.map((d) => ({
        ...d,
        startTime: source.startTime,
        endTime: source.endTime,
        slotDurationMinutes: source.slotDurationMinutes,
        isActive: true,
      }))
    );
    toast.info(`${source.dayName} çalışma saatleri tüm günlere uygulandı.`);
  };

  const handleApplyPreset = (preset: "7days" | "weekdays" | "weekend") => {
    setSchedule((prev) =>
      prev.map((d) => {
        if (preset === "7days") {
          return {
            ...d,
            isActive: true,
            startTime: d.dayOfWeek === 0 || d.dayOfWeek === 6 ? (d.startTime || "10:00") : (d.startTime || "09:00"),
            endTime: d.dayOfWeek === 0 || d.dayOfWeek === 6 ? (d.endTime || "14:00") : (d.endTime || "17:00"),
          };
        }
        if (preset === "weekdays") {
          return {
            ...d,
            isActive: d.dayOfWeek >= 1 && d.dayOfWeek <= 5,
          };
        }
        if (preset === "weekend") {
          return {
            ...d,
            isActive: d.dayOfWeek === 0 || d.dayOfWeek === 6,
          };
        }
        return d;
      })
    );
    toast.info(
      preset === "7days"
        ? "Haftanın 7 günü (Pazar dahil) açık olarak ayarlandı."
        : preset === "weekdays"
        ? "Hafta içi (5 gün) açık olarak ayarlandı."
        : "Hafta sonu (Cumartesi - Pazar) açık olarak ayarlandı."
    );
  };

  return (
    <div className="space-y-8 max-w-5xl">
      {/* Title */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Müsaitlik & Çalışma Saatleri
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Haftalık çalışma günlerini belirleyin ve tek tıkla randevu slotları oluşturun
          </p>
        </div>

        <Button
          onClick={() => saveScheduleMutation.mutate()}
          disabled={saveScheduleMutation.isPending}
          size="sm"
          className="gap-2 text-xs font-semibold shadow-xs"
        >
          {saveScheduleMutation.isPending ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Save className="w-4 h-4" />
          )}
          Çalışma Saatlerini Kaydet
        </Button>
      </div>

      {/* Weekly Hours Editor Card */}
      <Card className="shadow-xs">
        <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between pb-4 gap-3">
          <div>
            <CardTitle className="text-sm font-bold flex items-center gap-2">
              <Clock className="w-4 h-4 text-primary" />
              Haftalık Mesai Çizelgesi (7 Gün Esnek)
            </CardTitle>
            <p className="text-xs text-muted-foreground mt-0.5">
              Haftanın 7 günü için dilediğiniz gibi çalışma saatleri ve slot aralığı belirleyebilirsiniz
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => handleApplyPreset("7days")}
              className="h-7 text-[11px] px-2.5 font-medium border-primary/30 text-primary hover:bg-primary/5"
            >
              Haftanın 7 Günü Aç
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => handleApplyPreset("weekdays")}
              className="h-7 text-[11px] px-2.5 font-medium"
            >
              Hafta İçi (5 Gün)
            </Button>
            <Badge variant="outline" className="text-[10px] bg-primary/5 text-primary">
              {orgData?.timeZoneId || "Europe/Istanbul"}
            </Badge>
          </div>
        </CardHeader>

        <CardContent className="space-y-3 pt-0">
          <div className="space-y-2.5">
            {schedule.map((day) => (
              <div
                key={day.dayOfWeek}
                className={`p-3.5 rounded-xl border transition-all flex flex-col sm:flex-row sm:items-center justify-between gap-3 ${
                  day.isActive
                    ? "bg-surface border-border"
                    : "bg-muted/30 border-border/60 opacity-60"
                }`}
              >
                {/* Day Toggle & Name */}
                <div className="flex items-center gap-3 sm:w-40">
                  <input
                    type="checkbox"
                    checked={day.isActive}
                    onChange={() => handleDayToggle(day.dayOfWeek)}
                    className="w-4 h-4 rounded text-primary focus:ring-primary accent-primary cursor-pointer"
                  />
                  <div>
                    <span className="text-xs font-bold text-foreground">{day.dayName}</span>
                    <div className="text-[10px] text-muted-foreground">
                      {day.isActive ? "Açık" : "Kapalı"}
                    </div>
                  </div>
                </div>

                {/* Hours & Duration Inputs */}
                {day.isActive ? (
                  <div className="flex flex-wrap items-center gap-2.5 flex-1">
                    <div className="flex items-center gap-1.5">
                      <span className="text-[11px] text-muted-foreground">Başlangıç:</span>
                      <Input
                        type="time"
                        value={day.startTime}
                        onChange={(e) =>
                          handleTimeChange(day.dayOfWeek, "startTime", e.target.value)
                        }
                        className="w-28 text-xs h-8"
                      />
                    </div>

                    <div className="flex items-center gap-1.5">
                      <span className="text-[11px] text-muted-foreground">Bitiş:</span>
                      <Input
                        type="time"
                        value={day.endTime}
                        onChange={(e) =>
                          handleTimeChange(day.dayOfWeek, "endTime", e.target.value)
                        }
                        className="w-28 text-xs h-8"
                      />
                    </div>

                    <div className="flex items-center gap-1.5">
                      <span className="text-[11px] text-muted-foreground">Slot:</span>
                      <select
                        value={day.slotDurationMinutes}
                        onChange={(e) =>
                          handleTimeChange(
                            day.dayOfWeek,
                            "slotDurationMinutes",
                            Number(e.target.value)
                          )
                        }
                        className="h-8 rounded-lg border border-border bg-surface px-2 text-xs text-foreground focus:outline-none focus:ring-1 focus:ring-primary"
                      >
                        <option value={15}>15 dk</option>
                        <option value={20}>20 dk</option>
                        <option value={30}>30 dk</option>
                        <option value={45}>45 dk</option>
                        <option value={60}>60 dk</option>
                      </select>
                    </div>

                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => handleCopyToAll(day.dayOfWeek)}
                      title="Bu saatleri tüm haftaya kopyala"
                      className="h-8 px-2 text-[11px] text-muted-foreground hover:text-primary gap-1"
                    >
                      <Copy className="w-3 h-3" />
                      Kopyala
                    </Button>
                  </div>
                ) : (
                  <div className="text-xs text-muted-foreground italic flex-1">
                    Bu gün kurum randevuya kapalıdır.
                  </div>
                )}
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      {/* Batch Slot Generator */}
      <Card className="shadow-xs border-primary/20 bg-primary/[0.02]">
        <CardHeader>
          <CardTitle className="text-sm font-bold flex items-center gap-2">
            <Sparkles className="w-4 h-4 text-primary" />
            Otomatik Randevu Slotu Üretici
          </CardTitle>
          <p className="text-xs text-muted-foreground mt-0.5">
            Yukarıda tanımlanan haftalık çalışma saatlerine göre seçilen tarih aralığı için toplu slot üretin
          </p>
        </CardHeader>

        <CardContent className="space-y-4 pt-0">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-foreground">Başlangıç Tarihi</label>
              <Input
                type="date"
                value={generateStartDate}
                onChange={(e) => setGenerateStartDate(e.target.value)}
                className="text-xs h-9"
              />
            </div>
            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-foreground">Bitiş Tarihi</label>
              <Input
                type="date"
                value={generateEndDate}
                onChange={(e) => setGenerateEndDate(e.target.value)}
                className="text-xs h-9"
              />
            </div>
          </div>

          <div className="flex items-center justify-between pt-2 border-t border-border/50">
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <CheckCircle2 className="w-4 h-4 text-emerald-500" />
              <span>Mevcut dolu randevular korunur, yalnızca yeni boş slotlar eklenir.</span>
            </div>

            <Button
              onClick={() => generateSlotsMutation.mutate()}
              disabled={generateSlotsMutation.isPending}
              size="sm"
              className="gap-2 text-xs font-semibold shadow-xs"
            >
              {generateSlotsMutation.isPending ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <Calendar className="w-4 h-4" />
              )}
              Slotları Şimdi Üret
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
