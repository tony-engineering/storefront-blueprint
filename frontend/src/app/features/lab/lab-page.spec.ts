import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LabRun, LabStatus, LabTest } from './lab.models';
import { ACTIVE_POLL_MS, LabPage } from './lab-page';

const tests: LabTest[] = [
  {
    id: 'steady',
    title: 'Busy Saturday',
    context: 'c',
    requests: 600,
    shape: 's',
    mix: 'm',
    delay: 'd',
    expected: 'e',
  },
  {
    id: 'flash-sale',
    title: 'Flash sale',
    context: 'c',
    requests: 300,
    shape: 's',
    mix: 'm',
    delay: 'd',
    expected: 'e',
  },
];

const run = (phase: LabRun['phase'], extra: Partial<LabRun> = {}): LabRun => ({
  id: 'run-1',
  testId: 'flash-sale',
  phase,
  percent: 40,
  startedAt: '2026-10-23T10:00:00Z',
  finishedAt: null,
  audit: [],
  error: null,
  sampleCount: 0,
  samples: [],
  ...extra,
});

const status = (extra: Partial<LabStatus> = {}): LabStatus => ({
  enabled: true,
  busy: false,
  cooldownEndsAt: null,
  run: null,
  ...extra,
});

describe('LabPage', () => {
  let fixture: ComponentFixture<LabPage>;
  let http: HttpTestingController;

  beforeEach(async () => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LabPage);
    fixture.detectChanges();
    http.expectOne('/api/lab/tests').flush(tests);
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
  });

  const flushStatus = (value: LabStatus) => {
    http.expectOne((r) => r.url === '/api/lab/status').flush(value);
    fixture.detectChanges();
  };
  const el = () => fixture.nativeElement as HTMLElement;
  const runButtons = () =>
    Array.from(el().querySelectorAll<HTMLButtonElement>('.sf-panel-foot button'));

  it('lets a visitor start a test when nothing is running', () => {
    flushStatus(status());

    expect(runButtons()).toHaveLength(2);
    expect(runButtons().every((b) => !b.disabled)).toBe(true);
  });

  it('disables every Run button while a test is running, and says why', () => {
    flushStatus(status({ busy: true, run: run('Sending') }));

    expect(runButtons().every((b) => b.disabled)).toBe(true);
    expect(el().textContent).toContain('Only one test runs at a time');
  });

  it('disables the buttons during the cooldown and counts down', async () => {
    const ends = new Date(Date.now() + 30_000).toISOString();
    flushStatus(status({ cooldownEndsAt: ends, run: run('Done') }));

    expect(runButtons().every((b) => b.disabled)).toBe(true);
    expect(el().textContent).toMatch(/Cooling down: (29|30) s/);
  });

  it('shows the three steps and marks the current one', () => {
    flushStatus(status({ busy: true, run: run('Sending') }));

    const steps = Array.from(el().querySelectorAll('.sf-step'));
    expect(steps.map((s) => s.textContent?.trim())).toEqual([
      '1Initializing',
      '2Sending',
      '3Finalizing',
    ]);
    expect(steps.map((s) => s.classList.contains('is-done'))).toEqual([true, false, false]);
    expect(steps[1].classList.contains('is-current')).toBe(true);
  });

  it('polls once a second during a run and asks only for the samples it has not seen', async () => {
    flushStatus(
      status({ busy: true, run: run('Sending', { sampleCount: 1, samples: [sampleAt(1)] }) }),
    );

    await vi.advanceTimersByTimeAsync(ACTIVE_POLL_MS);

    const request = http.expectOne((r) => r.url === '/api/lab/status');
    expect(request.request.params.get('after')).toBe('1');
    expect(request.request.params.get('run')).toBe('run-1');
    request.flush(
      status({ busy: true, run: run('Sending', { sampleCount: 2, samples: [sampleAt(2)] }) }),
    );
  });

  it('shows a passing audit', () => {
    flushStatus(
      status({
        run: run('Done', {
          percent: 100,
          audit: [
            { id: 'created', label: 'Orders created match', result: 'pass', detail: '300 orders' },
          ],
        }),
      }),
    );

    expect(el().querySelector('.sf-check.is-pass')?.textContent).toContain('Orders created match');
    expect(el().textContent).toContain('Passed');
  });
});

function sampleAt(t: number) {
  return {
    t,
    sent: t * 10,
    rps: 5,
    inFlight: 1,
    p50: 10,
    p95: 20,
    latencyBuckets: [0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0],
    errors: 0,
    replies: { '201': t },
    orders: { Paid: t },
    ordersTotal: t,
  };
}
