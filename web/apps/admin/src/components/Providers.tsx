"use client";

import React, { useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ConfirmProvider } from "@malabak/ui";
import { ApiError } from "@/lib/api";
import { StepUpProvider } from "@/lib/stepup";

export function Providers({ children }: { children: React.ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          // A missing permission (403) or missing item (404) will not change on retry.
          queries: {
            staleTime: 30_000,
            refetchOnWindowFocus: true,
            retry: (count, error) => !(error instanceof ApiError && [401, 403, 404].includes(error.status)) && count < 1,
          },
        },
      }),
  );
  return (
    <QueryClientProvider client={client}>
      <StepUpProvider>
        <ConfirmProvider>{children}</ConfirmProvider>
      </StepUpProvider>
    </QueryClientProvider>
  );
}
