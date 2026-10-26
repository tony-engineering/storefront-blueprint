import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { problemMessage } from '../../core/http/problem';
import { LabApiService } from './lab-api.service';
import { LabRun, LabSample, LabStatus, LabTest, RunPhase } from './lab.models';
import { HeatmapChart } from './heatmap-chart';
import { RepliesChart } from './replies-chart';
import { StatusChart } from './status-chart';

export const ACTIVE_POLL_MS = 1000;
export const IDLE_POLL_MS = 5000;

const STEPS: readonly RunPhase[] = ['Initializing', 'Sending', 'Finalizing'];

type StepState = 'done' | 'current' | 'failed' | 'todo';

/**
 * The Lab: start a load test from a fixed list and watch the store cope. The load generator keeps one
 * run in memory; every visitor, including one who arrives mid-run, sees the same run.
 */
@Component({
  selector: 'app-lab-page',
  imports: [StatusChart, RepliesChart, HeatmapChart],
  templateUrl: './lab-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabPage {
  private readonly api = inject(LabApiService);

  protected readonly tests = signal<LabTest[] | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly startError = signal<string | null>(null);
  protected readonly starting = signal<string | null>(null);

  protected readonly enabled = signal(true);
  protected readonly busy = signal(false);
  protected readonly cooldownEndsAt = signal<number | null>(null);
  protected readonly run = signal<LabRun | null>(null);
  protected readonly samples = signal<LabSample[]>([]);

  /** Ticks every second so the cooldown counts down between polls. */
  private readonly now = signal(Date.now());

  protected readonly cooldownSeconds = computed(() => {
    const ends = this.cooldownEndsAt();
    return ends === null ? 0 : Math.max(0, Math.ceil((ends - this.now()) / 1000));
  });

  /** Why the Run buttons are disabled; null when a test can start. */
  protected readonly blockedReason = computed(() => {
    if (!this.enabled()) {
      return 'The lab is switched off.';
    }
    if (this.busy()) {
      return 'A test is running. Only one test runs at a time.';
    }
    if (this.cooldownSeconds() > 0) {
      return `Cooling down: ${this.cooldownSeconds()} s until the next test.`;
    }
    return null;
  });

  protected readonly currentTest = computed(() => {
    const run = this.run();
    return run ? (this.tests()?.find((t) => t.id === run.testId) ?? null) : null;
  });

  protected readonly steps = computed(() => {
    const run = this.run();
    return STEPS.map((phase, index) => ({
      phase,
      number: index + 1,
      state: stepState(run, index),
    }));
  });

  protected readonly latest = computed(() => this.samples().at(-1) ?? null);

  /** The worst second of the run: the last sample is often quiet, which would show 0. */
  protected readonly peakP95 = computed(() => Math.max(0, ...this.samples().map((s) => s.p95)));

  /** Server errors (5xx) so far. Replies are cumulative, so the latest sample holds the run total. */
  protected readonly fivexx = computed(() =>
    Object.entries(this.latest()?.replies ?? {})
      .filter(([code]) => code.startsWith('5'))
      .reduce((sum, [, count]) => sum + count, 0),
  );

  protected readonly progressTone = computed(() => {
    const phase = this.run()?.phase;
    return phase === 'Done' ? 'success' : phase === 'Failed' ? 'danger' : null;
  });

  protected readonly allPassed = computed(() => {
    const audit = this.run()?.audit ?? [];
    return audit.length > 0 && audit.every((c) => c.result === 'pass');
  });

  private timer: ReturnType<typeof setTimeout> | undefined;
  private destroyed = false;

  constructor() {
    const clock = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      clearInterval(clock);
      clearTimeout(this.timer);
    });

    this.api.tests().subscribe({
      next: (tests) => this.tests.set(tests),
      error: (err) => this.loadError.set(problemMessage(err)),
    });
    this.poll();
  }

  protected start(test: LabTest): void {
    if (this.blockedReason() !== null || this.starting() !== null) {
      return;
    }
    this.starting.set(test.id);
    this.startError.set(null);
    this.api.start(test.id).subscribe({
      next: () => {
        this.starting.set(null);
        this.busy.set(true);
        this.samples.set([]);
        this.refresh();
      },
      error: (err: unknown) => {
        this.starting.set(null);
        this.startError.set(startMessage(err));
        this.refresh();
      },
    });
  }

  /** Polls now, then again after the interval that fits: fast while a run is active, slow when idle. */
  private poll(): void {
    clearTimeout(this.timer);
    const run = this.run();
    this.api.status(this.samples().length, run?.id ?? null).subscribe({
      next: (status) => {
        this.apply(status);
        this.schedule();
      },
      error: () => this.schedule(),
    });
  }

  private refresh(): void {
    if (!this.destroyed) {
      this.poll();
    }
  }

  private schedule(): void {
    if (this.destroyed) {
      return;
    }
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.poll(), this.busy() ? ACTIVE_POLL_MS : IDLE_POLL_MS);
  }

  private apply(status: LabStatus): void {
    this.enabled.set(status.enabled);
    this.busy.set(status.busy);
    this.cooldownEndsAt.set(status.cooldownEndsAt ? Date.parse(status.cooldownEndsAt) : null);
    this.now.set(Date.now());

    const incoming = status.run;
    if (!incoming) {
      this.run.set(null);
      this.samples.set([]);
      return;
    }
    // The server sends every sample of a run we have not followed, and only the new ones otherwise.
    const sameRun = this.run()?.id === incoming.id;
    this.samples.update((have) => (sameRun ? [...have, ...incoming.samples] : incoming.samples));
    this.run.set({ ...incoming, samples: [] });
  }
}

function stepState(run: LabRun | null, index: number): StepState {
  if (!run) {
    return 'todo';
  }
  if (run.phase === 'Done') {
    return 'done';
  }
  if (run.phase === 'Failed') {
    const failedAt = run.sampleCount === 0 ? 0 : 1;
    return index < failedAt ? 'done' : index === failedAt ? 'failed' : 'todo';
  }
  const current = STEPS.indexOf(run.phase);
  return index < current ? 'done' : index === current ? 'current' : 'todo';
}

function startMessage(err: unknown): string {
  if (err instanceof HttpErrorResponse) {
    if (err.status === 409) {
      return 'Someone else just started a test. Only one test runs at a time.';
    }
    if (err.status === 429) {
      const wait = err.headers.get('Retry-After');
      return `A test just finished. Try again${wait ? ` in ${wait} s` : ' shortly'}.`;
    }
  }
  return problemMessage(err);
}
