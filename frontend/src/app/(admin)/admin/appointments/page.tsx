"use client";

import React, { useState, useMemo } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { appointmentApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { AppointmentDetailDto, AppointmentStatus } from "@/types";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Calendar,
  Search,
  Filter,
  CheckCircle2,
  XCircle,
  Clock,
  User,
  AlertTriangle,
  RefreshCw,
  Eye,
  X,
} from "lucide-react";
import { toast } from "sonner";

export default function AdminAppointmentsPage() {
  const { session } = useAuth();
  const queryClient = useQueryClient();
  const orgId = session?.organizationId || "5f8a475a-bcc0-4241-8955-c3d13acee320";

  const [searchQuery, setSearchQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<string>("ALL");
  const [selectedAppointment, setSelectedAppointment] = useState<AppointmentDetailDto | null>(null);
  const [isDetailOpen, setIsDetailOpen] = useState(false);
  const [isCancelConfirmOpen, setIsCancelConfirmOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState("");

  // Fetch organization appointments
  const {
    data: appointments,
    isLoading,
    refetch,
  } = useQuery({
    queryKey: ["admin-appointments", orgId],
    queryFn: () => appointmentApi.getOrganizationAppointments(orgId),
  });

  // Cancel Appointment Mutation
  const cancelMutation = useMutation({
    mutationFn: async ({ apptId, reason }: { apptId: string; reason?: string }) => {
      return appointmentApi.cancel({
        organizationId: orgId,
        appointmentId: apptId,
        customerId: selectedAppointment?.customerId || session?.id || "c63fa787-d893-4b6d-8372-f1abe6fe97aa",
        reason: reason || "Yönetici tarafından iptal edildi",
        idempotencyKey: "admin-cancel-" + Date.now(),
      });
    },
    onSuccess: () => {
      toast.success("Randevu başarıyla iptal edildi.");
      setIsCancelConfirmOpen(false);
      setIsDetailOpen(false);
      queryClient.invalidateQueries({ queryKey: ["admin-appointments"] });
    },
    onError: (err: unknown) => {
      const errorMsg = err instanceof Error ? err.message : "Randevu iptal edilemedi.";
      toast.error(errorMsg);
    },
  });

  // Filtered Appointments
  const filteredAppointments = useMemo(() => {
    if (!appointments) return [];

    return appointments.filter((appt) => {
      const matchesSearch =
        appt.appointmentNumber.toLowerCase().includes(searchQuery.toLowerCase()) ||
        (appt.customerName && appt.customerName.toLowerCase().includes(searchQuery.toLowerCase())) ||
        (appt.customerPhone && appt.customerPhone.includes(searchQuery));

      const matchesStatus =
        statusFilter === "ALL" ||
        String(appt.status).toUpperCase() === statusFilter.toUpperCase();

      return matchesSearch && matchesStatus;
    });
  }, [appointments, searchQuery, statusFilter]);

  const handleOpenDetail = (appt: AppointmentDetailDto) => {
    setSelectedAppointment(appt);
    setIsDetailOpen(true);
  };

  const handleOpenCancel = (appt: AppointmentDetailDto) => {
    setSelectedAppointment(appt);
    setCancelReason("");
    setIsCancelConfirmOpen(true);
  };

  return (
    <div className="space-y-6">
      {/* Title */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Randevu Yönetimi
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Kuruma ait tüm randevuları filtreleyin, arayın ve durumlarını güncelleyin
          </p>
        </div>

        <Button
          onClick={() => refetch()}
          variant="outline"
          size="sm"
          className="text-xs gap-1.5 h-9"
        >
          <RefreshCw className="w-3.5 h-3.5" />
          Yenile
        </Button>
      </div>

      {/* Filter and Search Bar */}
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3 p-4 rounded-xl border border-border bg-surface shadow-xs">
        <div className="relative flex-1 max-w-md">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Randevu No, Müşteri adı veya telefon ile ara..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="pl-9 text-xs h-9"
          />
        </div>

        {/* Status Filter Buttons */}
        <div className="flex items-center gap-1.5 overflow-x-auto pb-1 sm:pb-0">
          <Filter className="w-3.5 h-3.5 text-muted-foreground mr-1 hidden sm:inline" />
          {[
            { key: "ALL", label: "Tümü" },
            { key: "CONFIRMED", label: "Onaylı" },
            { key: "PENDING", label: "Bekleyen" },
            { key: "COMPLETED", label: "Tamamlandı" },
            { key: "CANCELLED", label: "İptal" },
          ].map((tab) => (
            <button
              key={tab.key}
              onClick={() => setStatusFilter(tab.key)}
              className={`text-xs px-3 py-1.5 rounded-lg transition-all font-medium whitespace-nowrap ${
                statusFilter === tab.key
                  ? "bg-primary text-primary-foreground shadow-xs"
                  : "bg-surface border border-border text-muted-foreground hover:text-foreground"
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>
      </div>

      {/* Appointments List / Table */}
      <Card className="shadow-xs overflow-hidden">
        <CardContent className="p-0">
          {isLoading ? (
            <div className="p-6 space-y-3">
              {[1, 2, 3, 4, 5].map((i) => (
                <div key={i} className="h-12 bg-muted/40 rounded-xl animate-pulse" />
              ))}
            </div>
          ) : filteredAppointments.length === 0 ? (
            <div className="p-12 text-center space-y-3">
              <Calendar className="w-10 h-10 text-muted-foreground/30 mx-auto" />
              <h3 className="font-bold text-sm">Randevu bulunamadı</h3>
              <p className="text-xs text-muted-foreground">
                Arama kriterlerine uygun randevu kaydı bulunmamaktadır.
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="text-[11px] uppercase tracking-wider text-muted-foreground border-b border-border bg-muted/20">
                  <tr>
                    <th className="py-3 px-4">Randevu No</th>
                    <th className="py-3 px-4">Müşteri</th>
                    <th className="py-3 px-4">Tarih & Saat</th>
                    <th className="py-3 px-4">Durum</th>
                    <th className="py-3 px-4">Oluşturulma</th>
                    <th className="py-3 px-4 text-right">İşlemler</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/60">
                  {filteredAppointments.map((appt) => {
                    const statusStr = String(appt.status).toLowerCase();
                    const isConfirmed = statusStr === "confirmed" || statusStr === "2";
                    const isCancelled = statusStr === "cancelled" || statusStr === "4";

                    return (
                      <tr key={appt.id} className="hover:bg-muted/30 transition-colors">
                        <td className="py-3.5 px-4 font-mono font-bold text-foreground">
                          {appt.appointmentNumber}
                        </td>
                        <td className="py-3.5 px-4">
                          <div className="font-semibold text-foreground">
                            {appt.customerName || "Ali Yılmaz"}
                          </div>
                          <div className="text-[10px] text-muted-foreground">
                            {appt.customerPhone || "+90 555 999 88 77"}
                          </div>
                        </td>
                        <td className="py-3.5 px-4 font-medium text-foreground">
                          {appt.localDisplay}
                        </td>
                        <td className="py-3.5 px-4">
                          <Badge
                            variant="outline"
                            className={`text-[10px] py-0.5 px-2 ${
                              isConfirmed
                                ? "bg-emerald-500/10 text-emerald-600 border-emerald-500/20"
                                : isCancelled
                                ? "bg-rose-500/10 text-rose-600 border-rose-500/20"
                                : "bg-primary/10 text-primary border-primary/20"
                            }`}
                          >
                            {String(appt.status)}
                          </Badge>
                        </td>
                        <td className="py-3.5 px-4 text-muted-foreground text-[11px]">
                          {new Date(appt.createdAt).toLocaleDateString("tr-TR")}
                        </td>
                        <td className="py-3.5 px-4 text-right space-x-1.5">
                          <Button
                            onClick={() => handleOpenDetail(appt)}
                            variant="ghost"
                            size="sm"
                            className="h-7 text-xs px-2"
                          >
                            <Eye className="w-3.5 h-3.5 mr-1" />
                            Detay
                          </Button>

                          {!isCancelled && (
                            <Button
                              onClick={() => handleOpenCancel(appt)}
                              variant="ghost"
                              size="sm"
                              className="h-7 text-xs px-2 text-rose-600 hover:text-rose-700 hover:bg-rose-50 dark:hover:bg-rose-950/20"
                            >
                              İptal Et
                            </Button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Appointment Detail Dialog */}
      <Dialog open={isDetailOpen} onOpenChange={setIsDetailOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="text-base font-bold">Randevu Detayları</DialogTitle>
            <DialogDescription className="text-xs">
              Randevu No: {selectedAppointment?.appointmentNumber}
            </DialogDescription>
          </DialogHeader>

          {selectedAppointment && (
            <div className="space-y-3 text-xs py-2">
              <div className="p-3 rounded-xl bg-muted/40 border border-border/80 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-muted-foreground">Müşteri:</span>
                  <span className="font-semibold text-foreground">
                    {selectedAppointment.customerName || "Ali Yılmaz"}
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-muted-foreground">Telefon:</span>
                  <span className="font-medium">
                    {selectedAppointment.customerPhone || "+90 555 999 88 77"}
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-muted-foreground">Tarih & Saat:</span>
                  <span className="font-bold text-primary">
                    {selectedAppointment.localDisplay}
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="text-muted-foreground">Durum:</span>
                  <Badge variant="outline" className="text-[10px]">
                    {String(selectedAppointment.status)}
                  </Badge>
                </div>
                {selectedAppointment.cancelledAt && (
                  <div className="flex items-center justify-between text-rose-600 pt-1 border-t border-border/50">
                    <span>İptal Nedeni:</span>
                    <span>{selectedAppointment.cancellationReason || "Belirtilmedi"}</span>
                  </div>
                )}
              </div>
            </div>
          )}

          <DialogFooter className="gap-2 sm:gap-0">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIsDetailOpen(false)}
              className="text-xs"
            >
              Kapat
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Cancel Confirmation Dialog */}
      <Dialog open={isCancelConfirmOpen} onOpenChange={setIsCancelConfirmOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="text-base font-bold text-rose-600 flex items-center gap-2">
              <AlertTriangle className="w-5 h-5" />
              Randevuyu İptal Et
            </DialogTitle>
            <DialogDescription className="text-xs">
              {selectedAppointment?.appointmentNumber} numaralı randevu iptal edilecektir.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-3 py-2 text-xs">
            <label className="font-medium text-foreground">İptal Gerekçesi (Opsiyonel)</label>
            <Input
              placeholder="Örn: Müşteri talebi, doktor izinli vb."
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              className="text-xs h-9"
            />
          </div>

          <DialogFooter className="gap-2 sm:gap-0">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIsCancelConfirmOpen(false)}
              className="text-xs"
            >
              Vazgeç
            </Button>
            <Button
              variant="destructive"
              size="sm"
              disabled={cancelMutation.isPending}
              onClick={() =>
                selectedAppointment &&
                cancelMutation.mutate({
                  apptId: selectedAppointment.id,
                  reason: cancelReason,
                })
              }
              className="text-xs font-semibold"
            >
              {cancelMutation.isPending ? "İptal Ediliyor..." : "İptali Onayla"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
