"use client";

import React, { createContext, useContext, useEffect, useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider as NextThemesProvider } from "next-themes";
import { Toaster } from "sonner";
import { UserSession } from "@/types";

interface AuthContextType {
  session: UserSession | null;
  login: (session: UserSession) => void;
  logout: () => void;
  isAuthenticated: boolean;
  isAdmin: boolean;
}

const AuthContext = createContext<AuthContextType>({
  session: null,
  login: () => {},
  logout: () => {},
  isAuthenticated: false,
  isAdmin: false
});

export function useAuth() {
  return useContext(AuthContext);
}

// Default seed session for quick testing / fallback
const DEFAULT_DEMO_SESSION: UserSession = {
  id: "c63fa787-d893-4b6d-8372-f1abe6fe97aa",
  fullName: "Ali Yılmaz",
  email: "ali@example.com",
  role: "Customer",
  organizationId: "5f8a475a-bcc0-4241-8955-c3d13acee320",
  organizationSlug: "conc-org-6f88d9d7"
};

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 60 * 1000,
            retry: 1,
            refetchOnWindowFocus: false
          }
        }
      })
  );

  const [session, setSession] = useState<UserSession | null>(null);

  useEffect(() => {
    try {
      const stored = localStorage.getItem("ab_session");
      if (stored) {
        setSession(JSON.parse(stored));
      } else {
        // Initialize with demo customer session if empty
        localStorage.setItem("ab_session", JSON.stringify(DEFAULT_DEMO_SESSION));
        setSession(DEFAULT_DEMO_SESSION);
      }
    } catch {
      setSession(DEFAULT_DEMO_SESSION);
    }
  }, []);

  const login = (newSession: UserSession) => {
    localStorage.setItem("ab_session", JSON.stringify(newSession));
    setSession(newSession);
  };

  const logout = () => {
    localStorage.removeItem("ab_session");
    setSession(null);
  };

  const isAuthenticated = !!session;
  const isAdmin = session?.role === "SuperAdmin" || session?.role === "OrganizationAdmin" || session?.role === "OrganizationStaff";

  return (
    <QueryClientProvider client={queryClient}>
      <NextThemesProvider attribute="class" defaultTheme="system" enableSystem>
        <AuthContext.Provider value={{ session, login, logout, isAuthenticated, isAdmin }}>
          {children}
          <Toaster position="top-right" richColors />
        </AuthContext.Provider>
      </NextThemesProvider>
    </QueryClientProvider>
  );
}
