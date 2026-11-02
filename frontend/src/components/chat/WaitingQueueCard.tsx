"use client";

import React from "react";
import { Users, Clock, AlertCircle } from "lucide-react";
import { Badge } from "@/components/ui/badge";

interface WaitingQueueCardProps {
  position?: number;
  totalInQueue?: number;
  requestedDate?: string;
  requestedTime?: string;
  status?: string;
}

export function WaitingQueueCard({
  position = 1,
  totalInQueue = 3,
  requestedDate = "2 Kasım 2026",
  requestedTime = "14:00",
  status = "Waiting",
}: WaitingQueueCardProps) {
  return (
    <div className="rounded-2xl border border-amber-500/30 bg-amber-500/5 p-4 shadow-md space-y-3 max-w-md mt-3">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div className="w-8 h-8 rounded-lg bg-amber-500/10 text-amber-600 flex items-center justify-center">
            <Users className="w-4 h-4" />
          </div>
          <div>
            <h4 className="text-xs font-bold text-foreground">Bekleme Listesine Eklendiniz</h4>
            <p className="text-[10px] text-muted-foreground">İptal olduğunda ilk size haber verilecek</p>
          </div>
        </div>
        <Badge variant="outline" className="text-[10px] bg-amber-500/10 text-amber-700 border-amber-500/20">
          Sıradasınız
        </Badge>
      </div>

      <div className="p-3 rounded-xl bg-surface border border-border/80 flex items-center justify-around text-center">
        <div>
          <div className="text-[10px] text-muted-foreground uppercase tracking-wider font-semibold">Sıra Numaranız</div>
          <div className="text-xl font-extrabold text-amber-600">#{position}</div>
        </div>
        <div className="h-8 w-px bg-border" />
        <div>
          <div className="text-[10px] text-muted-foreground uppercase tracking-wider font-semibold">İstenen Saat</div>
          <div className="text-xs font-bold text-foreground">{requestedTime}</div>
        </div>
        <div className="h-8 w-px bg-border" />
        <div>
          <div className="text-[10px] text-muted-foreground uppercase tracking-wider font-semibold">Tarih</div>
          <div className="text-xs font-medium text-foreground">{requestedDate}</div>
        </div>
      </div>

      <p className="text-[11px] text-muted-foreground leading-relaxed flex items-start gap-1.5">
        <AlertCircle className="w-3.5 h-3.5 text-amber-500 shrink-0 mt-0.5" />
        <span>
          Bu saat için başka bir kullanıcı işlem yapıyor veya doluluk mevcut. Slot açıldığında rezervasyon hakkı size devredilecektir.
        </span>
      </p>
    </div>
  );
}
