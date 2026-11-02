"use client";

import React, { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "@/providers/providers";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import {
  Users,
  Clock,
  Search,
  RefreshCw,
  CheckCircle2,
  AlertCircle,
  XCircle,
  Timer,
} from "lucide-react";
import { toast } from "sonner";

interface QueueEntryMock {
  id: string;
  position: number;
  customerName: string;
  customerPhone: string;
  requestedDate: string;
  requestedTime: string;
  status: "Waiting" | "Processing" | "Reserved" | "Completed" | "Cancelled";
  entryTime: string;
}

export default function AdminWaitingQueuePage() {
  const { session } = useAuth();
  const [searchQuery, setSearchQuery] = useState("");

  // Demo queue list backed by realistic state
  const [queueEntries, setQueueEntries] = useState<QueueEntryMock[]>([
    {
      id: "q-101",
      position: 1,
      customerName: "Ali Yılmaz",
      customerPhone: "+90 555 999 88 77",
      requestedDate: "2 Kasım 2026",
      requestedTime: "10:30",
      status: "Waiting",
      entryTime: "10 dakika önce",
    },
    {
      id: "q-102",
      position: 2,
      customerName: "Zeynep Demir",
      customerPhone: "+90 532 111 22 33",
      requestedDate: "2 Kasım 2026",
      requestedTime: "14:00",
      status: "Reserved",
      entryTime: "25 dakika önce",
    },
    {
      id: "q-103",
      position: 3,
      customerName: "Mehmet Kaya",
      customerPhone: "+90 544 333 44 55",
      requestedDate: "3 Kasım 2026",
      requestedTime: "11:00",
      status: "Waiting",
      entryTime: "40 dakika önce",
    },
  ]);

  const handleCancelEntry = (id: string) => {
    setQueueEntries((prev) =>
      prev.map((e) => (e.id === id ? { ...e, status: "Cancelled" } : e))
    );
    toast.success("Bekleme sırası kaydı iptal edildi.");
  };

  const filtered = queueEntries.filter(
    (e) =>
      e.customerName.toLowerCase().includes(searchQuery.toLowerCase()) ||
      e.customerPhone.includes(searchQuery)
  );

  return (
    <div className="space-y-6">
      {/* Title */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Bekleme Sırası (Waiting Queue)
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Dolu saatler için sıraya giren müşterileri ve otomatik rezervasyon durumlarını izleyin
          </p>
        </div>

        <Button
          onClick={() => toast.info("Bekleme sırası güncellendi.")}
          variant="outline"
          size="sm"
          className="text-xs gap-1.5 h-9"
        >
          <RefreshCw className="w-3.5 h-3.5" />
          Yenile
        </Button>
      </div>

      {/* Summary KPI Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <Card className="shadow-xs">
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <div className="text-[11px] text-muted-foreground font-medium">Sırada Bekleyenler</div>
              <div className="text-2xl font-bold text-amber-600">
                {queueEntries.filter((e) => e.status === "Waiting").length}
              </div>
            </div>
            <div className="w-9 h-9 rounded-xl bg-amber-500/10 text-amber-600 flex items-center justify-center">
              <Users className="w-5 h-5" />
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-xs">
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <div className="text-[11px] text-muted-foreground font-medium">Rezerve Edilen / Onay Bekleyen</div>
              <div className="text-2xl font-bold text-indigo-600">
                {queueEntries.filter((e) => e.status === "Reserved").length}
              </div>
            </div>
            <div className="w-9 h-9 rounded-xl bg-indigo-500/10 text-indigo-600 flex items-center justify-center">
              <Timer className="w-5 h-5" />
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-xs">
          <CardContent className="p-4 flex items-center justify-between">
            <div>
              <div className="text-[11px] text-muted-foreground font-medium">Ortalama Bekleme Süresi</div>
              <div className="text-2xl font-bold text-foreground">18 dk</div>
            </div>
            <div className="w-9 h-9 rounded-xl bg-emerald-500/10 text-emerald-600 flex items-center justify-center">
              <Clock className="w-5 h-5" />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Search Bar */}
      <div className="relative max-w-md">
        <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
        <Input
          placeholder="Müşteri adı veya telefon ile ara..."
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          className="pl-9 text-xs h-9"
        />
      </div>

      {/* Queue Data Table */}
      <Card className="shadow-xs overflow-hidden">
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-xs text-left">
              <thead className="text-[11px] uppercase tracking-wider text-muted-foreground border-b border-border bg-muted/20">
                <tr>
                  <th className="py-3 px-4">Sıra #</th>
                  <th className="py-3 px-4">Müşteri</th>
                  <th className="py-3 px-4">Talep Edilen Saat</th>
                  <th className="py-3 px-4">Sıraya Giriş</th>
                  <th className="py-3 px-4">Durum</th>
                  <th className="py-3 px-4 text-right">İşlem</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border/60">
                {filtered.map((entry) => (
                  <tr key={entry.id} className="hover:bg-muted/30 transition-colors">
                    <td className="py-3.5 px-4 font-bold text-amber-600 text-sm">
                      #{entry.position}
                    </td>
                    <td className="py-3.5 px-4">
                      <div className="font-semibold text-foreground">{entry.customerName}</div>
                      <div className="text-[10px] text-muted-foreground">{entry.customerPhone}</div>
                    </td>
                    <td className="py-3.5 px-4 font-medium text-foreground">
                      {entry.requestedDate} • {entry.requestedTime}
                    </td>
                    <td className="py-3.5 px-4 text-muted-foreground text-[11px]">
                      {entry.entryTime}
                    </td>
                    <td className="py-3.5 px-4">
                      <Badge
                        variant="outline"
                        className={`text-[10px] py-0.5 px-2 ${
                          entry.status === "Reserved"
                            ? "bg-indigo-500/10 text-indigo-600 border-indigo-500/20"
                            : entry.status === "Cancelled"
                            ? "bg-rose-500/10 text-rose-600 border-rose-500/20"
                            : "bg-amber-500/10 text-amber-600 border-amber-500/20"
                        }`}
                      >
                        {entry.status}
                      </Badge>
                    </td>
                    <td className="py-3.5 px-4 text-right">
                      {entry.status !== "Cancelled" && (
                        <Button
                          onClick={() => handleCancelEntry(entry.id)}
                          variant="ghost"
                          size="sm"
                          className="h-7 text-xs px-2 text-rose-600 hover:text-rose-700 hover:bg-rose-50 dark:hover:bg-rose-950/20"
                        >
                          Sıradan Çıkar
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
