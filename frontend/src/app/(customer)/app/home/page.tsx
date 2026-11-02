"use client";

import React from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  Sparkles,
  CalendarCheck,
  Building2,
  Clock,
  ArrowRight,
  MessageSquare,
  CheckCircle2,
  AlertCircle
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { coreApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { formatDate } from "@/lib/utils";

export default function CustomerHomePage() {
  const { session } = useAuth();
  const customerId = session?.id || "c63fa787-d893-4b6d-8372-f1abe6fe97aa";

  // Fetch customer appointments
  const { data: appointments, isLoading } = useQuery({
    queryKey: ["customer-appointments", customerId],
    queryFn: () => coreApi.getCustomerAppointments(customerId)
  });

  const upcomingAppts = appointments?.filter((a) => {
    const isCancelled = a.status === "Cancelled" || a.status === 4;
    const isCompleted = a.status === "Completed" || a.status === 3 || a.status === "NoShow" || a.status === 5;
    if (isCancelled || isCompleted) return false;
    const time = a.startUtc ? new Date(a.startUtc).getTime() : 0;
    return time > Date.now() && (a.status === "Confirmed" || a.status === "Pending" || a.status === 2 || a.status === 1);
  }) || [];

  return (
    <div className="max-w-5xl mx-auto w-full p-4 sm:p-6 md:p-8 space-y-6">
      {/* Welcome Banner with conversational prompt */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-primary/90 via-primary to-indigo-950 text-primary-foreground p-6 sm:p-8 shadow-xl">
        <div className="relative z-10 max-w-2xl space-y-3">
          <Badge variant="outline" className="bg-primary-foreground/15 border-primary-foreground/20 text-white font-medium">
            <Sparkles className="w-3 h-3 mr-1 fill-current" />
            Yapay Zeka Destekli Randevu
          </Badge>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Merhaba, {session?.fullName || "Ziyaretçi"} 👋
          </h1>
          <p className="text-primary-foreground/80 text-sm sm:text-base leading-relaxed">
            Karmaşık formlarla vakit kaybetmeyin. Doğal dilde yazarak randevu oluşturun, saatleri öğrenin veya mevcut randevularınızı yönetin.
          </p>

          <div className="pt-2 flex flex-wrap gap-3">
            <Link href="/app/chat">
              <Button size="lg" className="bg-white text-primary hover:bg-white/90 shadow-md font-semibold gap-2">
                <MessageSquare className="w-4 h-4 fill-primary" />
                <span>AI Asistan ile Randevu Al</span>
              </Button>
            </Link>
            <Link href="/app/organizations">
              <Button size="lg" variant="outline" className="border-white/30 text-white hover:bg-white/10 font-medium">
                Kurumları İncele
              </Button>
            </Link>
          </div>
        </div>

        {/* Decorative background glow */}
        <div className="absolute right-0 top-0 bottom-0 w-1/3 bg-radial from-white/10 to-transparent pointer-events-none" />
      </div>

      {/* Quick Action Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Link href="/app/chat" className="group">
          <Card className="h-full border-border hover:border-primary/50 transition-all hover:shadow-md cursor-pointer">
            <CardHeader className="pb-3">
              <div className="w-10 h-10 rounded-lg bg-primary/10 text-primary flex items-center justify-center mb-2 group-hover:scale-105 transition-transform">
                <Sparkles className="w-5 h-5 fill-primary/20" />
              </div>
              <CardTitle className="text-base group-hover:text-primary transition-colors">
                AI Asistan
              </CardTitle>
              <CardDescription className="text-xs">
                Müsait saatleri anında sorgulayın ve tek mesajla randevu oluşturun.
              </CardDescription>
            </CardHeader>
          </Card>
        </Link>

        <Link href="/app/my-appointments" className="group">
          <Card className="h-full border-border hover:border-primary/50 transition-all hover:shadow-md cursor-pointer">
            <CardHeader className="pb-3">
              <div className="w-10 h-10 rounded-lg bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center mb-2 group-hover:scale-105 transition-transform">
                <CalendarCheck className="w-5 h-5" />
              </div>
              <CardTitle className="text-base group-hover:text-primary transition-colors">
                Randevularım ({upcomingAppts.length})
              </CardTitle>
              <CardDescription className="text-xs">
                Yaklaşan randevularınızı görüntüleyin veya gerekirse iptal edin.
              </CardDescription>
            </CardHeader>
          </Card>
        </Link>

        <Link href="/app/organizations" className="group">
          <Card className="h-full border-border hover:border-primary/50 transition-all hover:shadow-md cursor-pointer">
            <CardHeader className="pb-3">
              <div className="w-10 h-10 rounded-lg bg-blue-500/10 text-blue-600 dark:text-blue-400 flex items-center justify-center mb-2 group-hover:scale-105 transition-transform">
                <Building2 className="w-5 h-5" />
              </div>
              <CardTitle className="text-base group-hover:text-primary transition-colors">
                Kurumları Keşfet
              </CardTitle>
              <CardDescription className="text-xs">
                Klinik, poliklinik ve hizmet noktalarını inceleyip randevu başlatın.
              </CardDescription>
            </CardHeader>
          </Card>
        </Link>
      </div>

      {/* Upcoming Appointments Section */}
      <Card className="border-border">
        <CardHeader className="flex flex-row items-center justify-between pb-3">
          <div>
            <CardTitle className="text-lg">Yaklaşan Randevularınız</CardTitle>
            <CardDescription className="text-xs">
              Sistemde adınıza kayıtlı olan aktif randevu bilgileriniz
            </CardDescription>
          </div>
          <Link href="/app/my-appointments">
            <Button variant="ghost" size="sm" className="text-xs text-primary gap-1">
              <span>Tümünü Gör</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </Button>
          </Link>
        </CardHeader>

        <CardContent>
          {isLoading ? (
            <div className="py-8 text-center text-sm text-muted-foreground">
              Randevular yükleniyor...
            </div>
          ) : upcomingAppts.length === 0 ? (
            <div className="py-8 text-center space-y-3">
              <div className="w-12 h-12 rounded-full bg-muted flex items-center justify-center mx-auto text-muted-foreground">
                <Clock className="w-6 h-6" />
              </div>
              <p className="text-sm font-medium text-foreground">Yaklaşan randevunuz bulunmuyor</p>
              <p className="text-xs text-muted-foreground max-w-sm mx-auto">
                AI Asistan ile sohbet ederek birkaç saniye içinde ilk randevunuzu oluşturabilirsiniz.
              </p>
              <Link href="/app/chat">
                <Button size="sm" className="gap-2">
                  <Sparkles className="w-3.5 h-3.5" />
                  <span>Hemen Randevu Al</span>
                </Button>
              </Link>
            </div>
          ) : (
            <div className="divide-y divide-border">
              {upcomingAppts.slice(0, 3).map((appt) => (
                <div key={appt.id} className="py-3 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                  <div className="flex items-start gap-3">
                    <div className="w-9 h-9 rounded-lg bg-primary/10 text-primary flex items-center justify-center shrink-0 mt-0.5">
                      <CheckCircle2 className="w-4 h-4" />
                    </div>
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-sm text-foreground">
                          {appt.organizationName || "Sağlık Merkezi"}
                        </span>
                        <Badge variant="success">Onaylandı</Badge>
                      </div>
                      <p className="text-xs text-muted-foreground font-mono mt-0.5">
                        No: {appt.appointmentNumber} • {appt.localDisplay}
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 self-end sm:self-center">
                    <Link href={`/app/chat`}>
                      <Button variant="outline" size="sm" className="text-xs h-8">
                        Detay / Asistana Sor
                      </Button>
                    </Link>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
