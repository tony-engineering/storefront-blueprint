/** What the load generator's API (`/api/lab`) returns. */

export interface LabTest {
  id: string;
  title: string;
  context: string;
  requests: number;
  shape: string;
  mix: string;
  delay: string;
  expected: string;
}

export interface LabSample {
  /** Seconds since the run started. */
  t: number;
  sent: number;
  /** Replies received during this second. */
  rps: number;
  inFlight: number;
  p50: number;
  p95: number;
  errors: number;
  replies: Record<string, number>;
  /** Orders by status, as counted by the API. */
  orders: Record<string, number>;
  ordersTotal: number;
}

export interface AuditCheck {
  id: string;
  label: string;
  result: 'pass' | 'fail';
  detail: string;
}

export type RunPhase = 'Initializing' | 'Sending' | 'Finalizing' | 'Done' | 'Failed';

export interface LabRun {
  id: string;
  testId: string;
  phase: RunPhase;
  percent: number;
  startedAt: string;
  finishedAt: string | null;
  audit: AuditCheck[];
  error: string | null;
  sampleCount: number;
  samples: LabSample[];
}

export interface LabStatus {
  enabled: boolean;
  busy: boolean;
  cooldownEndsAt: string | null;
  run: LabRun | null;
}
