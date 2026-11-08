import { NextResponse } from "next/server";

export async function POST(req: Request) {
  try {
    const body = await req.json();
    const apiUrl = process.env.API_URL || process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000";

    const forwardedFor = req.headers.get("x-forwarded-for") || req.headers.get("cf-connecting-ip");
    const userAgent = req.headers.get("user-agent") || "";

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };
    if (forwardedFor) {
      headers["X-Forwarded-For"] = forwardedFor;
    }
    if (userAgent) {
      headers["User-Agent"] = userAgent;
    }

    const res = await fetch(`${apiUrl}/api/v1/waitlist`, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
    });

    const data = await res.json().catch(() => ({}));
    return NextResponse.json(data, { status: res.status });
  } catch (error) {
    console.error("Waitlist proxy error:", error);
    return NextResponse.json(
      { title: "Waitlist.Error", detail: "حدث خطأ غير متوقع أثناء تسجيلك. حاول مرة أخرى لاحقاً." },
      { status: 500 }
    );
  }
}
