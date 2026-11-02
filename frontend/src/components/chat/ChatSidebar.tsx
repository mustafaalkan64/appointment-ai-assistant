"use client";

import React from "react";
import Link from "next/link";
import { MessageSquare, Plus, Sparkles, Building2, Calendar, ChevronRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";

export interface ConversationMeta {
  id: string;
  title: string;
  organizationName?: string;
  organizationSlug?: string;
  updatedAt: string;
  preview: string;
}

interface ChatSidebarProps {
  conversations: ConversationMeta[];
  activeConversationId: string | null;
  onSelectConversation: (id: string) => void;
  onNewConversation: () => void;
  currentOrgName?: string;
  currentOrgSlug?: string;
}

export function ChatSidebar({
  conversations,
  activeConversationId,
  onSelectConversation,
  onNewConversation,
  currentOrgName,
  currentOrgSlug,
}: ChatSidebarProps) {
  return (
    <aside className="w-72 border-r border-border bg-surface/50 flex flex-col h-full shrink-0">
      {/* Top Action */}
      <div className="p-4 border-b border-border space-y-3">
        <Button
          onClick={onNewConversation}
          className="w-full gap-2 text-xs font-semibold shadow-xs"
        >
          <Plus className="w-4 h-4" />
          Yeni Sohbet Başlat
        </Button>

        {currentOrgName && (
          <div className="p-2.5 rounded-xl border border-primary/20 bg-primary/5 flex items-center justify-between">
            <div className="flex items-center gap-2 overflow-hidden">
              <Building2 className="w-4 h-4 text-primary shrink-0" />
              <div className="truncate">
                <div className="text-[10px] text-muted-foreground uppercase font-bold tracking-wider">Seçili Kurum</div>
                <div className="text-xs font-semibold truncate text-foreground">{currentOrgName}</div>
              </div>
            </div>
            {currentOrgSlug && (
              <Link href={`/organization/${currentOrgSlug}`}>
                <ChevronRight className="w-4 h-4 text-muted-foreground hover:text-foreground" />
              </Link>
            )}
          </div>
        )}
      </div>

      {/* Conversation List */}
      <div className="flex-1 overflow-y-auto p-3 space-y-1">
        <div className="px-2 py-1.5 text-[11px] font-semibold text-muted-foreground uppercase tracking-wider">
          Geçmiş Görüşmeler
        </div>

        {conversations.length === 0 ? (
          <div className="p-4 text-center text-xs text-muted-foreground space-y-2">
            <MessageSquare className="w-6 h-6 mx-auto text-muted-foreground/40" />
            <p>Henüz kayıtlı bir görüşmeniz bulunmuyor.</p>
          </div>
        ) : (
          conversations.map((conv) => {
            const isActive = conv.id === activeConversationId;
            return (
              <button
                key={conv.id}
                onClick={() => onSelectConversation(conv.id)}
                className={`w-full text-left p-3 rounded-xl transition-all space-y-1 group ${
                  isActive
                    ? "bg-primary/10 border border-primary/30 text-foreground"
                    : "hover:bg-muted text-muted-foreground hover:text-foreground"
                }`}
              >
                <div className="flex items-center justify-between gap-1">
                  <span className="text-xs font-semibold truncate group-hover:text-primary transition-colors">
                    {conv.title || "Randevu Talebi"}
                  </span>
                  <span className="text-[10px] text-muted-foreground whitespace-nowrap">
                    {conv.updatedAt}
                  </span>
                </div>
                <p className="text-[11px] text-muted-foreground truncate leading-relaxed">
                  {conv.preview || "Sohbet detayı..."}
                </p>
                {conv.organizationName && (
                  <Badge variant="outline" className="text-[9px] py-0 px-1 font-normal text-muted-foreground">
                    {conv.organizationName}
                  </Badge>
                )}
              </button>
            );
          })
        )}
      </div>

      {/* Footer Info */}
      <div className="p-3 border-t border-border bg-surface/80">
        <Link
          href="/app/my-appointments"
          className="flex items-center justify-between p-2 rounded-lg hover:bg-muted text-xs text-muted-foreground hover:text-foreground transition-colors"
        >
          <div className="flex items-center gap-2">
            <Calendar className="w-4 h-4 text-primary" />
            <span className="font-medium">Randevularımı İncele</span>
          </div>
          <ChevronRight className="w-3.5 h-3.5" />
        </Link>
      </div>
    </aside>
  );
}
