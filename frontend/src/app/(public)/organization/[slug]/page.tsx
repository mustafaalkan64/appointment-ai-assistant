"use client";

import React, { use, Suspense } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  Building2,
  MapPin,
  Phone,
  Mail,
  Clock,
  Sparkles,
  ArrowLeft,
  CalendarCheck,
  CheckCircle2,
  ChevronRight
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { coreApi } from "@/lib/api/client";

const DAY_NAMES = ["Pazar", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi"];

function OrganizationDetailContent({
  params
}: {
  params: Promise<{ slug: string }>;
}) {
  const { slug } = use(params);

  const { data: org, isLoading, error } = useQuery({
    queryKey: ["organization-detail", slug],
    queryFn: () => coreApi.getOrganizationBySlug(slug)
  });

  if (isLoading) {
    return (
      <div className="max-w-4xl mx-auto p-8 text-center text-sm text-muted-foreground">
        Kurum bilgileri yükleniyor...
      </div>
    );
  }

  if (error || !org) {
    return (
      <div className="max-w-md mx-auto p-8 text-center space-y-4">
        <Building2 className="w-12 h-12 text-muted-foreground mx-auto opacity-50" />
        <h2 className="text-lg font-bold">Kurum Bulunamadı</h2>
        <p className="text-xs text-muted-foreground">
          Aradığınız kurum sistemde bulunamadı veya bağlantı adresi geçersiz.
        </p>
        <Link href="/organizations">
          <Button variant="outline" size="sm">Kurumlara Geri Dön</Button>
        </Link>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto w-full p-4 sm:p-6 md:p-8 space-y-6">
      {/* Back Link */}
      <Link href="/organizations" className="inline-flex items-center gap-1.5 text-xs text-muted-foreground hover:text-foreground">
        <ArrowLeft className="w-3.5 h-3.5" />
        <span>Tüm Kurumlara Dön</span>
      </Link>

      {/* Header Profile Card */}
      <div className="rounded-2xl border border-border bg-surface p-6 sm:p-8 shadow-sm flex flex-col sm:flex-row sm:items-center justify-between gap-6">
        <div className="flex items-start gap-4">
          <div className="w-16 h-16 rounded-2xl bg-primary/10 text-primary flex items-center justify-center font-bold text-2xl shrink-0 border border-primary/20">
            {org.name[0]}
          </div>
          <div className="space-y-1">
            <div className="flex items-center gap-2.5">
              <h1 className="text-2xl font-bold tracking-tight text-foreground">{org.name}</h1>
              <Badge variant={org.isActive ? "success" : "secondary"}>
                {org.isActive ? "Açık / Hizmette" : "Kapalı"}
              </Badge>
            </div>
            <p className="text-xs text-muted-foreground font-mono">@{org.slug}</p>
            <p className="text-sm text-muted-foreground pt-1">
              Yapay zeka asistanı ile doğrudan konuşarak hızlıca randevu alabilirsiniz.
            </p>
          </div>
        </div>

        {/* Primary CTA Button */}
        <Link href={`/app/chat/${org.slug}`} className="shrink-0">
          <Button size="lg" className="w-full sm:w-auto gap-2.5 font-bold shadow-md shadow-primary/20">
            <Sparkles className="w-4 h-4 fill-current" />
            <span>Sohbetle Randevu Al</span>
          </Button>
        </Link>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Contact & Location Info */}
        <Card className="md:col-span-1 border-border">
          <CardHeader>
            <CardTitle className="text-base">İletişim & Konum</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3.5 text-xs">
            {org.address && (
              <div className="flex items-start gap-2.5">
                <MapPin className="w-4 h-4 text-primary shrink-0 mt-0.5" />
                <span className="text-foreground leading-relaxed">{org.address}</span>
              </div>
            )}
            {org.phoneNumber && (
              <div className="flex items-center gap-2.5">
                <Phone className="w-4 h-4 text-primary shrink-0" />
                <span className="text-foreground font-mono">{org.phoneNumber}</span>
              </div>
            )}
            {org.email && (
              <div className="flex items-center gap-2.5">
                <Mail className="w-4 h-4 text-primary shrink-0" />
                <span className="text-foreground">{org.email}</span>
              </div>
            )}
            {org.timeZone && (
              <div className="flex items-center gap-2.5">
                <Clock className="w-4 h-4 text-primary shrink-0" />
                <span className="text-muted-foreground">Saat Dilimi: {org.timeZone}</span>
              </div>
            )}
          </CardContent>
        </Card>

        {/* Weekly Availability Schedule */}
        <Card className="md:col-span-2 border-border">
          <CardHeader>
            <CardTitle className="text-base">Haftalık Çalışma Saatleri</CardTitle>
            <CardDescription className="text-xs">
              Kurumun randevu kabul ettiği standart gün ve saat aralıkları
            </CardDescription>
          </CardHeader>
          <CardContent>
            {org.weeklyAvailabilities && org.weeklyAvailabilities.length > 0 ? (
              <div className="divide-y divide-border text-xs">
                {org.weeklyAvailabilities.map((w) => (
                  <div key={w.dayOfWeek} className="py-2.5 flex items-center justify-between">
                    <span className="font-medium text-foreground w-28">
                      {w.dayName || DAY_NAMES[w.dayOfWeek]}
                    </span>
                    {w.isActive ? (
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-foreground font-semibold">
                          {w.startTime.slice(0, 5)} - {w.endTime.slice(0, 5)}
                        </span>
                        <span className="text-[10px] text-muted-foreground">({w.slotDurationMinutes} dk)</span>
                      </div>
                    ) : (
                      <span className="text-muted-foreground">Kapalı</span>
                    )}
                  </div>
                ))}
              </div>
            ) : (
              <p className="text-xs text-muted-foreground">Haftalık takvim bilgisi henüz tanımlanmamış.</p>
            )}

            <div className="mt-5 p-3 rounded-lg bg-accent/40 border border-primary/20 text-xs flex items-center justify-between">
              <span className="text-muted-foreground">Bu kurum için randevu saati bakmak ister misiniz?</span>
              <Link href={`/app/chat/${org.slug}`}>
                <Button size="sm" variant="default" className="gap-1.5 text-xs h-7">
                  <Sparkles className="w-3 h-3" />
                  <span>Saatleri Sor</span>
                </Button>
              </Link>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

export default function OrganizationDetailPage({
  params,
}: {
  params: Promise<{ slug: string }>;
}) {
  return (
    <Suspense
      fallback={
        <div className="max-w-4xl mx-auto p-8 text-center text-sm text-muted-foreground">
          Kurum detayları yükleniyor...
        </div>
      }
    >
      <OrganizationDetailContent params={params} />
    </Suspense>
  );
}
