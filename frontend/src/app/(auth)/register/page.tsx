"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Building2, User, ArrowRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { coreApi } from "@/lib/api/client";
import { useAuth } from "@/providers/providers";
import { UserSession } from "@/types";

export default function RegisterPage() {
  const router = useRouter();
  const { login } = useAuth();

  const [registerType, setRegisterType] = useState<"customer" | "organization">("customer");
  const [isLoading, setIsLoading] = useState(false);

  // Customer state
  const [custName, setCustName] = useState("");
  const [custPhone, setCustPhone] = useState("+90555");
  const [custEmail, setCustEmail] = useState("");

  // Organization state
  const [orgName, setOrgName] = useState("");
  const [orgSlug, setOrgSlug] = useState("");
  const [orgPhone, setOrgPhone] = useState("+90555");
  const [orgEmail, setOrgEmail] = useState("");
  const [orgAddress, setOrgAddress] = useState("");
  const [adminName, setAdminName] = useState("");
  const [adminPassword, setAdminPassword] = useState("");

  const handleOrgNameChange = (val: string) => {
    setOrgName(val);
    const slug = val
      .toLowerCase()
      .trim()
      .replace(/ğ/g, "g")
      .replace(/ü/g, "u")
      .replace(/ş/g, "s")
      .replace(/ı/g, "i")
      .replace(/ö/g, "o")
      .replace(/ç/g, "c")
      .replace(/[^a-z0-9]/g, "-")
      .replace(/-+/g, "-");
    setOrgSlug(slug);
  };

  const handleCustomerSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!custName || !custPhone || !custEmail) {
      toast.error("Lütfen tüm alanları doldurunuz.");
      return;
    }

    setIsLoading(true);
    try {
      const customer = await coreApi.registerCustomer({
        fullName: custName,
        phoneNumber: custPhone,
        email: custEmail
      });

      const session: UserSession = {
        id: customer.id,
        fullName: customer.fullName,
        email: customer.email,
        role: "Customer",
        organizationId: "5f8a475a-bcc0-4241-8955-c3d13acee320",
        organizationSlug: "conc-org-6f88d9d7",
        token: "customer-session-token"
      };

      login(session);
      toast.success("Müşteri kaydınız başarıyla oluşturuldu!");
      router.push("/app/home");
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : "Kayıt işlemi başarısız oldu.";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  };

  const handleOrgSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!orgName || !orgSlug || !orgEmail || !adminName || !adminPassword) {
      toast.error("Lütfen zorunlu alanları doldurunuz.");
      return;
    }

    setIsLoading(true);
    try {
      const org = await coreApi.registerOrganization({
        name: orgName,
        slug: orgSlug,
        email: orgEmail,
        phoneNumber: orgPhone,
        address: orgAddress,
        timeZone: "Europe/Istanbul",
        adminEmail: orgEmail,
        adminPassword: adminPassword,
        adminFullName: adminName
      });

      const session: UserSession = {
        id: org.id,
        fullName: adminName,
        email: orgEmail,
        role: "OrganizationAdmin",
        organizationId: org.id,
        organizationSlug: org.slug,
        token: "admin-session-token"
      };

      login(session);
      toast.success("Kurum kaydı başarıyla tamamlandı!");
      router.push("/admin/dashboard");
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : "Kurum kaydı sırasında hata oluştu.";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <Card className="border-border shadow-lg">
      <CardHeader className="space-y-1 text-center">
        <CardTitle className="text-2xl font-bold">Hesap Oluşturun</CardTitle>
        <CardDescription>
          Hızlı randevu almak veya kurumunuzu platforma eklemek için kaydolun
        </CardDescription>

        <div className="pt-2">
          <Tabs value={registerType} onValueChange={(v) => setRegisterType(v as "customer" | "organization")}>
            <TabsList className="grid w-full grid-cols-2">
              <TabsTrigger value="customer" className="gap-2">
                <User className="w-3.5 h-3.5" />
                Müşteri Kaydı
              </TabsTrigger>
              <TabsTrigger value="organization" className="gap-2">
                <Building2 className="w-3.5 h-3.5" />
                Kurum Aç
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
      </CardHeader>

      <CardContent>
        {registerType === "customer" ? (
          <form onSubmit={handleCustomerSubmit} className="space-y-4">
            <Input
              label="Ad Soyad"
              placeholder="Ahmet Yılmaz"
              value={custName}
              onChange={(e) => setCustName(e.target.value)}
              required
            />
            <Input
              label="Telefon Numarası"
              placeholder="+905551234567"
              value={custPhone}
              onChange={(e) => setCustPhone(e.target.value)}
              required
            />
            <Input
              label="E-Posta Adresi"
              type="email"
              placeholder="ahmet@example.com"
              value={custEmail}
              onChange={(e) => setCustEmail(e.target.value)}
              required
            />
            <Button type="submit" className="w-full gap-2" isLoading={isLoading}>
              <span>Kayıt Ol ve Başla</span>
              <ArrowRight className="w-4 h-4" />
            </Button>
          </form>
        ) : (
          <form onSubmit={handleOrgSubmit} className="space-y-3.5">
            <Input
              label="Kurum Adı"
              placeholder="Örn: Kadıköy Polikliniği"
              value={orgName}
              onChange={(e) => handleOrgNameChange(e.target.value)}
              required
            />
            <Input
              label="URL Slug (Benzersiz Tanımlayıcı)"
              placeholder="kadikoy-poliklinigi"
              value={orgSlug}
              onChange={(e) => setOrgSlug(e.target.value)}
              required
            />
            <div className="grid grid-cols-2 gap-3">
              <Input
                label="Kurum E-Posta"
                type="email"
                placeholder="info@kurum.com"
                value={orgEmail}
                onChange={(e) => setOrgEmail(e.target.value)}
                required
              />
              <Input
                label="Kurum Telefon"
                placeholder="+902161234567"
                value={orgPhone}
                onChange={(e) => setOrgPhone(e.target.value)}
              />
            </div>
            <Input
              label="Adres"
              placeholder="Bağdat Cad. No: 42 Kadıköy, İstanbul"
              value={orgAddress}
              onChange={(e) => setOrgAddress(e.target.value)}
            />
            <div className="border-t border-border pt-3 space-y-3">
              <p className="text-xs font-semibold text-primary">Yönetici Bilgileri</p>
              <Input
                label="Yönetici Ad Soyad"
                placeholder="Dr. Mehmet Öz"
                value={adminName}
                onChange={(e) => setAdminName(e.target.value)}
                required
              />
              <Input
                label="Yönetici Şifre"
                type="password"
                placeholder="••••••••"
                value={adminPassword}
                onChange={(e) => setAdminPassword(e.target.value)}
                required
              />
            </div>
            <Button type="submit" className="w-full gap-2 mt-2" isLoading={isLoading}>
              <span>Kurumu Kaydet ve Başlat</span>
              <ArrowRight className="w-4 h-4" />
            </Button>
          </form>
        )}
      </CardContent>

      <CardFooter className="flex flex-col gap-2 text-center text-xs text-muted-foreground">
        <div>
          Zaten hesabınız var mı?{" "}
          <Link href="/login" className="text-primary font-medium hover:underline">
            Giriş Yapın
          </Link>
        </div>
      </CardFooter>
    </Card>
  );
}
