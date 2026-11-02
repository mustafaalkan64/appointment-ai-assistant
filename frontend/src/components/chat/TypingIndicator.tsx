"use client";

import React from "react";
import { Sparkles, Loader2 } from "lucide-react";

interface TypingIndicatorProps {
  statusText?: string;
}

export function TypingIndicator({ statusText = "Asistan yanıt hazırlıyor..." }: TypingIndicatorProps) {
  return (
    <div className="flex items-start gap-2.5 max-w-md animate-in fade-in slide-in-from-bottom-2 duration-200">
      <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center text-primary shrink-0 mt-0.5">
        <Sparkles className="w-4 h-4 animate-pulse" />
      </div>

      <div className="bg-surface border border-border/80 text-muted-foreground rounded-2xl rounded-tl-xs px-4 py-3 shadow-xs space-y-1.5">
        <div className="flex items-center gap-2 text-xs font-medium">
          <Loader2 className="w-3.5 h-3.5 animate-spin text-primary" />
          <span>{statusText}</span>
        </div>
        <div className="flex items-center gap-1 pl-1">
          <span className="w-1.5 h-1.5 rounded-full bg-primary/40 animate-bounce" style={{ animationDelay: "0ms" }} />
          <span className="w-1.5 h-1.5 rounded-full bg-primary/60 animate-bounce" style={{ animationDelay: "150ms" }} />
          <span className="w-1.5 h-1.5 rounded-full bg-primary animate-bounce" style={{ animationDelay: "300ms" }} />
        </div>
      </div>
    </div>
  );
}
