import { createProxy } from "@malabak/bff/server";
import { bffConfig } from "@/lib/bff-config";

export const dynamic = "force-dynamic";

export const { GET, POST, PUT, PATCH, DELETE } = createProxy(bffConfig);
