export interface ApiConfig {
  baseUrl: string;
  getAccessToken?: () => Promise<string | null>;
}

export interface PingResponse {
  message: string;
  status: string;
}

export class MalabakApiClient {
  private readonly config: ApiConfig;

  constructor(config: ApiConfig) {
    this.config = config;
  }

  async ping(): Promise<PingResponse> {
    const res = await fetch(`${this.config.baseUrl}/api/v1/ping`);
    if (!res.ok) {
      throw new Error(`Ping failed with status ${res.status}`);
    }
    return res.json() as Promise<PingResponse>;
  }
}
