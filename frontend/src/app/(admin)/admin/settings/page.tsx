"use client";

import React, { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { organizationApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  Building2,
  Clock,
  Save,
  Users,
  Loader2,
  AlertCircle,
  CheckCircle2,
} from "lucide-react";
import { toast } from "sonner";

export default function AdminSettingsPage() {
  const { session } = useAuth();
  const queryClient = useQueryClient();
  const orgId = session?.organizationId || "5f8a475a-bcc0-4241-8955-c3d13acee320";

  const { data: orgData, isLoading } = useQuery({
    queryKey: ["org-settings", orgId],
    queryFn: () => organizationApi.getById(orgId),
  });

  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [address, setAddress] = useState("");
  const [timezone, setTimezone] = useState("Europe/Istanbul");
  const [slotDuration, setSlotDuration] = useState("30");

  // Sync state when orgData is fetched from server
  useEffect(() => {
    if (orgData) {
      if (orgData.name) setName(orgData.name);
      if (orgData.email) setEmail(orgData.email);
      if (orgData.phoneNumber) setPhone(orgData.phoneNumber);
      if (orgData.address) setAddress(orgData.address);
      if (orgData.timeZone || orgData.timeZoneId) {
        setTimezone(orgData.timeZone || orgData.timeZoneId || "Europe/Istanbul");
      }
      if (orgData.weeklyAvailabilities && orgData.weeklyAvailabilities.length > 0) {
        const firstDuration = orgData.weeklyAvailabilities[0]?.slotDurationMinutes;
        if (firstDuration) {
          setSlotDuration(String(firstDuration));
        }
      }
    }
  }, [orgData]);

  // Update Organization Mutation
  const updateMutation = useMutation({
    mutationFn: async () => {
      // 1. Update general organization details via PUT /api/organizations/{orgId}
      const updatedOrg = await organizationApi.update(orgId, {
        name: name.trim(),
        email: email.trim(),
        phoneNumber: phone.trim(),
        address: address.trim(),
        timeZone: timezone,
      });

      // 2. If weekly availabilities exist and slotDuration changed, update weekly schedule slot durations
      const newDuration = parseInt(slotDuration, 10);
      if (
        orgData?.weeklyAvailabilities &&
        orgData.weeklyAvailabilities.length > 0 &&
        newDuration &&
        newDuration !== orgData.weeklyAvailabilities[0]?.slotDurationMinutes
      ) {
        await organizationApi.setWeeklyAvailability({
          organizationId: orgId,
          days: orgData.weeklyAvailabilities.map((w) => ({
            dayOfWeek: w.dayOfWeek,
            startTime: w.startTime,
            endTime: w.endTime,
            slotDurationMinutes: newDuration,
            isActive: w.isActive,
          })),
        });
      }

      return updatedOrg;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["org-settings", orgId] });
      queryClient.invalidateQueries({ queryKey: ["org-details", orgId] });
      queryClient.invalidateQueries({ queryKey: ["organization", orgId] });
      queryClient.invalidateQueries({ queryKey: ["organizations"] });
      toast.success("Kurum ayarları başarıyla güncellendi.");
    },
    onError: (err: unknown) => {
      const errorMsg = err instanceof Error ? err.message : "Kurum ayarları güncellenirken bir hata oluştu.";
      toast.error(errorMsg);
    },
  });

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      toast.error("Kurum adı boş bırakılamaz.");
      return;
    }
    if (!email.trim()) {
      toast.error("E-posta adresi boş bırakılamaz.");
      return;
    }
    updateMutation.mutate();
  };

  return (
    <div className="space-y-8 max-w-4xl">
      {/* Title & Save Action */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight">
            Kurum Ayarları
          </h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Kurum profili, iletişim detayları, randevu kuralları ve ekip yönetimi
          </p>
        </div>

        <Button
          type="submit"
          form="settings-form"
          disabled={updateMutation.isPending || isLoading}
          size="sm"
          className="gap-2 text-xs font-semibold shadow-xs"
        >
          {updateMutation.isPending ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Save className="w-4 h-4" />
          )}
          {updateMutation.isPending ? "Kaydediliyor..." : "Değişiklikleri Kaydet"}
        </Button>
      </div>

      <form id="settings-form" onSubmit={handleSave} className="space-y-6">
        {/* General Info Card */}
        <Card className="shadow-xs">
          <CardHeader>
            <CardTitle className="text-sm font-bold flex items-center gap-2">
              <Building2 className="w-4 h-4 text-primary" />
              Genel Kurum Bilgileri
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 pt-0">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-foreground">
                  Kurum Adı <span className="text-destructive">*</span>
                </label>
                <Input
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  disabled={isLoading || updateMutation.isPending}
                  placeholder="Örn: Concurrency Clinic"
                  className="text-xs h-9"
                  required
                />
              </div>

              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-foreground">Slug (URL Tanımı)</label>
                <Input
                  value={orgData?.slug || "conc-org-6f88d9d7"}
                  disabled
                  className="text-xs h-9 bg-muted/40 font-mono"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-foreground">
                  E-Posta Adresi <span className="text-destructive">*</span>
                </label>
                <Input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  disabled={isLoading || updateMutation.isPending}
                  placeholder="info@kurum.com"
                  className="text-xs h-9"
                  required
                />
              </div>

              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-foreground">Telefon</label>
                <Input
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  disabled={isLoading || updateMutation.isPending}
                  placeholder="+90 212 555 0199"
                  className="text-xs h-9"
                />
              </div>
            </div>

            <div className="space-y-1.5">
              <label className="text-xs font-semibold text-foreground">Adres</label>
              <Input
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                disabled={isLoading || updateMutation.isPending}
                placeholder="Adres bilgisi giriniz"
                className="text-xs h-9"
              />
            </div>
          </CardContent>
        </Card>

        {/* Appointment & Timezone Rules */}
        <Card className="shadow-xs">
          <CardHeader>
            <CardTitle className="text-sm font-bold flex items-center gap-2">
              <Clock className="w-4 h-4 text-primary" />
              Zaman Dilimi & Randevu Parametreleri
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 pt-0">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-foreground">Saat Dilimi (Timezone)</label>
                <select
                  value={timezone}
                  onChange={(e) => setTimezone(e.target.value)}
                  disabled={isLoading || updateMutation.isPending}
                  className="w-full h-9 rounded-lg border border-border bg-surface px-3 text-xs text-foreground focus:outline-none focus:ring-1 focus:ring-primary"
                >
                  <option value="Europe/Istanbul">Europe/Istanbul (UTC+03:00)</option>
                  <option value="Europe/London">Europe/London (GMT/BST)</option>
                  <option value="Europe/Berlin">Europe/Berlin (CET)</option>
                  <option value="America/New_York">America/New_York (EST)</option>
                </select>
              </div>

              <div className="space-y-1.5">
                <label className="text-xs font-semibold text-foreground">Varsayılan Slot Süresi</label>
                <select
                  value={slotDuration}
                  onChange={(e) => setSlotDuration(e.target.value)}
                  disabled={isLoading || updateMutation.isPending}
                  className="w-full h-9 rounded-lg border border-border bg-surface px-3 text-xs text-foreground focus:outline-none focus:ring-1 focus:ring-primary"
                >
                  <option value="15">15 Dakika</option>
                  <option value="30">30 Dakika</option>
                  <option value="45">45 Dakika</option>
                  <option value="60">60 Dakika</option>
                </select>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Team Members Mock Table */}
        <Card className="shadow-xs">
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle className="text-sm font-bold flex items-center gap-2">
              <Users className="w-4 h-4 text-primary" />
              Ekip Üyeleri & Yetkiler
            </CardTitle>
            <Button type="button" variant="outline" size="sm" className="text-xs h-7">
              + Yeni Üye Davet Et
            </Button>
          </CardHeader>
          <CardContent className="pt-0">
            <div className="divide-y divide-border/60 text-xs">
              <div className="py-2.5 flex items-center justify-between">
                <div>
                  <div className="font-semibold text-foreground">Dr. Ahmet Yılmaz</div>
                  <div className="text-[10px] text-muted-foreground">ahmet@concurrencyclinic.com</div>
                </div>
                <Badge variant="outline" className="text-[10px] bg-primary/5 text-primary border-primary/20">
                  Kurum Yöneticisi (Admin)
                </Badge>
              </div>

              <div className="py-2.5 flex items-center justify-between">
                <div>
                  <div className="font-semibold text-foreground">Selin Koç</div>
                  <div className="text-[10px] text-muted-foreground">selin@concurrencyclinic.com</div>
                </div>
                <Badge variant="outline" className="text-[10px]">
                  Resepsiyonist / Personel
                </Badge>
              </div>
            </div>
          </CardContent>
        </Card>

        {/* Bottom Save Action */}
        <div className="flex justify-end pt-2">
          <Button
            type="submit"
            disabled={updateMutation.isPending || isLoading}
            size="sm"
            className="gap-2 text-xs font-semibold shadow-xs"
          >
            {updateMutation.isPending ? (
              <Loader2 className="w-4 h-4 animate-spin" />
            ) : (
              <Save className="w-4 h-4" />
            )}
            {updateMutation.isPending ? "Kaydediliyor..." : "Değişiklikleri Kaydet"}
          </Button>
        </div>
      </form>
    </div>
  );
}
