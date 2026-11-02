import React from "react";
import Link from "next/link";
import { Sparkles } from "lucide-react";
import { ThemeToggle } from "@/components/layout/ThemeToggle";

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex flex-col bg-background text-foreground">
      {/* Auth Header */}
      <header className="h-16 border-b border-border flex items-center justify-between px-6 bg-surface/80 backdrop-blur-md sticky top-0 z-40">
        <Link href="/" className="flex items-center gap-2.5 font-bold text-lg tracking-tight group">
          <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center text-primary-foreground shadow-xs group-hover:scale-105 transition-transform">
            <Sparkles className="w-4 h-4 fill-current" />
          </div>
          <span>Appoint<span className="text-primary">AI</span></span>
        </Link>
        <div className="flex items-center gap-3">
          <ThemeToggle />
        </div>
      </header>

      {/* Main Container */}
      <main className="flex-1 flex items-center justify-center p-4 sm:p-6 md:p-8">
        <div className="w-full max-w-md">
          {children}
        </div>
      </main>

      {/* Footer */}
      <footer className="py-4 text-center text-xs text-muted-foreground border-t border-border">
        &copy; 2026 AppointAI Platform. Tüm hakları saklıdır.
      </footer>
    </div>
  );
}
