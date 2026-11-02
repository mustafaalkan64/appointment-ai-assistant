"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  CalendarCheck,
  Clock,
  Building2,
  XCircle,
  AlertTriangle,
  Sparkles,
  Search,
  CheckCircle2,
  CalendarDays
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter
} from "@/components/ui/dialog";
import { coreApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { AppointmentDetailDto } from "@/types";

export default function MyAppointmentsPage() {
  const queryClient = useQueryClient();
  const { session } = useAuth();
  const customerId = session?.id || "c63fa787-d893-4b6d-8372-f1abe6fe97aa";

  const [activeTab, setActiveTab] = useState("upcoming");
  const [selectedApptForCancel, setSelectedApptForCancel] = useState<AppointmentDetailDto | null>(null);
  const [cancelReason, setCancelReason] = useState("");

  const { data: appointments, isLoading } = useQuery({
    queryKey: ["customer-appointments", customerId],
    queryFn: () => coreApi.getCustomerAppointments(customerId)
  });

  const cancelMutation = useMutation({
    mutationFn: async ({ apptId, orgId, reason }: { apptId: string; orgId: string; reason: string }) => {
      const idempotencyKey = `web-cancel-${apptId}-${Date.now()}`;
      return coreApi.cancelAppointment(apptId, {
        organizationId: orgId,
        appointmentId: apptId,
        customerId,
        reason: reason || "Müşteri web paneli üzerinden iptal etti",
        idempotencyKey
      });
    },
    onSuccess: () => {
      toast.success("Randevunuz başarıyla iptal edildi.");
      setSelectedApptForCancel(null);
      setCancelReason("");
      queryClient.invalidateQueries({ queryKey: ["customer-appointments"] });
    },
    onError: (err: unknown) => {
      const msg = err instanceof Error ? err.message : "İptal işlemi gerçekleştirilemedi.";
      toast.error(msg);
    }
  });

  const isCancelled = (appt: AppointmentDetailDto) =>
    appt.status === "Cancelled" || appt.status === 4;

  const isAppointmentPast = (appt: AppointmentDetailDto) => {
    if (appt.status === "Completed" || appt.status === 3 || appt.status === "NoShow" || appt.status === 5) {
      return true;
    }
    const timeStr = appt.startUtc || appt.endUtc;
    if (!timeStr) return false;
    const time = new Date(timeStr).getTime();
    return !isNaN(time) && time <= Date.now();
  };

  const isCancellable = (appt: AppointmentDetailDto) => {
    if (isCancelled(appt) || isAppointmentPast(appt)) return false;
    return appt.status === "Confirmed" || appt.status === "Pending" || appt.status === 2 || appt.status === 1;
  };

  const allAppts = appointments || [];

  // Yaklaşan: Zamanı henüz gelmemiş, aktif randevular
  const upcomingAppts = allAppts
    .filter((a) => !isCancelled(a) && !isAppointmentPast(a) && (a.status === "Confirmed" || a.status === "Pending" || a.status === 2 || a.status === 1))
    .sort((a, b) => new Date(a.startUtc).getTime() - new Date(b.startUtc).getTime());

  // Geçmiş: Zamanı geçmiş olan veya tamamlanmış randevular
  const pastAppts = allAppts
    .filter((a) => !isCancelled(a) && (isAppointmentPast(a) || a.status === "Completed" || a.status === 3))
    .sort((a, b) => new Date(b.startUtc).getTime() - new Date(a.startUtc).getTime());

  // İptal Edilen randevular
  const cancelledAppts = allAppts
    .filter(isCancelled)
    .sort((a, b) => new Date(b.cancelledAt || b.startUtc || b.createdAt).getTime() - new Date(a.cancelledAt || a.startUtc || a.createdAt).getTime());

  const getStatusBadge = (appt: AppointmentDetailDto) => {
    if (isCancelled(appt)) return <Badge variant="danger">İptal Edildi</Badge>;
    if (appt.status === "Completed" || appt.status === 3) return <Badge variant="secondary">Tamamlandı</Badge>;
    if (isAppointmentPast(appt)) return <Badge variant="secondary">Zamanı Geçti</Badge>;
    if (appt.status === "Confirmed" || appt.status === 2) return <Badge variant="success">Onaylandı</Badge>;
    return <Badge variant="warning">Bekliyor</Badge>;
  };

  const renderAppointmentCard = (appt: AppointmentDetailDto) => {
    const cancellable = isCancellable(appt);
    const past = isAppointmentPast(appt);

    return (
      <Card key={appt.id} className="border-border hover:border-primary/40 transition-all p-5 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-border">
          <div className="flex items-center gap-2.5">
            <div className="w-9 h-9 rounded-lg bg-primary/10 text-primary flex items-center justify-center shrink-0">
              <Building2 className="w-5 h-5" />
            </div>
            <div>
              <h3 className="font-semibold text-sm text-foreground">{appt.organizationName || "Kurum"}</h3>
              <p className="text-xs text-muted-foreground font-mono">Randevu No: {appt.appointmentNumber}</p>
            </div>
          </div>
          <div>{getStatusBadge(appt)}</div>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
          <div className="flex items-center gap-2 text-foreground font-medium">
            <Clock className="w-4 h-4 text-primary shrink-0" />
            <span>{appt.localDisplay}</span>
          </div>

          {appt.cancelledAt && (
            <div className="text-danger flex items-center gap-1.5">
              <XCircle className="w-4 h-4 shrink-0" />
              <span>İptal Nedeni: {appt.cancellationReason || "Belirtilmedi"}</span>
            </div>
          )}
        </div>

        <div className="flex flex-wrap items-center justify-between pt-2 border-t border-border gap-2">
          <Link href="/app/chat">
            <Button variant="ghost" size="sm" className="text-xs text-primary gap-1.5 h-8">
              <Sparkles className="w-3.5 h-3.5" />
              <span>Asistan ile Yönet</span>
            </Button>
          </Link>

          {cancellable ? (
            <Button
              variant="outline"
              size="sm"
              className="text-xs text-danger hover:bg-danger/10 hover:text-danger h-8 border-danger/30"
              onClick={() => setSelectedApptForCancel(appt)}
            >
              Randevuyu İptal Et
            </Button>
          ) : past && !isCancelled(appt) ? (
            <span className="text-xs text-muted-foreground italic">
              Zamanı geçen randevular iptal edilemez
            </span>
          ) : null}
        </div>
      </Card>
    );
  };

  return (
    <div className="max-w-4xl mx-auto w-full p-4 sm:p-6 md:p-8 space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Randevularım</h1>
          <p className="text-sm text-muted-foreground">
            Geçmiş, yaklaşan ve iptal edilen randevularınızı inceleyin.
          </p>
        </div>

        <Link href="/app/chat">
          <Button className="gap-2 shadow-sm font-semibold">
            <Sparkles className="w-4 h-4" />
            <span>Yeni Randevu Al</span>
          </Button>
        </Link>
      </div>

      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList className="grid w-full grid-cols-3 max-w-md">
          <TabsTrigger value="upcoming">
            Yaklaşan ({upcomingAppts.length})
          </TabsTrigger>
          <TabsTrigger value="completed">
            Geçmiş ({pastAppts.length})
          </TabsTrigger>
          <TabsTrigger value="cancelled">
            İptal Edilen ({cancelledAppts.length})
          </TabsTrigger>
        </TabsList>

        {isLoading ? (
          <div className="py-12 text-center text-sm text-muted-foreground">
            Randevular listeleniyor...
          </div>
        ) : (
          <>
            <TabsContent value="upcoming" className="space-y-4 pt-4">
              {upcomingAppts.length === 0 ? (
                <Card className="p-8 text-center border-dashed">
                  <CalendarDays className="w-10 h-10 text-muted-foreground mx-auto mb-2 opacity-50" />
                  <p className="text-sm font-medium">Yaklaşan aktif randevunuz yok</p>
                  <p className="text-xs text-muted-foreground mt-1">
                    AI Asistan ile saniyeler içinde yeni randevu oluşturabilirsiniz.
                  </p>
                </Card>
              ) : (
                upcomingAppts.map(renderAppointmentCard)
              )}
            </TabsContent>

            <TabsContent value="completed" className="space-y-4 pt-4">
              {pastAppts.length === 0 ? (
                <Card className="p-8 text-center border-dashed">
                  <p className="text-sm text-muted-foreground">Geçmiş randevunuz bulunamadı.</p>
                </Card>
              ) : (
                pastAppts.map(renderAppointmentCard)
              )}
            </TabsContent>

            <TabsContent value="cancelled" className="space-y-4 pt-4">
              {cancelledAppts.length === 0 ? (
                <Card className="p-8 text-center border-dashed">
                  <p className="text-sm text-muted-foreground">İptal edilen randevunuz yok.</p>
                </Card>
              ) : (
                cancelledAppts.map(renderAppointmentCard)
              )}
            </TabsContent>
          </>
        )}
      </Tabs>

      {/* Cancellation Confirmation Dialog */}
      <Dialog open={!!selectedApptForCancel} onOpenChange={(open) => !open && setSelectedApptForCancel(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2 text-danger">
              <AlertTriangle className="w-5 h-5 shrink-0" />
              <span>Randevuyu İptal Etmek İstiyor Musunuz?</span>
            </DialogTitle>
            <DialogDescription>
              {selectedApptForCancel?.appointmentNumber} numaralı ({selectedApptForCancel?.localDisplay}) randevunuz iptal edilecek ve slot boşa çıkarılacaktır.
            </DialogDescription>
          </DialogHeader>

          <div className="py-2 space-y-2">
            <label className="text-xs font-medium text-foreground">İptal Nedeni (İsteğe Bağlı):</label>
            <input
              type="text"
              placeholder="Örn: Planım değişti"
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              className="w-full rounded-lg border border-input bg-surface px-3 py-2 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            />
          </div>

          <DialogFooter className="gap-2 sm:gap-0">
            <Button variant="ghost" onClick={() => setSelectedApptForCancel(null)}>
              Vazgeç
            </Button>
            <Button
              variant="danger"
              isLoading={cancelMutation.isPending}
              onClick={() => {
                if (selectedApptForCancel) {
                  cancelMutation.mutate({
                    apptId: selectedApptForCancel.id,
                    orgId: selectedApptForCancel.organizationId,
                    reason: cancelReason
                  });
                }
              }}
            >
              Evet, İptal Et
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
