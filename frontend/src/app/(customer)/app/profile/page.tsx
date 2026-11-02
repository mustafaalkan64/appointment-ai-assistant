"use client";

import React, { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { User, Phone, Mail, LogOut, Shield, Bell, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useAuth } from "@/providers/providers";

export default function CustomerProfilePage() {
  const router = useRouter();
  const { session, logout } = useAuth();

  const [fullName, setFullName] = useState(session?.fullName || "Ali Yılmaz");
  const [email, setEmail] = useState(session?.email || "ali@example.com");
  const [phone, setPhone] = useState("+905559998877");
  const [notificationsEnabled, setNotificationsEnabled] = useState(true);

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    toast.success("Profil bilgileriniz güncellendi!");
  };

  const handleLogout = () => {
    logout();
    toast.info("Oturumunuz kapatıldı.");
    router.push("/login");
  };

  return (
    <div className="max-w-3xl mx-auto w-full p-4 sm:p-6 md:p-8 space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Profil & Hesap Ayarları</h1>
        <p className="text-sm text-muted-foreground">
          Kişisel bilgilerinizi, iletişim kanallarınızı ve bildirim tercihlerinizi yönetin.
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* User Summary Card */}
        <Card className="md:col-span-1 border-border text-center p-6 space-y-4">
          <div className="w-20 h-20 rounded-full bg-primary/10 text-primary font-bold text-2xl flex items-center justify-center mx-auto border-2 border-primary/20">
            {fullName ? fullName[0].toUpperCase() : "A"}
          </div>
          <div>
            <h2 className="font-semibold text-base text-foreground">{fullName}</h2>
            <p className="text-xs text-muted-foreground">{email}</p>
          </div>

          <div className="pt-2 flex justify-center">
            <Badge variant="success" className="gap-1">
              <CheckCircle2 className="w-3 h-3" />
              Doğrulanmış Müşteri
            </Badge>
          </div>

          <div className="pt-4 border-t border-border">
            <Button variant="danger" className="w-full gap-2 text-xs" onClick={handleLogout}>
              <LogOut className="w-4 h-4" />
              <span>Oturumu Kapat</span>
            </Button>
          </div>
        </Card>

        {/* Profile Edit Form */}
        <Card className="md:col-span-2 border-border">
          <CardHeader>
            <CardTitle className="text-lg">Kişisel Bilgiler</CardTitle>
            <CardDescription className="text-xs">
              Randevu onay SMS'leri ve e-posta bildirimleri için kullanılan iletişim detayları
            </CardDescription>
          </CardHeader>

          <CardContent>
            <form onSubmit={handleSave} className="space-y-4">
              <Input
                label="Ad Soyad"
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                required
              />

              <Input
                label="E-Posta Adresi"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />

              <Input
                label="Telefon Numarası"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                required
              />

              <div className="pt-2 border-t border-border space-y-3">
                <p className="text-xs font-semibold text-foreground flex items-center gap-2">
                  <Bell className="w-3.5 h-3.5 text-primary" />
                  Bildirim Tercihleri
                </p>
                <label className="flex items-center gap-3 text-xs text-muted-foreground cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={notificationsEnabled}
                    onChange={(e) => setNotificationsEnabled(e.target.checked)}
                    className="rounded border-border text-primary focus:ring-primary w-4 h-4"
                  />
                  <span>Randevu hatırlatma ve bekleme sırası bildirimlerini SMS ile al</span>
                </label>
              </div>

              <div className="pt-2">
                <Button type="submit" className="gap-2">
                  Değişiklikleri Kaydet
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
