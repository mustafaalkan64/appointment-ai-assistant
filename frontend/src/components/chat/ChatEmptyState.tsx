"use client";

import React from "react";
import { Sparkles, Bot, Clock, Calendar, XCircle, HelpCircle } from "lucide-react";

interface ChatEmptyStateProps {
  organizationName?: string;
  onSelectPrompt: (promptText: string) => void;
}

export function ChatEmptyState({ organizationName, onSelectPrompt }: ChatEmptyStateProps) {
  const suggestedPrompts = [
    {
      title: "Müsait günleri ve saatleri göster",
      description: "Takvimdeki boş günleri ve uygun saat dilimlerini listele",
      icon: Clock,
      prompt: "Firmanın müsait günlerini ve saatlerini listeler misin?",
    },
    {
      title: "Yarın saat 16:00'da randevu al",
      description: "Yarın öğleden sonra 4 için hızlıca randevu oluştur",
      icon: Calendar,
      prompt: "Yarın öğleden sonra 4'te randevu almak istiyorum.",
    },
    {
      title: "Tüm randevularımı takip et",
      description: "Oluşturduğum ve iptal ettiğim tüm randevuları incele",
      icon: Sparkles,
      prompt: "Daha önce oluşturduğum ve iptal ettiğim randevularımı listele.",
    },
    {
      title: "Randevumu iptal etmek istiyorum",
      description: "Onay ekranı ile mevcut randevuyu güvenle iptal et",
      icon: XCircle,
      prompt: "Randevumu iptal etmek istiyorum.",
    },
    {
      title: "Çalışma saatleriniz nedir?",
      description: "Haftalık çalışma günlerini ve mesai saatlerini öğren",
      icon: HelpCircle,
      prompt: "Çalışma saatleriniz ve günleri nedir?",
    },
  ];

  return (
    <div className="max-w-2xl mx-auto py-8 px-4 flex flex-col items-center text-center space-y-6">
      {/* Bot Icon */}
      <div className="relative">
        <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-primary/20 to-indigo-500/20 text-primary flex items-center justify-center shadow-lg border border-primary/20">
          <Bot className="w-8 h-8" />
        </div>
        <div className="absolute -bottom-1 -right-1 w-5 h-5 rounded-full bg-emerald-500 ring-2 ring-surface flex items-center justify-center text-white text-[10px]">
          ✓
        </div>
      </div>

      {/* Greeting Text */}
      <div className="space-y-2">
        <h2 className="text-xl sm:text-2xl font-bold tracking-tight">
          Merhaba! 👋
        </h2>
        <p className="text-sm text-foreground/90 font-medium">
          {organizationName
            ? `${organizationName} AI Randevu Asistanına Hoş Geldiniz!`
            : "Size nasıl yardımcı olabilirim?"}
        </p>
        <p className="text-xs text-muted-foreground max-w-md mx-auto leading-relaxed">
          Randevu almak, uygun saatleri öğrenmek veya mevcut randevunuzu yönetmek için bana dilediğiniz gibi yazabilirsiniz.
        </p>
      </div>

      {/* Suggested Prompt Pills Grid */}
      <div className="w-full pt-2 space-y-2">
        <div className="text-[11px] font-semibold text-muted-foreground uppercase tracking-wider">
          Önerilen İstekler
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5 text-left">
          {suggestedPrompts.map((item, idx) => {
            const Icon = item.icon;
            return (
              <button
                key={idx}
                onClick={() => onSelectPrompt(item.prompt)}
                className="p-3.5 rounded-xl border border-border bg-surface hover:border-primary/50 hover:bg-primary/5 transition-all text-left group flex items-start gap-3 shadow-2xs hover:shadow-xs active:scale-98"
              >
                <div className="w-7 h-7 rounded-lg bg-primary/10 text-primary flex items-center justify-center shrink-0 group-hover:scale-110 transition-transform">
                  <Icon className="w-3.5 h-3.5" />
                </div>
                <div className="overflow-hidden">
                  <div className="text-xs font-semibold text-foreground group-hover:text-primary transition-colors">
                    {item.title}
                  </div>
                  <div className="text-[10px] text-muted-foreground truncate mt-0.5">
                    {item.description}
                  </div>
                </div>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}
