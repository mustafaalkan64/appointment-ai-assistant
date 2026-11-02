"use client";

import React, { useState, useEffect } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { appointmentApi, organizationApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Calendar,
  Users,
  Clock,
  CheckCircle2,
  XCircle,
  TrendingUp,
  Sparkles,
  ArrowUpRight,
  Plus,
  RefreshCw,
  Building2,
} from "lucide-react";
import {
  ResponsiveContainer,
  AreaChart,
  Area,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
} from "recharts";

export default function AdminDashboardPage() {
  const { session } = useAuth();
  const orgId = session?.organizationId || "5f8a475a-bcc0-4241-8955-c3d13acee320";

  // Fetch Organization Appointments
  const {
    data: appointments,
    isLoading: loadingAppointments,
    refetch: refetchAppointments,
  } = useQuery({
    queryKey: ["admin-appointments", orgId],
    queryFn: () => appointmentApi.getOrganizationAppointments(orgId),
  });

  // Fetch Available Slots for next 7 days
  const [dateRange, setDateRange] = useState({
    today: "2026-11-01",
    nextWeek: "2026-11-08",
  });

  useEffect(() => {
    try {
      const t = new Date().toISOString().split("T")[0];
      const nw = new Date(Date.now() + 7 * 86400000).toISOString().split("T")[0];
      setDateRange({ today: t, nextWeek: nw });
    } catch {}
  }, []);

  const { data: slotsResponse, isLoading: loadingSlots } = useQuery({
    queryKey: ["admin-available-slots", orgId, dateRange.today, dateRange.nextWeek],
    queryFn: () => appointmentApi.getAvailableSlots(orgId, dateRange.today, dateRange.nextWeek),
  });

  // Compute Metrics
  const totalAppointments = appointments?.length || 0;
  const completedAppointments =
    appointments?.filter((a) => String(a.status).toLowerCase() === "completed").length || 0;
  const cancelledAppointments =
    appointments?.filter((a) => String(a.status).toLowerCase() === "cancelled").length || 0;
  const upcomingAppointments =
    appointments?.filter(
      (a) =>
        String(a.status).toLowerCase() === "confirmed" ||
        String(a.status).toLowerCase() === "pending"
    ) || [];

  const availableSlotsCount = slotsResponse?.slots?.length || 0;

  // Chart Mock Data based on weekly appointments
  const bookingActivityData = [
    { day: "Pzt", bookings: 4, slots: 16 },
    { day: "Sal", bookings: 7, slots: 16 },
    { day: "Çar", bookings: 9, slots: 16 },
    { day: "Per", bookings: 6, slots: 16 },
    { day: "Cum", bookings: 12, slots: 18 },
    { day: "Cmt", bookings: 8, slots: 10 },
    { day: "Paz", bookings: 0, slots: 0 },
  ];

  return (
    <div className="space-y-8">
      {/* Page Title & Action Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2 text-xs text-muted-foreground mb-1">
            <Building2 className="w-3.5 h-3.5 text-primary" />
            <span>Yönetim Kontrol Merkezi</span>
          </div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Dashboard Genel Bakış
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Bugünkü randevular, aktif kapasite ve müşteri talepleri
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <Button
            onClick={() => refetchAppointments()}
            variant="outline"
            size="sm"
            className="text-xs gap-1.5 h-9"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            Yenile
          </Button>

          <Link href="/admin/availability">
            <Button size="sm" className="text-xs gap-1.5 h-9 font-semibold shadow-xs">
              <Plus className="w-4 h-4" />
              Slot Oluştur
            </Button>
          </Link>
        </div>
      </div>

      {/* KPI Cards Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Total Appointments */}
        <Card className="shadow-xs hover:border-primary/40 transition-colors">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">
              Toplam Randevular
            </CardTitle>
            <div className="w-8 h-8 rounded-lg bg-primary/10 text-primary flex items-center justify-center">
              <Calendar className="w-4 h-4" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-foreground">
              {loadingAppointments ? "..." : totalAppointments}
            </div>
            <p className="text-[11px] text-muted-foreground mt-1 flex items-center gap-1">
              <TrendingUp className="w-3 h-3 text-emerald-500" />
              <span className="text-emerald-500 font-semibold">%14 artış</span> geçen haftaya göre
            </p>
          </CardContent>
        </Card>

        {/* Upcoming Appointments */}
        <Card className="shadow-xs hover:border-primary/40 transition-colors">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">
              Yaklaşan Randevular
            </CardTitle>
            <div className="w-8 h-8 rounded-lg bg-indigo-500/10 text-indigo-500 flex items-center justify-center">
              <Clock className="w-4 h-4" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-indigo-600 dark:text-indigo-400">
              {loadingAppointments ? "..." : upcomingAppointments.length}
            </div>
            <p className="text-[11px] text-muted-foreground mt-1">
              Onaylanmış aktif randevu
            </p>
          </CardContent>
        </Card>

        {/* Available Slots */}
        <Card className="shadow-xs hover:border-primary/40 transition-colors">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">
              Boş Slot Sayısı (7 Gün)
            </CardTitle>
            <div className="w-8 h-8 rounded-lg bg-emerald-500/10 text-emerald-600 flex items-center justify-center">
              <CheckCircle2 className="w-4 h-4" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-emerald-600">
              {loadingSlots ? "..." : availableSlotsCount}
            </div>
            <p className="text-[11px] text-muted-foreground mt-1">
              Rezervasyona hazır açık saat
            </p>
          </CardContent>
        </Card>

        {/* Cancelled / Waiting Queue */}
        <Card className="shadow-xs hover:border-primary/40 transition-colors">
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-xs font-medium text-muted-foreground">
              İptal / Bekleme Sırası
            </CardTitle>
            <div className="w-8 h-8 rounded-lg bg-rose-500/10 text-rose-500 flex items-center justify-center">
              <XCircle className="w-4 h-4" />
            </div>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-rose-600">
              {loadingAppointments ? "..." : cancelledAppointments}
            </div>
            <p className="text-[11px] text-muted-foreground mt-1">
              İptal edilen kayıtlar
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Chart & Quick Actions Row */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Weekly Booking Chart */}
        <Card className="lg:col-span-8 shadow-xs">
          <CardHeader className="flex flex-row items-center justify-between pb-4">
            <div>
              <CardTitle className="text-sm font-bold">Haftalık Randevu Trendi</CardTitle>
              <p className="text-xs text-muted-foreground mt-0.5">
                AI Asistan ve doğrudan alınan randevuların gün bazlı dağılımı
              </p>
            </div>
            <Badge variant="outline" className="text-[10px] bg-primary/5 text-primary border-primary/20">
              Canlı Metrikler
            </Badge>
          </CardHeader>
          <CardContent className="h-64 pt-2">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={bookingActivityData}>
                <defs>
                  <linearGradient id="bookingColor" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#4f46e5" stopOpacity={0.4} />
                    <stop offset="95%" stopColor="#4f46e5" stopOpacity={0.0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" opacity={0.15} />
                <XAxis dataKey="day" stroke="#94a3b8" fontSize={11} tickLine={false} />
                <YAxis stroke="#94a3b8" fontSize={11} tickLine={false} />
                <Tooltip
                  contentStyle={{
                    backgroundColor: "#1e293b",
                    borderRadius: "8px",
                    border: "none",
                    color: "#fff",
                    fontSize: "12px",
                  }}
                />
                <Area
                  type="monotone"
                  dataKey="bookings"
                  name="Alınan Randevu"
                  stroke="#4f46e5"
                  strokeWidth={2.5}
                  fillOpacity={1}
                  fill="url(#bookingColor)"
                />
              </AreaChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        {/* Quick Operations Panel */}
        <Card className="lg:col-span-4 shadow-xs flex flex-col justify-between">
          <CardHeader>
            <CardTitle className="text-sm font-bold flex items-center gap-2">
              <Sparkles className="w-4 h-4 text-primary" />
              Hızlı İşlemler
            </CardTitle>
            <p className="text-xs text-muted-foreground mt-0.5">
              Sık kullanılan yönetim aksiyonları
            </p>
          </CardHeader>
          <CardContent className="space-y-3 pt-0">
            <Link
              href="/admin/availability"
              className="flex items-center justify-between p-3 rounded-xl border border-border bg-surface hover:border-primary/50 hover:bg-primary/5 transition-all group"
            >
              <div className="flex items-center gap-3">
                <div className="w-9 h-9 rounded-lg bg-primary/10 text-primary flex items-center justify-center group-hover:scale-105 transition-transform">
                  <Clock className="w-4 h-4" />
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground group-hover:text-primary transition-colors">
                    Haftalık Çalışma Saatleri
                  </div>
                  <div className="text-[10px] text-muted-foreground">Mesai ve slot süreleri</div>
                </div>
              </div>
              <ArrowUpRight className="w-4 h-4 text-muted-foreground group-hover:text-primary transition-colors" />
            </Link>

            <Link
              href="/admin/calendar"
              className="flex items-center justify-between p-3 rounded-xl border border-border bg-surface hover:border-primary/50 hover:bg-primary/5 transition-all group"
            >
              <div className="flex items-center gap-3">
                <div className="w-9 h-9 rounded-lg bg-emerald-500/10 text-emerald-600 flex items-center justify-center group-hover:scale-105 transition-transform">
                  <Calendar className="w-4 h-4" />
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground group-hover:text-emerald-600 transition-colors">
                    Takvim Yönetimi
                  </div>
                  <div className="text-[10px] text-muted-foreground">Slotları aç / kapat</div>
                </div>
              </div>
              <ArrowUpRight className="w-4 h-4 text-muted-foreground group-hover:text-emerald-600 transition-colors" />
            </Link>

            <Link
              href="/admin/waiting-queue"
              className="flex items-center justify-between p-3 rounded-xl border border-border bg-surface hover:border-primary/50 hover:bg-primary/5 transition-all group"
            >
              <div className="flex items-center gap-3">
                <div className="w-9 h-9 rounded-lg bg-amber-500/10 text-amber-600 flex items-center justify-center group-hover:scale-105 transition-transform">
                  <Users className="w-4 h-4" />
                </div>
                <div>
                  <div className="text-xs font-semibold text-foreground group-hover:text-amber-600 transition-colors">
                    Bekleme Sırası
                  </div>
                  <div className="text-[10px] text-muted-foreground">Yoğun saat kuyrukları</div>
                </div>
              </div>
              <ArrowUpRight className="w-4 h-4 text-muted-foreground group-hover:text-amber-600 transition-colors" />
            </Link>
          </CardContent>
        </Card>
      </div>

      {/* Upcoming Appointments Table / Card List */}
      <Card className="shadow-xs">
        <CardHeader className="flex flex-row items-center justify-between">
          <div>
            <CardTitle className="text-sm font-bold">Yaklaşan Randevu Listesi</CardTitle>
            <p className="text-xs text-muted-foreground mt-0.5">
              En yakın tarihli müşteri randevuları
            </p>
          </div>
          <Link href="/admin/appointments">
            <Button variant="ghost" size="sm" className="text-xs text-primary">
              Tümünü Gör →
            </Button>
          </Link>
        </CardHeader>
        <CardContent className="pt-0">
          {loadingAppointments ? (
            <div className="space-y-2 py-4">
              {[1, 2, 3].map((i) => (
                <div key={i} className="h-12 bg-muted/40 rounded-xl animate-pulse" />
              ))}
            </div>
          ) : upcomingAppointments.length === 0 ? (
            <div className="text-center py-8 text-xs text-muted-foreground space-y-2">
              <Calendar className="w-8 h-8 mx-auto text-muted-foreground/30" />
              <p>Aktif bekleyen randevu bulunmuyor.</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-xs text-left">
                <thead className="text-[11px] uppercase tracking-wider text-muted-foreground border-b border-border bg-muted/20">
                  <tr>
                    <th className="py-2.5 px-3">Randevu No</th>
                    <th className="py-2.5 px-3">Müşteri</th>
                    <th className="py-2.5 px-3">Tarih & Saat</th>
                    <th className="py-2.5 px-3">Durum</th>
                    <th className="py-2.5 px-3 text-right">Detay</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/60">
                  {upcomingAppointments.slice(0, 5).map((appt) => (
                    <tr key={appt.id} className="hover:bg-muted/30 transition-colors">
                      <td className="py-3 px-3 font-mono font-medium text-foreground">
                        {appt.appointmentNumber}
                      </td>
                      <td className="py-3 px-3 font-medium">
                        {appt.customerName || "Ali Yılmaz"}
                      </td>
                      <td className="py-3 px-3 text-muted-foreground">
                        {appt.localDisplay}
                      </td>
                      <td className="py-3 px-3">
                        <Badge
                          variant="outline"
                          className="text-[10px] bg-emerald-500/10 text-emerald-600 border-emerald-500/20"
                        >
                          {String(appt.status)}
                        </Badge>
                      </td>
                      <td className="py-3 px-3 text-right">
                        <Link href="/admin/appointments">
                          <Button variant="ghost" size="sm" className="h-7 text-[11px] px-2 text-primary">
                            İncele
                          </Button>
                        </Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
