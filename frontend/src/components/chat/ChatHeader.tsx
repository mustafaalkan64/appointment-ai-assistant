"use client";

import React from "react";
import Link from "next/link";
import { Bot, Sparkles, Building2, Plus, ArrowLeft } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";

interface ChatHeaderProps {
  organizationName?: string;
  organizationSlug?: string;
  onNewConversation: () => void;
  showBack?: boolean;
}

export function ChatHeader({
  organizationName = "Genel Asistan",
  organizationSlug,
  onNewConversation,
  showBack = false,
}: ChatHeaderProps) {
  return (
    <div className="h-16 border-b border-border bg-surface/90 backdrop-blur-md px-4 sm:px-6 flex items-center justify-between sticky top-0 z-20">
      <div className="flex items-center gap-3">
        {showBack && (
          <Link href="/app/home" className="md:hidden text-muted-foreground hover:text-foreground">
            <ArrowLeft className="w-5 h-5" />
          </Link>
        )}

        <div className="relative">
          <div className="w-10 h-10 rounded-xl bg-primary/10 flex items-center justify-center text-primary font-bold shadow-xs">
            <Bot className="w-5 h-5" />
          </div>
          <span className="absolute bottom-0 right-0 w-2.5 h-2.5 rounded-full bg-emerald-500 ring-2 ring-surface" />
        </div>

        <div>
          <div className="flex items-center gap-2">
            <h2 className="text-sm font-bold text-foreground leading-tight">
              {organizationName}
            </h2>
            <Badge variant="outline" className="text-[10px] bg-emerald-500/10 text-emerald-600 border-emerald-500/20 py-0 px-1.5 h-4">
              Çevrimiçi
            </Badge>
          </div>
          <p className="text-[11px] text-muted-foreground flex items-center gap-1 mt-0.5">
            <Sparkles className="w-3 h-3 text-primary" />
            <span>AI Randevu Asistanı</span>
          </p>
        </div>
      </div>

      <div className="flex items-center gap-2">
        {organizationSlug && (
          <Link href={`/organization/${organizationSlug}`} className="hidden sm:inline-block">
            <Button variant="outline" size="sm" className="text-xs gap-1.5 h-8">
              <Building2 className="w-3.5 h-3.5" />
              Kurum Bilgisi
            </Button>
          </Link>
        )}

        <Button
          onClick={onNewConversation}
          variant="outline"
          size="sm"
          className="text-xs gap-1.5 h-8 bg-surface hover:bg-muted font-medium"
        >
          <Plus className="w-3.5 h-3.5" />
          <span className="hidden sm:inline">Yeni Sohbet</span>
        </Button>
      </div>
    </div>
  );
}
