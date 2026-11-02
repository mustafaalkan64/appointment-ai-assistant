"use client";

import React, { useState, useRef, useEffect } from "react";
import { Send, Sparkles, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";

interface ChatComposerProps {
  onSendMessage: (message: string) => void;
  isLoading: boolean;
  placeholder?: string;
  suggestedPills?: string[];
  onSelectPill?: (pill: string) => void;
}

export function ChatComposer({
  onSendMessage,
  isLoading,
  placeholder = "Randevu talebinizi veya saatinizi yazın...",
  suggestedPills = [],
  onSelectPill,
}: ChatComposerProps) {
  const [text, setText] = useState("");
  const inputRef = useRef<HTMLTextAreaElement>(null);

  const handleSend = () => {
    if (!text.trim() || isLoading) return;
    onSendMessage(text.trim());
    setText("");
    if (inputRef.current) {
      inputRef.current.style.height = "auto";
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setText(e.target.value);
    // Auto-adjust height up to max 120px
    if (inputRef.current) {
      inputRef.current.style.height = "auto";
      inputRef.current.style.height = `${Math.min(inputRef.current.scrollHeight, 120)}px`;
    }
  };

  return (
    <div className="border-t border-border bg-surface/95 backdrop-blur-md p-3 sm:p-4 space-y-2 sticky bottom-0 z-20">
      {/* Quick Contextual Action Pills */}
      {suggestedPills.length > 0 && (
        <div className="flex items-center gap-1.5 overflow-x-auto pb-1 max-w-4xl mx-auto scrollbar-none">
          <Sparkles className="w-3.5 h-3.5 text-primary shrink-0 mr-0.5" />
          {suggestedPills.map((pill, idx) => (
            <button
              key={idx}
              type="button"
              disabled={isLoading}
              onClick={() => (onSelectPill ? onSelectPill(pill) : onSendMessage(pill))}
              className="px-2.5 py-1 rounded-full border border-border bg-surface hover:border-primary/40 hover:bg-primary/5 text-[11px] font-medium text-muted-foreground hover:text-foreground whitespace-nowrap transition-all shrink-0 active:scale-95"
            >
              {pill}
            </button>
          ))}
        </div>
      )}

      {/* Main Composer Box */}
      <div className="max-w-4xl mx-auto relative flex items-end gap-2 bg-muted/40 rounded-2xl border border-border p-1.5 focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/20 transition-all">
        <textarea
          ref={inputRef}
          value={text}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          rows={1}
          disabled={isLoading}
          className="flex-1 bg-transparent border-0 resize-none text-xs sm:text-sm px-3 py-2 text-foreground placeholder:text-muted-foreground focus:outline-none max-h-32 min-h-[38px] leading-relaxed"
        />

        <Button
          type="button"
          onClick={handleSend}
          disabled={!text.trim() || isLoading}
          size="sm"
          className="h-9 w-9 rounded-xl p-0 shrink-0 shadow-xs font-semibold"
        >
          {isLoading ? (
            <Loader2 className="w-4 h-4 animate-spin" />
          ) : (
            <Send className="w-4 h-4" />
          )}
        </Button>
      </div>

      <div className="text-[10px] text-center text-muted-foreground max-w-4xl mx-auto">
        AI Asistan gerçek zamanlı takvimi kontrol eder. Rezervasyonlar onayınız sonrası kesinleşir.
      </div>
    </div>
  );
}
