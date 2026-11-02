"use client";

import React, { useState, useMemo } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { customerApi, appointmentApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { Customer } from "@/types";
import { Card, CardContent } from "@/components/ui/card";
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
  Search,
  Phone,
  Mail,
  Calendar,
  CheckCircle2,
  Clock,
  User,
  ExternalLink,
  RefreshCw,
  AlertCircle,
} from "lucide-react";

export default function AdminCustomersPage() {
  const { session } = useAuth();
  const orgId = session?.organizationId || "5f8a475a-bcc0-4241-8955-c3d13acee320";

  const [searchQuery, setSearchQuery] = useState("");
  const [selectedCustomer, setSelectedCustomer] = useState<Customer | null>(null);
  const [isAppointmentsDialogOpen, setIsAppointmentsDialogOpen] = useState(false);

  // Fetch real customers for the organization
  const {
    data: apiCustomers,
    isLoading: loadingCustomers,
    refetch: refetchCustomers,
  } = useQuery({
    queryKey: ["admin-customers", orgId],
    queryFn: () => customerApi.getOrganizationCustomers(orgId),
  });

  // Fallback demo customers if database has no records yet
  const defaultCustomers: Customer[] = useMemo(
    () => [
      {
        id: "c63fa787-d893-4b6d-8372-f1abe6fe97aa",
        fullName: "Ali Yılmaz",
        phoneNumber: "+905559998877",
        email: "ali.yilmaz@example.com",
        isPhoneVerified: true,
        createdAt: "2026-10-15T10:00:00Z",
        appointmentCount: 3,
      },
    ],
    []
  );

  const customers = (apiCustomers && apiCustomers.length > 0) ? apiCustomers : defaultCustomers;

  // Filter customers by search term
  const filtered = useMemo(() => {
    return customers.filter(
      (c) =>
        c.fullName.toLowerCase().includes(searchQuery.toLowerCase()) ||
        c.phoneNumber.includes(searchQuery) ||
        c.email.toLowerCase().includes(searchQuery.toLowerCase())
    );
  }, [customers, searchQuery]);

  // Fetch appointments for the selected customer
  const {
    data: customerAppointments,
    isLoading: loadingAppointments,
  } = useQuery({
    queryKey: ["customer-appointments", selectedCustomer?.id, orgId],
    queryFn: () =>
      selectedCustomer ? appointmentApi.getCustomerAppointments(selectedCustomer.id, orgId) : Promise.resolve([]),
    enabled: !!selectedCustomer && isAppointmentsDialogOpen,
  });

  const handleOpenAppointments = (customer: Customer) => {
    setSelectedCustomer(customer);
    setIsAppointmentsDialogOpen(true);
  };

  return (
    <div className="space-y-6">
      {/* Title & Stats */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Müşteri Yönetimi
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Kurumdan randevu alan veya sıraya giren kayıtlı müşteriler
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            onClick={() => refetchCustomers()}
            variant="outline"
            size="sm"
            className="text-xs h-8 gap-1.5"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loadingCustomers ? "animate-spin" : ""}`} />
            Yenile
          </Button>

          <Badge variant="outline" className="text-xs py-1 px-3 bg-primary/5 text-primary border-primary/20">
            Toplam: {customers.length} Müşteri
          </Badge>
        </div>
      </div>

      {/* Search Bar */}
      <div className="relative max-w-md">
        <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
        <Input
          placeholder="İsim, telefon veya e-posta ile ara..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          className="pl-9 text-xs h-9"
        />
      </div>

      {/* Customer List Card */}
      <Card className="shadow-xs overflow-hidden">
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-xs text-left">
              <thead className="text-[11px] uppercase tracking-wider text-muted-foreground border-b border-border bg-muted/20">
                <tr>
                  <th className="py-3 px-4">Müşteri</th>
                  <th className="py-3 px-4">İletişim</th>
                  <th className="py-3 px-4">Telefon Doğrulama</th>
                  <th className="py-3 px-4">Kayıt Tarihi</th>
                  <th className="py-3 px-4 text-right">İşlemler</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60">
                {filtered.map((c) => (
                  <tr key={c.id} className="hover:bg-muted/30 transition-colors">
                    <td className="py-3.5 px-4">
                      <div className="flex items-center gap-2.5">
                        <div className="w-8 h-8 rounded-full bg-primary/10 text-primary flex items-center justify-center font-bold text-xs">
                          {c.fullName ? c.fullName[0].toUpperCase() : "M"}
                        </div>
                        <div>
                          <div className="font-semibold text-foreground flex items-center gap-1.5">
                            <span>{c.fullName}</span>
                            {c.appointmentCount !== undefined && c.appointmentCount > 0 && (
                              <Badge variant="secondary" className="text-[9px] py-0 px-1.5 h-4 bg-primary/10 text-primary font-mono">
                                {c.appointmentCount} Randevu
                              </Badge>
                            )}
                          </div>
                          <div className="text-[10px] text-muted-foreground font-mono">
                            {c.id.slice(0, 8)}...
                          </div>
                        </div>
                      </div>
                    </td>
                    <td className="py-3.5 px-4 space-y-0.5">
                      <div className="flex items-center gap-1.5 text-muted-foreground">
                        <Phone className="w-3 h-3 text-primary" />
                        <span>{c.phoneNumber}</span>
                      </div>
                      <div className="flex items-center gap-1.5 text-muted-foreground">
                        <Mail className="w-3 h-3 text-muted-foreground" />
                        <span>{c.email}</span>
                      </div>
                    </td>
                    <td className="py-3.5 px-4">
                      {c.isPhoneVerified ? (
                        <Badge
                          variant="outline"
                          className="text-[10px] bg-emerald-500/10 text-emerald-600 border-emerald-500/20 gap-1"
                        >
                          <CheckCircle2 className="w-3 h-3" />
                          Doğrulandı
                        </Badge>
                      ) : (
                        <Badge
                          variant="outline"
                          className="text-[10px] bg-amber-500/10 text-amber-600 border-amber-500/20"
                        >
                          Bekliyor
                        </Badge>
                      )}
                    </td>
                    <td className="py-3.5 px-4 text-muted-foreground text-[11px]">
                      {new Date(c.createdAt).toLocaleDateString("tr-TR")}
                    </td>
                    <td className="py-3.5 px-4 text-right">
                      <Button
                        onClick={() => handleOpenAppointments(c)}
                        variant="ghost"
                        size="sm"
                        className="h-7 text-xs px-2.5 text-primary hover:bg-primary/10 gap-1.5 font-semibold"
                      >
                        <Calendar className="w-3.5 h-3.5" />
                        Randevuları Gör
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      {/* Customer Appointments Modal */}
      <Dialog open={isAppointmentsDialogOpen} onOpenChange={setIsAppointmentsDialogOpen}>
        <DialogContent className="sm:max-w-xl max-h-[85vh] flex flex-col">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2 text-base font-bold">
              <Calendar className="w-4 h-4 text-primary" />
              <span>{selectedCustomer?.fullName} - Randevuları</span>
            </DialogTitle>
            <DialogDescription className="text-xs text-muted-foreground">
              {selectedCustomer?.phoneNumber} • {selectedCustomer?.email}
            </DialogDescription>
          </DialogHeader>

          <div className="flex-1 overflow-y-auto py-2 space-y-3">
            {loadingAppointments ? (
              <div className="space-y-2 py-4">
                {[1, 2, 3].map((i) => (
                  <div key={i} className="h-16 rounded-xl bg-muted/40 animate-pulse" />
                ))}
              </div>
            ) : !customerAppointments || customerAppointments.length === 0 ? (
              <div className="py-12 text-center space-y-2">
                <Calendar className="w-8 h-8 text-muted-foreground/40 mx-auto" />
                <p className="text-sm font-semibold">Kayıtlı randevu bulunamadı</p>
                <p className="text-xs text-muted-foreground">
                  Bu müşteriye ait aktif veya geçmiş randevu kaydı bulunmamaktadır.
                </p>
              </div>
            ) : (
              <div className="space-y-2.5">
                {customerAppointments.map((appt) => {
                  const statusStr = String(appt.status).toUpperCase();
                  const isConfirmed = statusStr === "CONFIRMED" || statusStr === "2";
                  const isPending = statusStr === "PENDING" || statusStr === "1";
                  const isCancelled = statusStr === "CANCELLED" || statusStr === "3";
                  const isCompleted = statusStr === "COMPLETED" || statusStr === "4";

                  return (
                    <div
                      key={appt.id}
                      className="p-3 rounded-xl border border-border bg-surface hover:border-primary/40 transition-all flex items-center justify-between gap-3 text-xs"
                    >
                      <div className="space-y-1">
                        <div className="flex items-center gap-2">
                          <span className="font-mono font-bold text-foreground">
                            {appt.appointmentNumber}
                          </span>
                          <Badge
                            variant="outline"
                            className={`text-[10px] py-0 px-1.5 ${
                              isConfirmed
                                ? "bg-emerald-500/10 text-emerald-600 border-emerald-500/20"
                                : isPending
                                ? "bg-amber-500/10 text-amber-600 border-amber-500/20"
                                : isCancelled
                                ? "bg-rose-500/10 text-rose-600 border-rose-500/20"
                                : "bg-indigo-500/10 text-indigo-600 border-indigo-500/20"
                            }`}
                          >
                            {isConfirmed
                              ? "Onaylandı"
                              : isPending
                              ? "Onay Bekliyor"
                              : isCancelled
                              ? "İptal Edildi"
                              : isCompleted
                              ? "Tamamlandı"
                              : appt.status}
                          </Badge>
                        </div>

                        <div className="flex items-center gap-1.5 text-muted-foreground">
                          <Clock className="w-3.5 h-3.5 text-primary shrink-0" />
                          <span className="font-medium text-foreground">{appt.localDisplay}</span>
                        </div>

                        {appt.description && (
                          <div className="text-[11px] text-muted-foreground">
                            Not: {appt.description}
                          </div>
                        )}
                        {appt.cancellationReason && (
                          <div className="text-[10px] text-rose-500">
                            İptal Nedeni: {appt.cancellationReason}
                          </div>
                        )}
                      </div>

                      <div className="flex items-center gap-1">
                        <Link href={`/admin/appointments?search=${encodeURIComponent(appt.appointmentNumber)}`}>
                          <Button variant="outline" size="sm" className="h-7 text-[11px] px-2 gap-1">
                            <ExternalLink className="w-3 h-3" />
                            Detay
                          </Button>
                        </Link>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          <DialogFooter className="border-t border-border/50 pt-3 flex flex-row items-center justify-between sm:justify-between">
            <Link href={`/admin/appointments?search=${encodeURIComponent(selectedCustomer?.fullName || "")}`}>
              <Button variant="ghost" size="sm" className="text-xs text-primary gap-1">
                <ExternalLink className="w-3 h-3" />
                Tüm Randevularda Gör
              </Button>
            </Link>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setIsAppointmentsDialogOpen(false)}
              className="text-xs"
            >
              Kapat
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
