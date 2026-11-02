import React from "react";
import Link from "next/link";
import { Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import { ThemeToggle } from "@/components/layout/ThemeToggle";

export default function PublicLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex flex-col bg-background text-foreground">
      {/* Top Navbar */}
      <header className="h-16 border-b border-border bg-surface/80 backdrop-blur-md px-4 sm:px-8 flex items-center justify-between sticky top-0 z-40">
        <Link href="/" className="flex items-center gap-2.5 font-bold tracking-tight">
          <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center text-primary-foreground shadow-xs">
            <Sparkles className="w-4 h-4 fill-current" />
          </div>
          <span className="text-base font-bold">
            Appoint<span className="text-primary">AI</span>
          </span>
        </Link>

        <nav className="hidden md:flex items-center gap-6 text-sm font-medium text-muted-foreground">
          <Link href="/organizations" className="hover:text-foreground transition-colors">
            Kurumları Keşfet
          </Link>
          <Link href="/app/chat" className="hover:text-foreground transition-colors flex items-center gap-1.5 text-primary">
            <Sparkles className="w-3.5 h-3.5" />
            AI Asistan
          </Link>
          <Link href="/admin/dashboard" className="hover:text-foreground transition-colors">
            Kurum Paneli
          </Link>
        </nav>

        <div className="flex items-center gap-3">
          <ThemeToggle />
          <Link href="/login">
            <Button variant="ghost" size="sm" className="text-xs">
              Giriş Yap
            </Button>
          </Link>
          <Link href="/register">
            <Button size="sm" className="text-xs font-semibold">
              Kayıt Ol
            </Button>
          </Link>
        </div>
      </header>

      {/* Main Content */}
      <main className="flex-1">{children}</main>

      {/* SaaS Footer */}
      <footer className="border-t border-border bg-surface py-12 px-4 sm:px-8">
        <div className="max-w-6xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-6 text-xs text-muted-foreground">
          <div className="flex items-center gap-2 font-bold text-foreground">
            <Sparkles className="w-4 h-4 text-primary" />
            <span>AppointAI Platform</span>
          </div>
          <div className="flex items-center gap-6">
            <Link href="/organizations" className="hover:text-foreground">Kurumlar</Link>
            <Link href="/login" className="hover:text-foreground">Giriş Yap</Link>
            <Link href="/register" className="hover:text-foreground">Kurum Kaydı</Link>
            <Link href="/admin/dashboard" className="hover:text-foreground">Yönetici Paneli</Link>
          </div>
          <p>&copy; 2026 AppointAI. Tüm hakları saklıdır.</p>
        </div>
      </footer>
    </div>
  );
}
