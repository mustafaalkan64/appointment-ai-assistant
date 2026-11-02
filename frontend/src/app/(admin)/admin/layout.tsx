"use client";

import React, { useState, Suspense } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import {
  LayoutDashboard,
  CalendarCheck,
  Calendar as CalendarIcon,
  Clock,
  Users,
  ListOrdered,
  Settings,
  Sparkles,
  LogOut,
  Menu,
  X,
  ChevronRight,
  Bell,
  Building2
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { useAuth } from "@/providers/providers";

const NAV_ITEMS = [
  { href: "/admin/dashboard", label: "Genel Bakış", icon: LayoutDashboard },
  { href: "/admin/appointments", label: "Randevular", icon: CalendarCheck },
  { href: "/admin/calendar", label: "Takvim & Slotlar", icon: CalendarIcon },
  { href: "/admin/availability", label: "Çalışma Saatleri", icon: Clock },
  { href: "/admin/customers", label: "Müşteriler", icon: Users },
  { href: "/admin/waiting-queue", label: "Bekleme Sırası", icon: ListOrdered },
  { href: "/admin/settings", label: "Kurum Ayarları", icon: Settings },
];

function AdminLayoutInner({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const { session, logout } = useAuth();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  const handleLogout = () => {
    logout();
    router.push("/login");
  };

  return (
    <div className="min-h-screen flex bg-background text-foreground">
      {/* Desktop Sidebar */}
      <aside className="hidden lg:flex w-64 flex-col border-r border-border bg-surface shrink-0 z-30">
        {/* Brand & Organization Identity */}
        <div className="h-16 px-5 border-b border-border flex items-center justify-between">
          <Link href="/admin/dashboard" className="flex items-center gap-2.5 font-bold tracking-tight">
            <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center text-primary-foreground shadow-xs">
              <Sparkles className="w-4 h-4 fill-current" />
            </div>
            <span className="text-sm font-semibold">
              Appoint<span className="text-primary font-bold">AI</span> Admin
            </span>
          </Link>
        </div>

        {/* Active Organization Switcher Pill */}
        <div className="p-3 m-3 rounded-lg bg-accent/40 border border-primary/20 flex items-center gap-2.5">
          <div className="w-7 h-7 rounded-md bg-primary/10 text-primary flex items-center justify-center shrink-0">
            <Building2 className="w-4 h-4" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-xs font-semibold truncate text-foreground">
              {session?.fullName ? `${session.fullName} Polikliniği` : "Concurrency Clinic"}
            </p>
            <p className="text-[10px] text-muted-foreground truncate">
              ID: {session?.organizationId ? session.organizationId.slice(0, 8) + "..." : "5f8a475a..."}
            </p>
          </div>
        </div>

        {/* Sidebar Navigation Links */}
        <nav className="flex-1 px-3 space-y-1 overflow-y-auto py-2">
          {NAV_ITEMS.map((item) => {
            const Icon = item.icon;
            const isActive = pathname === item.href || (item.href !== "/admin/dashboard" && pathname.startsWith(item.href));

            return (
              <Link
                key={item.href}
                href={item.href}
                className={cn(
                  "flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors",
                  isActive
                    ? "bg-primary text-primary-foreground shadow-xs"
                    : "text-muted-foreground hover:text-foreground hover:bg-surface-hover"
                )}
              >
                <Icon className="w-4 h-4 shrink-0" />
                <span>{item.label}</span>
              </Link>
            );
          })}
        </nav>

        {/* User Profile & Sign Out Footer */}
        <div className="p-3 border-t border-border mt-auto">
          <div className="flex items-center justify-between p-2 rounded-lg bg-surface-hover/60 border border-border/40">
            <div className="flex items-center gap-2.5 min-w-0">
              <div className="w-8 h-8 rounded-full bg-primary/20 text-primary font-bold flex items-center justify-center text-xs shrink-0">
                {session?.fullName ? session.fullName[0].toUpperCase() : "A"}
              </div>
              <div className="min-w-0 flex-1">
                <p className="text-xs font-medium text-foreground truncate">
                  {session?.fullName || "Yönetici"}
                </p>
                <p className="text-[10px] text-muted-foreground truncate">
                  {session?.role || "Admin"}
                </p>
              </div>
            </div>
            <button
              onClick={handleLogout}
              className="p-1.5 text-muted-foreground hover:text-danger rounded-md hover:bg-surface-active transition-colors cursor-pointer"
              title="Çıkış Yap"
            >
              <LogOut className="w-4 h-4" />
            </button>
          </div>
        </div>
      </aside>

      {/* Mobile Sidebar Drawer Overlay */}
      {mobileMenuOpen && (
        <div className="fixed inset-0 z-50 lg:hidden flex">
          <div
            className="fixed inset-0 bg-black/60 backdrop-blur-xs"
            onClick={() => setMobileMenuOpen(false)}
          />
          <div className="relative w-72 max-w-[80vw] bg-surface border-r border-border h-full flex flex-col z-10 p-4">
            <div className="flex items-center justify-between pb-4 border-b border-border">
              <div className="flex items-center gap-2 font-bold text-sm">
                <Sparkles className="w-4 h-4 text-primary" />
                <span>AppointAI Admin</span>
              </div>
              <button
                onClick={() => setMobileMenuOpen(false)}
                className="p-1.5 rounded-md hover:bg-surface-hover text-muted-foreground"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <nav className="flex-1 space-y-1 py-4 overflow-y-auto">
              {NAV_ITEMS.map((item) => {
                const Icon = item.icon;
                const isActive = pathname === item.href;
                return (
                  <Link
                    key={item.href}
                    href={item.href}
                    onClick={() => setMobileMenuOpen(false)}
                    className={cn(
                      "flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors",
                      isActive
                        ? "bg-primary text-primary-foreground shadow-xs"
                        : "text-muted-foreground hover:text-foreground hover:bg-surface-hover"
                    )}
                  >
                    <Icon className="w-4 h-4" />
                    <span>{item.label}</span>
                  </Link>
                );
              })}
            </nav>

            <div className="pt-4 border-t border-border">
              <Button variant="outline" className="w-full gap-2 justify-start" onClick={handleLogout}>
                <LogOut className="w-4 h-4" />
                <span>Çıkış Yap</span>
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* Main Content Area */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Top Navbar */}
        <header className="h-16 border-b border-border bg-surface/80 backdrop-blur-md px-4 sm:px-6 flex items-center justify-between sticky top-0 z-20">
          <div className="flex items-center gap-3">
            <button
              onClick={() => setMobileMenuOpen(true)}
              className="lg:hidden p-2 rounded-lg text-muted-foreground hover:bg-surface-hover cursor-pointer"
              aria-label="Menüyü Aç"
            >
              <Menu className="w-5 h-5" />
            </button>

            {/* Breadcrumbs */}
            <div className="hidden sm:flex items-center gap-2 text-xs text-muted-foreground">
              <span>Admin</span>
              <ChevronRight className="w-3.5 h-3.5" />
              <span className="text-foreground font-medium capitalize">
                {NAV_ITEMS.find((n) => n.href === pathname)?.label || "Panel"}
              </span>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Link href="/app/home" className="hidden sm:inline-flex">
              <Button variant="outline" size="sm" className="gap-2 text-xs">
                <span>Müşteri Uygulaması</span>
                <ChevronRight className="w-3.5 h-3.5" />
              </Button>
            </Link>

            <ThemeToggle />

            <div className="w-8 h-8 rounded-full bg-primary/10 text-primary flex items-center justify-center font-bold text-xs">
              {session?.fullName ? session.fullName[0].toUpperCase() : "A"}
            </div>
          </div>
        </header>

        {/* Viewport Content */}
        <main className="flex-1 p-4 sm:p-6 md:p-8 overflow-y-auto">
          {children}
        </main>
      </div>
    </div>
  );
}

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={<div className="min-h-screen bg-background" />}>
      <AdminLayoutInner>{children}</AdminLayoutInner>
    </Suspense>
  );
}
