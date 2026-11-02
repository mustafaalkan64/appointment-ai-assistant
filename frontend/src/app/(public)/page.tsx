"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { organizationApi } from "@/lib/api/client";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Sparkles,
  ArrowRight,
  Bot,
  Calendar,
  Clock,
  CheckCircle2,
  Building2,
  Users,
  ShieldCheck,
  ChevronDown,
  MessageSquare,
  Zap,
} from "lucide-react";

export default function LandingPage() {
  const [openFaq, setOpenFaq] = useState<number | null>(null);

  // Fetch featured organizations
  const { data: orgs, isLoading: orgsLoading } = useQuery({
    queryKey: ["public-organizations"],
    queryFn: () => organizationApi.getAll(),
  });

  const faqs = [
    {
      q: "AI Asistan ile randevu almak nasıl çalışır?",
      a: "Tıpkı bir insanla mesajlaşır gibi istediğiniz günü ve saati söylersiniz (örneğin 'Yarın öğleden sonra müsait saat var mı?'). AI asistan kurumun gerçek zamanlı müsaitlik takvimini kontrol eder, size alternatifleri sunar ve onayınızla anında randevunuzu kesinleştirir.",
    },
    {
      q: "İstediğim saat doluysa ne olur?",
      a: "Akıllı bekleme sırası (Waiting Queue) devreye girer. İsterseniz sizi o saat dilimi için bekleme listesine ekler; bir iptal gerçekleştiğinde sıranız geldiği anda size rezervasyon fırsatı sunar.",
    },
    {
      q: "Randevumu daha sonra değiştirebilir veya iptal edebilir miyim?",
      a: "Evet! Asistana 'Yarınki randevumu cuma saat 15:00'e ertelemek istiyorum' yazmanız yeterlidir. Eski randevunuz güvende tutulurken yeni saat teyit edilir ve tek adımda güncellenir.",
    },
    {
      q: "Kurumlar için panel ve takvim yönetimi nasıl yapılır?",
      a: "Kurum yöneticileri özel Admin Paneli üzerinden haftalık çalışma saatlerini belirleyebilir, tek tıkla randevu slotları oluşturabilir, slotları dilediğinde açıp kapatabilir ve tüm hasta/müşteri akışını izleyebilir.",
    },
  ];

  return (
    <div className="space-y-24 py-12 md:py-20">
      {/* Hero Section */}
      <section className="max-w-6xl mx-auto px-4 sm:px-8">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-12 items-center">
          <div className="lg:col-span-7 space-y-6 text-center lg:text-left">
            <div className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full border border-primary/20 bg-primary/5 text-primary text-xs font-semibold">
              <Sparkles className="w-3.5 h-3.5" />
              <span>Yapay Zeka Destekli Randevu Platformu</span>
            </div>

            <h1 className="text-4xl sm:text-5xl lg:text-6xl font-extrabold tracking-tight text-foreground leading-[1.1]">
              Your appointments, <br className="hidden sm:inline" />
              <span className="text-transparent bg-clip-text bg-gradient-to-r from-primary to-indigo-500">
                simply managed.
              </span>
            </h1>

            <p className="text-base sm:text-lg text-muted-foreground max-w-xl mx-auto lg:mx-0 leading-relaxed">
              Find the right organization, talk to our AI assistant and book your appointment in seconds.
              Karmaşık takvimler ve formlar yerine doğal dille konuşarak randevunuzu oluşturun.
            </p>

            <div className="flex flex-col sm:flex-row items-center justify-center lg:justify-start gap-3.5 pt-2">
              <Link href="/organizations" className="w-full sm:w-auto">
                <Button size="lg" className="w-full sm:w-auto gap-2 font-semibold shadow-md shadow-primary/20">
                  <Building2 className="w-4 h-4" />
                  Find an Organization
                  <ArrowRight className="w-4 h-4" />
                </Button>
              </Link>
              <Link href="/app/my-appointments" className="w-full sm:w-auto">
                <Button size="lg" variant="outline" className="w-full sm:w-auto gap-2">
                  <Calendar className="w-4 h-4" />
                  Manage My Appointments
                </Button>
              </Link>
            </div>

            <div className="pt-4 flex items-center justify-center lg:justify-start gap-6 text-xs text-muted-foreground">
              <div className="flex items-center gap-1.5">
                <CheckCircle2 className="w-4 h-4 text-emerald-500" />
                <span>24/7 Akıllı Asistan</span>
              </div>
              <div className="flex items-center gap-1.5">
                <CheckCircle2 className="w-4 h-4 text-emerald-500" />
                <span>Sıfır Çakışma Garantisi</span>
              </div>
              <div className="flex items-center gap-1.5">
                <CheckCircle2 className="w-4 h-4 text-emerald-500" />
                <span>Akıllı Bekleme Sırası</span>
              </div>
            </div>
          </div>

          {/* AI Interactive Assistant Preview */}
          <div className="lg:col-span-5">
            <div className="relative mx-auto max-w-md rounded-2xl border border-border bg-surface p-5 shadow-2xl shadow-primary/10">
              <div className="flex items-center justify-between border-b border-border pb-3.5 mb-4">
                <div className="flex items-center gap-2.5">
                  <div className="relative">
                    <div className="w-9 h-9 rounded-full bg-primary/10 flex items-center justify-center text-primary font-bold">
                      <Bot className="w-5 h-5" />
                    </div>
                    <span className="absolute bottom-0 right-0 w-2.5 h-2.5 rounded-full bg-emerald-500 ring-2 ring-surface" />
                  </div>
                  <div>
                    <div className="text-xs font-bold leading-none">AppointAI Assistant</div>
                    <div className="text-[10px] text-muted-foreground mt-0.5">Concurrency Clinic</div>
                  </div>
                </div>
                <Badge variant="outline" className="text-[10px] bg-primary/5 text-primary border-primary/20">
                  Canlı Önizleme
                </Badge>
              </div>

              {/* Chat Thread */}
              <div className="space-y-3 text-xs">
                {/* User Bubble */}
                <div className="flex justify-end">
                  <div className="bg-primary text-primary-foreground rounded-2xl rounded-tr-xs px-3.5 py-2 max-w-[85%] shadow-xs">
                    Yarın saat 14:00 civarı randevu alabilir miyim?
                  </div>
                </div>

                {/* Assistant Bubble */}
                <div className="flex items-start gap-2">
                  <div className="w-6 h-6 rounded-full bg-primary/10 flex items-center justify-center text-primary shrink-0 mt-0.5">
                    <Sparkles className="w-3.5 h-3.5" />
                  </div>
                  <div className="space-y-2 max-w-[88%]">
                    <div className="bg-muted text-foreground rounded-2xl rounded-tl-xs px-3.5 py-2">
                      Tabii! Yarın için müsait saatleri kontrol ettim. Aşağıdaki saatlerden hangisini tercih edersiniz?
                    </div>

                    {/* Interactive Slots Mockup */}
                    <div className="grid grid-cols-3 gap-1.5 pt-1">
                      <button className="px-2.5 py-1.5 rounded-lg border border-border bg-surface hover:border-primary/50 text-[11px] font-semibold text-center transition-all hover:bg-primary/5">
                        14:00
                      </button>
                      <button className="px-2.5 py-1.5 rounded-lg border-2 border-primary bg-primary/10 text-primary text-[11px] font-bold text-center">
                        14:30 ✓
                      </button>
                      <button className="px-2.5 py-1.5 rounded-lg border border-border bg-surface hover:border-primary/50 text-[11px] font-semibold text-center transition-all hover:bg-primary/5">
                        15:00
                      </button>
                    </div>

                    {/* Confirmation Card Preview */}
                    <div className="p-2.5 rounded-xl border border-primary/20 bg-primary/5 space-y-1.5 mt-2">
                      <div className="flex items-center justify-between text-[11px] font-semibold text-primary">
                        <span>Randevu Onayı</span>
                        <span>14:30</span>
                      </div>
                      <p className="text-[10px] text-muted-foreground">
                        2 Kasım Pazartesi • Concurrency Clinic
                      </p>
                      <Link href="/app/chat">
                        <Button size="sm" className="w-full text-xs h-7 mt-1 font-semibold">
                          Sohbetle Randevuyu Başlat
                        </Button>
                      </Link>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* How It Works */}
      <section className="bg-muted/40 border-y border-border py-16">
        <div className="max-w-6xl mx-auto px-4 sm:px-8 space-y-12">
          <div className="text-center max-w-2xl mx-auto space-y-3">
            <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">Nasıl Çalışır?</h2>
            <p className="text-muted-foreground text-sm">
              Geleneksel randevu takvimlerini unutun. Üç kolay adımda randevunuz hazır.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
            <div className="p-6 rounded-2xl bg-surface border border-border space-y-4 relative">
              <div className="w-12 h-12 rounded-xl bg-primary/10 text-primary flex items-center justify-center font-bold text-lg">
                1
              </div>
              <h3 className="text-base font-bold">Kurumunuzu Seçin</h3>
              <p className="text-xs text-muted-foreground leading-relaxed">
                Klinikler, özel muayenehaneler veya danışmanlık kurumları arasından hizmet almak istediğiniz kurumu bulun.
              </p>
            </div>

            <div className="p-6 rounded-2xl bg-surface border border-border space-y-4 relative">
              <div className="w-12 h-12 rounded-xl bg-primary/10 text-primary flex items-center justify-center font-bold text-lg">
                2
              </div>
              <h3 className="text-base font-bold">AI Asistan ile Konuşun</h3>
              <p className="text-xs text-muted-foreground leading-relaxed">
                İstediğiniz zaman dilimini kendi cümlelerinizle yazın. Asistan gerçek zamanlı slotları anında getirsin.
              </p>
            </div>

            <div className="p-6 rounded-2xl bg-surface border border-border space-y-4 relative">
              <div className="w-12 h-12 rounded-xl bg-primary/10 text-primary flex items-center justify-center font-bold text-lg">
                3
              </div>
              <h3 className="text-base font-bold">Tek Tıkla Onaylayın</h3>
              <p className="text-xs text-muted-foreground leading-relaxed">
                Önerilen saat kartından seçiminizi yapın ve onaylayın. Randevu numaranız ve detaylarınız anında cebinizde.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Featured Organizations */}
      <section className="max-w-6xl mx-auto px-4 sm:px-8 space-y-8">
        <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-4">
          <div>
            <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">Kurumları Keşfedin</h2>
            <p className="text-muted-foreground text-sm mt-1">
              Hemen bir kurum seçip yapay zeka asistanı ile randevu oluşturun.
            </p>
          </div>
          <Link href="/organizations">
            <Button variant="outline" size="sm" className="gap-1.5 text-xs">
              Tüm Kurumları Gör
              <ArrowRight className="w-3.5 h-3.5" />
            </Button>
          </Link>
        </div>

        {orgsLoading ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {[1, 2, 3].map((i) => (
              <div key={i} className="h-44 rounded-2xl bg-muted/40 animate-pulse border border-border" />
            ))}
          </div>
        ) : orgs && orgs.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {orgs.map((org) => (
              <Card key={org.id} className="hover:border-primary/50 transition-all flex flex-col justify-between">
                <CardHeader>
                  <div className="flex items-start justify-between gap-2">
                    <CardTitle className="text-base font-bold">{org.name}</CardTitle>
                    <Badge variant="outline" className="text-[10px] bg-emerald-500/10 text-emerald-600 border-emerald-500/20">
                      Açık
                    </Badge>
                  </div>
                  <p className="text-xs text-muted-foreground line-clamp-2 mt-1">
                    {org.description || "Sağlık ve danışmanlık alanında randevu hizmeti."}
                  </p>
                </CardHeader>
                <CardContent className="pt-0 space-y-3">
                  <div className="text-[11px] text-muted-foreground space-y-1">
                    <div className="flex items-center gap-1.5">
                      <Clock className="w-3.5 h-3.5 text-primary" />
                      <span>{org.timeZoneId}</span>
                    </div>
                  </div>
                  <div className="pt-2 flex items-center gap-2">
                    <Link href={`/app/chat/${org.slug}`} className="flex-1">
                      <Button size="sm" className="w-full text-xs gap-1.5">
                        <MessageSquare className="w-3.5 h-3.5" />
                        AI ile Randevu Al
                      </Button>
                    </Link>
                    <Link href={`/organization/${org.slug}`}>
                      <Button size="sm" variant="outline" className="text-xs">
                        Detay
                      </Button>
                    </Link>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        ) : (
          <div className="p-8 rounded-2xl border border-dashed border-border text-center text-sm text-muted-foreground">
            Kayıtlı kurum bulunamadı.
          </div>
        )}
      </section>

      {/* Benefits */}
      <section className="bg-primary/5 border-y border-primary/10 py-16">
        <div className="max-w-6xl mx-auto px-4 sm:px-8 space-y-12">
          <div className="text-center max-w-2xl mx-auto space-y-2">
            <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">Neden AppointAI?</h2>
            <p className="text-muted-foreground text-sm">
              Müşteriler için kesintisiz deneyim, kurumlar için tam kontrol.
            </p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
            <div className="p-5 rounded-xl bg-surface border border-border space-y-2">
              <Zap className="w-6 h-6 text-primary" />
              <h4 className="font-bold text-sm">Saniyeler İçinde Randevu</h4>
              <p className="text-xs text-muted-foreground">
                Saatlerce takvim incelemek yerine tek bir mesajla uygun randevunuzu oluşturun.
              </p>
            </div>
            <div className="p-5 rounded-xl bg-surface border border-border space-y-2">
              <ShieldCheck className="w-6 h-6 text-emerald-500" />
              <h4 className="font-bold text-sm">Çift Rezervasyon Yok</h4>
              <p className="text-xs text-muted-foreground">
                Gerçek zamanlı concurrency ve locking mimarisi sayesinde çakışma ihtimali sıfırdır.
              </p>
            </div>
            <div className="p-5 rounded-xl bg-surface border border-border space-y-2">
              <Users className="w-6 h-6 text-indigo-500" />
              <h4 className="font-bold text-sm">Akıllı Bekleme Sırası</h4>
              <p className="text-xs text-muted-foreground">
                Yoğun saatlerde bekleme listesine katılın, iptal olduğunda ilk sizin haberiniz olsun.
              </p>
            </div>
            <div className="p-5 rounded-xl bg-surface border border-border space-y-2">
              <Bot className="w-6 h-6 text-violet-500" />
              <h4 className="font-bold text-sm">Doğal Dil Anlayışı</h4>
              <p className="text-xs text-muted-foreground">
                Türkçe konuşan zeki asistan niyetinizi anlar ve doğru slotları önünüze getirir.
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Frequently Asked Questions */}
      <section className="max-w-3xl mx-auto px-4 sm:px-8 space-y-6">
        <div className="text-center space-y-2">
          <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">Sıkça Sorulan Sorular</h2>
          <p className="text-muted-foreground text-sm">Platform hakkında aklınıza takılan soruların yanıtları.</p>
        </div>

        <div className="space-y-3 pt-4">
          {faqs.map((faq, idx) => (
            <div
              key={idx}
              className="rounded-xl border border-border bg-surface overflow-hidden transition-all"
            >
              <button
                onClick={() => setOpenFaq(openFaq === idx ? null : idx)}
                className="w-full p-4 text-left font-semibold text-sm flex items-center justify-between gap-4 hover:bg-muted/50 transition-colors"
              >
                <span>{faq.q}</span>
                <ChevronDown
                  className={`w-4 h-4 text-muted-foreground transition-transform ${
                    openFaq === idx ? "rotate-180" : ""
                  }`}
                />
              </button>
              {openFaq === idx && (
                <div className="px-4 pb-4 pt-1 text-xs text-muted-foreground leading-relaxed border-t border-border/50">
                  {faq.a}
                </div>
              )}
            </div>
          ))}
        </div>
      </section>

      {/* Organization CTA */}
      <section className="max-w-5xl mx-auto px-4 sm:px-8">
        <div className="rounded-3xl bg-gradient-to-br from-primary to-indigo-700 text-primary-foreground p-8 sm:p-12 text-center space-y-6 shadow-xl">
          <h2 className="text-2xl sm:text-4xl font-extrabold tracking-tight">
            Kurumunuz için AI Destekli Randevu Altyapısını Başlatın
          </h2>
          <p className="text-primary-foreground/80 max-w-xl mx-auto text-sm sm:text-base leading-relaxed">
            Dakikalar içinde kurum profilinizi açın, haftalık çalışma saatlerinizi tanımlayın ve müşterilerinize 7/24 AI destekli randevu deneyimi sunun.
          </p>
          <div className="flex flex-col sm:flex-row items-center justify-center gap-3">
            <Link href="/register">
              <Button size="lg" variant="secondary" className="font-bold text-xs sm:text-sm">
                Kurumunuzu Kaydedin
              </Button>
            </Link>
            <Link href="/login">
              <Button size="lg" variant="outline" className="border-primary-foreground/30 text-primary-foreground hover:bg-primary-foreground/10 text-xs sm:text-sm">
                Yönetici Girişi
              </Button>
            </Link>
          </div>
        </div>
      </section>
    </div>
  );
}
