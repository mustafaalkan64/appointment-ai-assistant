"use client";

import React, { useState, useEffect } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { appointmentApi, organizationApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Calendar as CalendarIcon,
  ChevronLeft,
  ChevronRight,
  Clock,
  CheckCircle2,
  Lock,
  Unlock,
  AlertCircle,
  RefreshCw,
  Filter,
  User,
} from "lucide-react";
import { toast } from "sonner";
import { AppointmentSlotDto, SlotStatus } from "@/types";

// Helper to format local date string without UTC timezone shift
function formatLocalDate(d: Date): string {
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export default function AdminCalendarPage() {
  const { session } = useAuth();
  const queryClient = useQueryClient();
  const orgId = session?.organizationId || "5f8a475a-bcc0-4241-8955-c3d13acee320";

  // Selected date state
  const [selectedDate, setSelectedDate] = useState<Date>(new Date());
  const [viewMode, setViewMode] = useState<"day" | "week">("day");

  useEffect(() => {
    try {
      setSelectedDate(new Date());
    } catch {}
  }, []);

  const formattedDate = formatLocalDate(selectedDate);

  // Fetch organization details to know weekly availability schedule
  const { data: orgData } = useQuery({
    queryKey: ["admin-org-details", orgId],
    queryFn: () => organizationApi.getById(orgId),
    staleTime: 1000 * 60 * 5,
  });

  const dayOfWeek = selectedDate.getDay();
  const weeklyDay = orgData?.weeklyAvailabilities?.find(
    (w) => w.dayOfWeek === dayOfWeek
  );
  const isDayClosed = weeklyDay ? !weeklyDay.isActive : false;

  // Fetch all slots for the selected date (including booked, reserved, and inactive slots)
  const {
    data: slotsResponse,
    isLoading: loadingSlots,
    refetch,
  } = useQuery({
    queryKey: ["admin-slots-date", orgId, formattedDate],
    queryFn: () => appointmentApi.getAvailableSlots(orgId, formattedDate, formattedDate, formattedDate, true),
  });

  // Toggle slot mutation (activate / deactivate slot)
  const toggleSlotMutation = useMutation({
    mutationFn: async ({ slotId, isActive }: { slotId: string; isActive: boolean }) => {
      return organizationApi.toggleSlot(orgId, slotId, isActive);
    },
    onSuccess: (data) => {
      toast.success(
        data.isActive
          ? "Slot başarıyla aktif edildi ve randevuya açıldı."
          : "Slot kapatıldı ve bloke edildi."
      );
      queryClient.invalidateQueries({ queryKey: ["admin-slots-date"] });
    },
    onError: (err: unknown) => {
      const errorMsg = err instanceof Error ? err.message : "Slot durumu değiştirilemedi.";
      toast.error(errorMsg);
    },
  });

  const slots = slotsResponse?.slots || [];

  const handlePrevDay = () => {
    const prev = new Date(selectedDate);
    prev.setDate(prev.getDate() - 1);
    setSelectedDate(prev);
  };

  const handleNextDay = () => {
    const next = new Date(selectedDate);
    next.setDate(next.getDate() + 1);
    setSelectedDate(next);
  };

  const handleToday = () => {
    setSelectedDate(new Date());
  };

  const availableCount = slots.filter((s) => s.status === 1 && s.isActive !== false).length;
  const bookedCount = slots.filter((s) => s.status === 3 || s.status === 2).length;
  const blockedCount = slots.filter((s) => s.status === 4 || s.isActive === false).length;

  return (
    <div className="space-y-6">
      {/* Header & Date Navigation */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Takvim & Slot Yönetimi
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Günlük randevu slotlarını görüntüleyin, dolu randevuları takip edin ve müsaitlikleri yönetin
          </p>
        </div>

        {/* Date Navigator Bar */}
        <div className="flex items-center gap-2 bg-surface border border-border p-1 rounded-xl shadow-xs">
          <Button
            onClick={handlePrevDay}
            variant="ghost"
            size="sm"
            className="h-8 w-8 p-0"
            title="Önceki Gün"
          >
            <ChevronLeft className="w-4 h-4" />
          </Button>

          <Button
            onClick={handleToday}
            variant="ghost"
            size="sm"
            className="h-8 px-2.5 text-xs font-semibold text-primary"
          >
            Bugün
          </Button>

          <div className="flex items-center gap-1.5 px-2">
            <span className="text-xs font-bold text-foreground min-w-[110px] text-center">
              {selectedDate.toLocaleDateString("tr-TR", {
                day: "numeric",
                month: "long",
                weekday: "short",
              })}
            </span>
            {isDayClosed && (
              <Badge
                variant="outline"
                className="bg-rose-500/10 text-rose-600 dark:text-rose-400 border-rose-500/20 text-[10px] py-0 px-1.5 font-semibold shrink-0"
              >
                Kapalı
              </Badge>
            )}
          </div>

          <Button
            onClick={handleNextDay}
            variant="ghost"
            size="sm"
            className="h-8 w-8 p-0"
            title="Sonraki Gün"
          >
            <ChevronRight className="w-4 h-4" />
          </Button>
        </div>
      </div>

      {/* Summary Chips & Controls */}
      <div className="flex flex-wrap items-center justify-between gap-4 p-3 bg-surface rounded-xl border border-border text-xs">
        <div className="flex flex-wrap items-center gap-4">
          {isDayClosed ? (
            <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-rose-500/10 border border-rose-500/20 text-rose-600 dark:text-rose-400 font-semibold text-xs">
              <Lock className="w-3.5 h-3.5 shrink-0" />
              <span>Haftalık takvimde bu gün kapalıdır</span>
            </div>
          ) : (
            <div className="flex items-center gap-1.5">
              <span className="w-2.5 h-2.5 rounded-full bg-emerald-500" />
              <span className="text-muted-foreground">Müsait:</span>
              <span className="font-bold text-foreground">
                {availableCount}
              </span>
            </div>
          )}

          <div className="flex items-center gap-1.5">
            <span className="w-2.5 h-2.5 rounded-full bg-indigo-500" />
            <span className="text-muted-foreground">Dolu / Rezerve:</span>
            <span className="font-bold text-foreground">
              {bookedCount}
            </span>
          </div>

          {!isDayClosed && (
            <div className="flex items-center gap-1.5">
              <span className="w-2.5 h-2.5 rounded-full bg-slate-400" />
              <span className="text-muted-foreground">Bloke / Kapalı:</span>
              <span className="font-bold text-foreground">
                {blockedCount}
              </span>
            </div>
          )}
        </div>

        <Button
          onClick={() => refetch()}
          variant="outline"
          size="sm"
          className="text-xs gap-1.5 h-7"
        >
          <RefreshCw className="w-3 h-3" />
          Yenile
        </Button>
      </div>

      {/* Slots Matrix */}
      <Card className="shadow-xs">
        <CardHeader className="pb-3 border-b border-border/50">
          <CardTitle className="text-sm font-bold flex items-center gap-2">
            <Clock className="w-4 h-4 text-primary" />
            <span>
              {selectedDate.toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} Saat Dilimleri
            </span>
          </CardTitle>
        </CardHeader>

        <CardContent className="pt-4">
          {loadingSlots ? (
            <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-6 gap-3">
              {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
                <div key={i} className="h-24 rounded-xl bg-muted/40 animate-pulse" />
              ))}
            </div>
          ) : isDayClosed && slots.length === 0 ? (
            <div className="p-12 text-center space-y-4">
              <div className="w-12 h-12 rounded-2xl bg-rose-500/10 border border-rose-500/20 flex items-center justify-center mx-auto text-rose-500">
                <Lock className="w-6 h-6" />
              </div>
              <div className="space-y-1.5">
                <h3 className="font-bold text-base text-foreground">
                  {selectedDate.toLocaleDateString("tr-TR", { weekday: "long" })} Günü Kapalıdır
                </h3>
                <p className="text-xs text-muted-foreground max-w-md mx-auto">
                  Kurumunuz bu gün haftalık çalışma takviminde kapalı olarak belirlenmiştir. Müsait randevu saati sunulmaz.
                </p>
              </div>
              <div className="pt-1">
                <Link href="/admin/availability">
                  <Button variant="outline" size="sm" className="text-xs gap-1.5">
                    Müsaitlik Ayarlarını Görüntüle / Düzenle
                  </Button>
                </Link>
              </div>
            </div>
          ) : slots.length === 0 ? (
            <div className="p-12 text-center space-y-3">
              <CalendarIcon className="w-10 h-10 text-muted-foreground/40 mx-auto" />
              <h3 className="font-bold text-sm">Bu gün için tanımlı slot bulunamadı</h3>
              <p className="text-xs text-muted-foreground max-w-sm mx-auto">
                Haftalık çalışma saatlerini belirleyip Müsaitlik sayfasından slot üretebilirsiniz.
              </p>
            </div>
          ) : (
            <div className="space-y-3">
              {isDayClosed && (
                <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-700 dark:text-amber-400 text-xs flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 shrink-0" />
                  <span>
                    Bu gün haftalık takvimde kapalıdır; ancak önceden rezerve edilmiş randevular aşağıda listelenmektedir.
                  </span>
                </div>
              )}
              <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
              {slots.map((slot) => {
                const isBlocked = slot.status === 4 || slot.isActive === false;
                const isBooked = slot.status === 3;
                const isReserved = slot.status === 2;

                return (
                  <div
                    key={slot.id}
                    className={`p-3.5 rounded-xl border transition-all flex flex-col justify-between space-y-3 ${
                      isBooked
                        ? "bg-indigo-500/10 border-indigo-500/40 text-foreground ring-1 ring-indigo-500/20"
                        : isReserved
                        ? "bg-amber-500/10 border-amber-500/40 text-foreground"
                        : isBlocked
                        ? "bg-muted/40 border-border text-muted-foreground opacity-60"
                        : "bg-surface border-border hover:border-primary/50 text-foreground"
                    }`}
                  >
                    <div className="flex items-start justify-between">
                      <div>
                        <div className="text-sm font-bold">
                          {slot.localStartTime || slot.localDisplay}
                        </div>
                        <div className="text-[10px] text-muted-foreground mt-0.5">
                          {slot.localEndTime ? `${slot.localStartTime} - ${slot.localEndTime}` : "30 dk"}
                        </div>
                      </div>

                      <Badge
                        variant="outline"
                        className={`text-[9px] py-0 px-1.5 ${
                          isBooked
                            ? "bg-indigo-500/20 text-indigo-700 dark:text-indigo-300 border-indigo-500/30"
                            : isReserved
                            ? "bg-amber-500/20 text-amber-700 dark:text-amber-300 border-amber-500/30"
                            : isBlocked
                            ? "bg-slate-500/10 text-slate-600 border-slate-500/20"
                            : "bg-emerald-500/10 text-emerald-600 border-emerald-500/20"
                        }`}
                      >
                        {isBooked ? "Dolu" : isReserved ? "Rezerve" : isBlocked ? "Bloke" : "Müsait"}
                      </Badge>
                    </div>

                    {/* Customer Information for Booked Slot */}
                    {isBooked && (
                      <div className="p-2 rounded-lg bg-indigo-500/10 border border-indigo-500/20 text-xs space-y-0.5">
                        <div className="font-bold text-indigo-900 dark:text-indigo-200 flex items-center gap-1.5 truncate">
                          <User className="w-3 h-3 shrink-0" />
                          <span className="truncate">{slot.customerName || "Kayıtlı Müşteri"}</span>
                        </div>
                        {slot.appointmentNumber && (
                          <div className="text-[10px] text-indigo-700/70 dark:text-indigo-300/70 font-mono">
                            No: {slot.appointmentNumber}
                          </div>
                        )}
                      </div>
                    )}

                    {/* Action Toggle */}
                    <div className="pt-2 border-t border-border/50 flex items-center justify-end text-xs">
                      {!isBooked && !isReserved ? (
                        <Button
                          onClick={() => toggleSlotMutation.mutate({ slotId: slot.id, isActive: isBlocked ? true : false })}
                          disabled={toggleSlotMutation.isPending && toggleSlotMutation.variables?.slotId === slot.id}
                          variant="ghost"
                          size="sm"
                          className="h-6 px-2 text-[10px] gap-1 hover:text-foreground"
                        >
                          {toggleSlotMutation.isPending && toggleSlotMutation.variables?.slotId === slot.id ? (
                            <RefreshCw className="w-3 h-3 animate-spin text-muted-foreground" />
                          ) : isBlocked ? (
                            <>
                              <Unlock className="w-3 h-3 text-emerald-600" />
                              <span className="text-emerald-600 font-semibold">Aç</span>
                            </>
                          ) : (
                            <>
                              <Lock className="w-3 h-3 text-rose-500" />
                              <span className="text-rose-500 font-semibold">Kapat</span>
                            </>
                          )}
                        </Button>
                      ) : (
                        <span className="text-[10px] font-medium text-indigo-600 dark:text-indigo-400">
                          {isBooked ? "Randevulu" : "Rezerve"}
                        </span>
                      )}
                    </div>
                  </div>
                );
              })}
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
