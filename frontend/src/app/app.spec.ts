import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  it('creates the shell', () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    expect(TestBed.createComponent(App).componentInstance).toBeTruthy();
  });
});
