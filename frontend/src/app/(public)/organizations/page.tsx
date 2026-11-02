"use client";

import React, { useState, useMemo } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { organizationApi } from "@/lib/api/client";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Search,
  Building2,
  Clock,
  Sparkles,
  MapPin,
  ArrowRight,
  Filter,
} from "lucide-react";

export default function PublicOrganizationsPage() {
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedTag, setSelectedTag] = useState<string>("Tümü");

  const { data: orgs, isLoading, error } = useQuery({
    queryKey: ["public-organizations-all"],
    queryFn: () => organizationApi.getAll(),
  });

  const tags = ["Tümü", "Klinik", "Diş", "Danışmanlık", "Güzellik"];

  const filteredOrgs = useMemo(() => {
    if (!orgs) return [];
    return orgs.filter((org) => {
      const matchesSearch =
        org.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
        (org.description && org.description.toLowerCase().includes(searchQuery.toLowerCase()));
      return matchesSearch;
    });
  }, [orgs, searchQuery]);

  return (
    <div className="max-w-6xl mx-auto px-4 sm:px-8 py-10 space-y-8">
      {/* Header */}
      <div className="space-y-3">
        <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full border border-primary/20 bg-primary/5 text-primary text-xs font-semibold">
          <Sparkles className="w-3.5 h-3.5" />
          <span>Keşfet & Randevu Al</span>
        </div>
        <h1 className="text-3xl sm:text-4xl font-extrabold tracking-tight">
          Hizmet Veren Kurumlar
        </h1>
        <p className="text-muted-foreground text-sm max-w-2xl">
          Randevu almak istediğiniz kurumu seçin, yapay zeka asistanı ile doğal dille sohbet ederek anında randevunuzu oluşturun.
        </p>
      </div>

      {/* Filters & Search */}
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-4">
        <div className="relative flex-1 max-w-md">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Kurum adı veya açıklama ile ara..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="pl-9 text-xs"
          />
        </div>

        {/* Tag pills */}
        <div className="flex items-center gap-1.5 overflow-x-auto pb-1 sm:pb-0">
          <Filter className="w-3.5 h-3.5 text-muted-foreground mr-1 hidden sm:inline" />
          {tags.map((tag) => (
            <button
              key={tag}
              onClick={() => setSelectedTag(tag)}
              className={`text-xs px-3 py-1.5 rounded-full transition-all whitespace-nowrap font-medium ${
                selectedTag === tag
                  ? "bg-primary text-primary-foreground shadow-xs"
                  : "bg-surface border border-border text-muted-foreground hover:text-foreground"
              }`}
            >
              {tag}
            </button>
          ))}
        </div>
      </div>

      {/* Grid of Organizations */}
      {isLoading ? (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {[1, 2, 3, 4, 5, 6].map((i) => (
            <div key={i} className="h-48 rounded-2xl bg-muted/40 animate-pulse border border-border" />
          ))}
        </div>
      ) : error ? (
        <div className="p-12 text-center rounded-2xl border border-rose-500/20 bg-rose-500/5 text-rose-600 text-sm">
          Kurum listesi yüklenirken bir hata oluştu. Lütfen backend servisinin çalıştığından emin olun.
        </div>
      ) : filteredOrgs.length === 0 ? (
        <div className="p-12 text-center rounded-2xl border border-dashed border-border space-y-3">
          <Building2 className="w-10 h-10 text-muted-foreground/50 mx-auto" />
          <h3 className="font-bold text-sm">Kurum bulunamadı</h3>
          <p className="text-xs text-muted-foreground">
            Arama kriterlerinize uygun kayıtlı kurum bulunmamaktadır.
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {filteredOrgs.map((org) => (
            <Card
              key={org.id}
              className="hover:border-primary/50 transition-all flex flex-col justify-between group shadow-xs hover:shadow-md"
            >
              <CardHeader className="pb-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="w-10 h-10 rounded-xl bg-primary/10 text-primary flex items-center justify-center font-bold text-sm group-hover:scale-105 transition-transform">
                    <Building2 className="w-5 h-5" />
                  </div>
                  <Badge variant="outline" className="text-[10px] bg-emerald-500/10 text-emerald-600 border-emerald-500/20">
                    Randevu Açık
                  </Badge>
                </div>
                <CardTitle className="text-base font-bold mt-3 group-hover:text-primary transition-colors">
                  {org.name}
                </CardTitle>
                <p className="text-xs text-muted-foreground line-clamp-2 mt-1">
                  {org.description || "Sağlık ve danışmanlık alanında modern AI destekli randevu hizmeti."}
                </p>
              </CardHeader>

              <CardContent className="space-y-4 pt-0">
                <div className="space-y-1.5 text-[11px] text-muted-foreground border-t border-border/50 pt-3">
                  <div className="flex items-center gap-1.5">
                    <Clock className="w-3.5 h-3.5 text-primary" />
                    <span>Saat Dilimi: {org.timeZoneId}</span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <MapPin className="w-3.5 h-3.5 text-muted-foreground" />
                    <span>Merkez Şube</span>
                  </div>
                </div>

                <div className="pt-2 flex items-center gap-2">
                  <Link href={`/app/chat/${org.slug}`} className="flex-1">
                    <Button size="sm" className="w-full text-xs gap-1.5 font-semibold">
                      <Sparkles className="w-3.5 h-3.5" />
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
      )}
    </div>
  );
}
