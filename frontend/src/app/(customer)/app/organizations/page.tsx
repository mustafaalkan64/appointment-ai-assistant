"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  Building2,
  MapPin,
  Phone,
  Mail,
  Clock,
  Sparkles,
  Search,
  ExternalLink
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { coreApi } from "@/lib/api/client";
import { Organization } from "@/types";

export default function OrganizationsDiscoveryPage() {
  const [searchTerm, setSearchTerm] = useState("");

  const { data: orgs, isLoading } = useQuery({
    queryKey: ["organizations-list"],
    queryFn: () => coreApi.getOrganizations()
  });

  const filteredOrgs = (orgs || []).filter(
    (o) =>
      o.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      o.slug.toLowerCase().includes(searchTerm.toLowerCase()) ||
      o.address.toLowerCase().includes(searchTerm.toLowerCase())
  );

  return (
    <div className="max-w-5xl mx-auto w-full p-4 sm:p-6 md:p-8 space-y-6">
      <div className="space-y-2">
        <h1 className="text-2xl font-bold tracking-tight">Kurumları Keşfedin</h1>
        <p className="text-sm text-muted-foreground">
          Platforma kayıtlı sağlık merkezleri, poliklinikler ve hizmet noktaları arasından seçim yapın.
        </p>
      </div>

      {/* Search Input Bar */}
      <div className="relative max-w-md">
        <Search className="w-4 h-4 text-muted-foreground absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
        <input
          type="text"
          placeholder="Kurum adı veya ilçe ile arayın..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          className="w-full rounded-lg border border-input bg-surface pl-9 pr-4 py-2 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        />
      </div>

      {isLoading ? (
        <div className="py-12 text-center text-sm text-muted-foreground">
          Kurumlar yükleniyor...
        </div>
      ) : filteredOrgs.length === 0 ? (
        <Card className="p-8 text-center border-dashed">
          <Building2 className="w-10 h-10 text-muted-foreground mx-auto mb-2 opacity-50" />
          <p className="text-sm font-medium">Aramanıza uygun kurum bulunamadı</p>
          <p className="text-xs text-muted-foreground mt-1">Farklı bir arama terimi deneyebilirsiniz.</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {filteredOrgs.map((org) => (
            <Card key={org.id} className="border-border hover:border-primary/40 transition-all flex flex-col justify-between">
              <CardHeader className="pb-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="flex items-center gap-3">
                    <div className="w-11 h-11 rounded-xl bg-primary/10 text-primary flex items-center justify-center font-bold text-lg shrink-0">
                      {org.name[0]}
                    </div>
                    <div>
                      <CardTitle className="text-base text-foreground">{org.name}</CardTitle>
                      <p className="text-xs text-muted-foreground font-mono">@{org.slug}</p>
                    </div>
                  </div>
                  <Badge variant={org.isActive ? "success" : "secondary"}>
                    {org.isActive ? "Açık" : "Kapalı"}
                  </Badge>
                </div>
              </CardHeader>

              <CardContent className="space-y-2 text-xs text-muted-foreground">
                {org.address && (
                  <div className="flex items-center gap-2">
                    <MapPin className="w-3.5 h-3.5 text-primary shrink-0" />
                    <span className="truncate">{org.address}</span>
                  </div>
                )}
                {org.phoneNumber && (
                  <div className="flex items-center gap-2">
                    <Phone className="w-3.5 h-3.5 text-primary shrink-0" />
                    <span>{org.phoneNumber}</span>
                  </div>
                )}
                {org.timeZone && (
                  <div className="flex items-center gap-2">
                    <Clock className="w-3.5 h-3.5 text-primary shrink-0" />
                    <span>Saat Dilimi: {org.timeZone}</span>
                  </div>
                )}
              </CardContent>

              <CardFooter className="pt-2 border-t border-border flex items-center justify-between gap-2">
                <Link href={`/organization/${org.slug}`} className="text-xs text-muted-foreground hover:text-foreground flex items-center gap-1">
                  <span>Detaylar</span>
                  <ExternalLink className="w-3 h-3" />
                </Link>

                <Link href={`/app/chat/${org.slug}`}>
                  <Button size="sm" className="gap-2 text-xs font-semibold shadow-xs">
                    <Sparkles className="w-3.5 h-3.5" />
                    <span>AI ile Randevu Al</span>
                  </Button>
                </Link>
              </CardFooter>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
