"use client";

import React, { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Building2, User, ArrowRight, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useAuth } from "@/providers/providers";
import { UserSession } from "@/types";

export default function LoginPage() {
  const router = useRouter();
  const { login } = useAuth();

  const [roleTab, setRoleTab] = useState<"customer" | "admin">("customer");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [isLoading, setIsLoading] = useState(false);

  // Demo seed logins
  const handleQuickLogin = (type: "customer" | "admin") => {
    setIsLoading(true);
    setTimeout(() => {
      let demoSession: UserSession;
      if (type === "admin") {
        demoSession = {
          id: "ad4923e2-1200-4ea1-8a90-3b1029384756",
          fullName: "Dr. Selim Yılmaz",
          email: "admin@concurrencyclinic.com",
          role: "OrganizationAdmin",
          organizationId: "5f8a475a-bcc0-4241-8955-c3d13acee320",
          organizationSlug: "conc-org-6f88d9d7",
          token: "jwt-demo-admin-token"
        };
        login(demoSession);
        toast.success("Yönetici girişi başarılı!");
        router.push("/admin/dashboard");
      } else {
        demoSession = {
          id: "c63fa787-d893-4b6d-8372-f1abe6fe97aa",
          fullName: "Ali Yılmaz",
          email: "ali@example.com",
          role: "Customer",
          organizationId: "5f8a475a-bcc0-4241-8955-c3d13acee320",
          organizationSlug: "conc-org-6f88d9d7",
          token: "jwt-demo-customer-token"
        };
        login(demoSession);
        toast.success("Müşteri girişi başarılı!");
        router.push("/app/home");
      }
      setIsLoading(false);
    }, 500);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!email) {
      toast.error("Lütfen e-posta adresinizi giriniz.");
      return;
    }

    setIsLoading(true);
    setTimeout(() => {
      if (roleTab === "admin") {
        const session: UserSession = {
          id: "ad4923e2-1200-4ea1-8a90-3b1029384756",
          fullName: email.split("@")[0],
          email,
          role: "OrganizationAdmin",
          organizationId: "5f8a475a-bcc0-4241-8955-c3d13acee320",
          organizationSlug: "conc-org-6f88d9d7",
          token: "jwt-token-active"
        };
        login(session);
        toast.success("Giriş yapıldı!");
        router.push("/admin/dashboard");
      } else {
        const session: UserSession = {
          id: "c63fa787-d893-4b6d-8372-f1abe6fe97aa",
          fullName: email.split("@")[0],
          email,
          role: "Customer",
          organizationId: "5f8a475a-bcc0-4241-8955-c3d13acee320",
          organizationSlug: "conc-org-6f88d9d7",
          token: "jwt-token-active"
        };
        login(session);
        toast.success("Giriş yapıldı!");
        router.push("/app/home");
      }
      setIsLoading(false);
    }, 600);
  };

  return (
    <Card className="border-border shadow-lg">
      <CardHeader className="space-y-1 text-center">
        <CardTitle className="text-2xl font-bold">Tekrar Hoş Geldiniz</CardTitle>
        <CardDescription>
          Randevularınızı yönetmek veya kurum panelinize erişmek için giriş yapın
        </CardDescription>
        
        {/* Role Switcher Tab */}
        <div className="pt-2">
          <Tabs value={roleTab} onValueChange={(v) => setRoleTab(v as "customer" | "admin")}>
            <TabsList className="grid w-full grid-cols-2">
              <TabsTrigger value="customer" className="gap-2">
                <User className="w-3.5 h-3.5" />
                Müşteri
              </TabsTrigger>
              <TabsTrigger value="admin" className="gap-2">
                <Building2 className="w-3.5 h-3.5" />
                Kurum Admin
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
      </CardHeader>

      <CardContent>
        {/* Demo Fast Access Pill */}
        <div className="mb-5 p-3 rounded-lg bg-accent/60 border border-primary/20 text-xs flex items-center justify-between">
          <div className="flex items-center gap-2">
            <ShieldCheck className="w-4 h-4 text-primary shrink-0" />
            <span className="text-foreground">
              {roleTab === "admin" ? "Canlı Kurum Yöneticisi Demosu" : "Kayıtlı Müşteri Demosu"}
            </span>
          </div>
          <Button
            type="button"
            size="sm"
            variant="default"
            className="h-7 text-xs px-2.5"
            onClick={() => handleQuickLogin(roleTab)}
          >
            Hızlı Giriş
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <Input
            label={roleTab === "admin" ? "Yönetici E-Posta" : "E-Posta veya Telefon"}
            placeholder={roleTab === "admin" ? "admin@kurum.com" : "ali@example.com"}
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />

          <Input
            label="Şifre"
            type="password"
            placeholder="••••••••"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />

          <Button type="submit" className="w-full gap-2" isLoading={isLoading}>
            <span>Giriş Yap</span>
            <ArrowRight className="w-4 h-4" />
          </Button>
        </form>
      </CardContent>

      <CardFooter className="flex flex-col gap-2 text-center text-xs text-muted-foreground">
        <div>
          Hesabınız yok mu?{" "}
          <Link href="/register" className="text-primary font-medium hover:underline">
            Hemen Kayıt Olun
          </Link>
        </div>
      </CardFooter>
    </Card>
  );
}
