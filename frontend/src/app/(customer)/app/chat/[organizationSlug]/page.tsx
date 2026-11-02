"use client";

import React, { use, Suspense } from "react";
import { ChatContainer } from "@/components/chat/ChatContainer";

function OrganizationChatContent({
  params,
}: {
  params: Promise<{ organizationSlug: string }>;
}) {
  const resolvedParams = use(params);
  return <ChatContainer initialOrgSlug={resolvedParams.organizationSlug} />;
}

export default function OrganizationChatPage({
  params,
}: {
  params: Promise<{ organizationSlug: string }>;
}) {
  return (
    <Suspense
      fallback={
        <div className="h-[calc(100vh-4rem)] flex items-center justify-center text-xs text-muted-foreground">
          Sohbet yükleniyor...
        </div>
      }
    >
      <OrganizationChatContent params={params} />
    </Suspense>
  );
}
