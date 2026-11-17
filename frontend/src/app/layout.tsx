import type { Metadata, Viewport } from "next";
import "./globals.generated.css";
import { Providers } from "@/providers/providers";

export const metadata: Metadata = {
  title: "AppointAI – AI-Powered Appointment Booking Platform",
  description:
    "Talk to our AI assistant to discover organizations, schedule appointments, and manage your booking lifecycle in seconds.",
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  maximumScale: 1,
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="tr" suppressHydrationWarning className="h-full">
      <body className="min-h-full flex flex-col font-sans">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
