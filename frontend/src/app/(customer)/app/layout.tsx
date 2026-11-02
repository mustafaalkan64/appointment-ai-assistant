"use client";

import React, { Suspense } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  Home,
  MessageSquare,
  CalendarCheck,
  Building2,
  User,
  Sparkles
} from "lucide-react";
import { cn } from "@/lib/utils";
import { ThemeToggle } from "@/components/layout/ThemeToggle";
import { useAuth } from "@/providers/providers";

const CUSTOMER_NAV = [
  { href: "/app/home", label: "Ana Sayfa", icon: Home },
  { href: "/app/organizations", label: "Kurumlar", icon: Building2 },
  { href: "/app/chat", label: "AI Asistan", icon: MessageSquare, isPrimary: true },
  { href: "/app/my-appointments", label: "Randevularım", icon: CalendarCheck },
  { href: "/app/profile", label: "Profilim", icon: User },
];

function CustomerLayoutInner({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { session } = useAuth();
  const isChatPage = pathname.startsWith("/app/chat");

  return (
    <div className="min-h-screen flex flex-col bg-background text-foreground">
      {/* Customer Desktop & Tablet Top Header */}
      <header className="h-16 border-b border-border bg-surface/80 backdrop-blur-md px-4 sm:px-6 md:px-8 flex items-center justify-between sticky top-0 z-30">
        <div className="flex items-center gap-6">
          <Link href="/app/home" className="flex items-center gap-2.5 font-bold tracking-tight">
            <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center text-primary-foreground shadow-xs">
              <Sparkles className="w-4 h-4 fill-current" />
            </div>
            <span className="text-base font-semibold">
              Appoint<span className="text-primary font-bold">AI</span>
            </span>
          </Link>

          {/* Desktop Navigation Links */}
          <nav className="hidden md:flex items-center gap-1">
            {CUSTOMER_NAV.map((item) => {
              const Icon = item.icon;
              const isActive = pathname === item.href || (item.href !== "/app/home" && pathname.startsWith(item.href));

              if (item.isPrimary) {
                return (
                  <Link
                    key={item.href}
                    href={item.href}
                    className={cn(
                      "flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-sm font-semibold transition-all ml-2",
                      isActive
                        ? "bg-primary text-primary-foreground shadow-sm shadow-primary/25"
                        : "bg-primary/10 text-primary hover:bg-primary/20"
                    )}
                  >
                    <Sparkles className="w-4 h-4" />
                    <span>{item.label}</span>
                  </Link>
                );
              }

              return (
                <Link
                  key={item.href}
                  href={item.href}
                  className={cn(
                    "flex items-center gap-2 px-3 py-1.5 rounded-lg text-sm font-medium transition-colors",
                    isActive
                      ? "text-primary bg-surface-hover font-semibold"
                      : "text-muted-foreground hover:text-foreground hover:bg-surface-hover"
                  )}
                >
                  <Icon className="w-4 h-4" />
                  <span>{item.label}</span>
                </Link>
              );
            })}
          </nav>
        </div>

        {/* Right Actions */}
        <div className="flex items-center gap-3">
          <Link href="/admin/dashboard" className="hidden lg:inline-flex text-xs text-muted-foreground hover:text-foreground">
            Kurum Paneli →
          </Link>
          <ThemeToggle />
          <Link href="/app/profile" className="flex items-center gap-2">
            <div className="w-8 h-8 rounded-full bg-primary/20 text-primary font-bold flex items-center justify-center text-xs">
              {session?.fullName ? session.fullName[0].toUpperCase() : "A"}
            </div>
          </Link>
        </div>
      </header>

      {/* Main Content Area (padding-bottom reserved on mobile except when chat page manages its own viewport) */}
      <main className={cn("flex-1 flex flex-col min-w-0", !isChatPage && "pb-20 md:pb-6")}>
        {children}
      </main>

      {/* Mobile Bottom Navigation Bar (Hidden on Desktop, optimized so it doesn't overlap chat) */}
      {!isChatPage && (
        <nav className="md:hidden fixed bottom-0 left-0 right-0 h-16 border-t border-border bg-surface/95 backdrop-blur-lg px-2 flex items-center justify-around z-40">
          {CUSTOMER_NAV.map((item) => {
            const Icon = item.icon;
            const isActive = pathname === item.href || (item.href !== "/app/home" && pathname.startsWith(item.href));

            if (item.isPrimary) {
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  className="flex flex-col items-center justify-center -mt-5"
                >
                  <div className="w-12 h-12 rounded-full bg-primary text-primary-foreground flex items-center justify-center shadow-lg shadow-primary/30 border-2 border-surface">
                    <Sparkles className="w-5 h-5 fill-current" />
                  </div>
                  <span className="text-[10px] font-semibold text-primary mt-0.5">{item.label}</span>
                </Link>
              );
            }

            return (
              <Link
                key={item.href}
                href={item.href}
                className={cn(
                  "flex flex-col items-center justify-center py-1 px-2 rounded-md transition-colors",
                  isActive ? "text-primary font-medium" : "text-muted-foreground hover:text-foreground"
                )}
              >
                <Icon className="w-5 h-5" />
                <span className="text-[10px] mt-0.5">{item.label}</span>
              </Link>
            );
          })}
        </nav>
      )}
    </div>
  );
}

export default function CustomerLayout({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={<div className="min-h-screen bg-background" />}>
      <CustomerLayoutInner>{children}</CustomerLayoutInner>
    </Suspense>
  );
}
