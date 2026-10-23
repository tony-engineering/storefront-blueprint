import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { LabRun, LabStatus, LabTest } from './lab.models';

/** Typed wrapper over the load generator. `/api/lab` is routed to it by nginx, the Ingress or the dev proxy. */
@Injectable({ providedIn: 'root' })
export class LabApiService {
  private readonly http = inject(HttpClient);

  tests() {
    return this.http.get<LabTest[]>('/api/lab/tests');
  }

  /** The current or last run; `after` and `run` ask for the samples this visitor has not seen yet. */
  status(after: number, run: string | null) {
    let params = new HttpParams().set('after', after);
    if (run) {
      params = params.set('run', run);
    }
    return this.http.get<LabStatus>('/api/lab/status', { params });
  }

  /** 202 started · 409 another test is running · 429 cooling down · 404 unknown test. */
  start(testId: string) {
    return this.http.post<LabRun>('/api/lab/runs', { testId });
  }
}
