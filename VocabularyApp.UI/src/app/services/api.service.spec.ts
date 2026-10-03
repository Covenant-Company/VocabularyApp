import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

import { ApiService } from './api.service';
import { environment } from '../../environments/environment';
import { AddWordRequest, FavoriteRequest, FavoriteResponse, VocabularyResponseDto, vocabularySearchPath } from '../models/word-api.model';
import { AddToVocabularyResult } from '../models/word-lookup.model';
import { currentAuthSuccess, currentLookupSuccess, currentQuizSuccess, currentVocabularySuccess, quizApplicationFailure, quizValidationFailure } from '../testing/api-contract.fixtures';
import { QuizStartResponse, QuizSubmitResponse, QuizHistoryResponse, StartQuizRequest, QuizSubmitRequest } from '../models/quiz.model';

describe('ApiService', () => {
  let service: ApiService;
  let authService: jasmine.SpyObj<AuthService>;
  let http: HttpTestingController;

  beforeEach(() => {
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['getToken']);
    authService.getToken.and.returnValue(null);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authService }
      ]
    });
    service = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // These characterize transport and backend-shaped success bodies only. Component
  // guards/error adaptation and corrected production models belong to Phase 2+.
  const quiz = currentQuizSuccess();
  const user = currentAuthSuccess('2026-01-02T03:05:05Z').data.user;
  const operations = [
    { method: 'GET', path: '/users/profile', body: undefined,
      response: { success: true, data: user, error: null } },
    { method: 'GET', path: '/users/validate-token', body: undefined,
      response: { success: true, data: user, error: null } },
    { method: 'POST', path: '/users/change-password',
      body: { currentPassword: 'Old test password!', newPassword: 'New test password!' },
      response: { success: true, message: 'Request succeeded.', data: null, error: null } },
    { method: 'GET', path: '/words/lookup/test', body: undefined, response: currentLookupSuccess() },
    { method: 'POST', path: '/words/vocabulary/add', body: { word: 'test', preferredWordDefinitionId: 11 },
      response: { success: true, data: { userWordId: 7, wordId: 3, alreadyExisted: false, message: 'Word added to your vocabulary' } } },
    { method: 'GET', path: '/words/vocabulary?page=1&pageSize=20', body: undefined, response: currentVocabularySuccess() },
    { method: 'GET', path: '/words/vocabulary/search?term=test', body: undefined, response: currentVocabularySuccess(5) },
    { method: 'PUT', path: '/words/vocabulary/7/favorite', body: { isFavorite: false },
      response: { success: true, data: { message: 'Word removed from favorites', userWordId: 7, isFavorite: false } } },
    { method: 'PUT', path: '/words/vocabulary/7/preferred-definition', body: { preferredWordDefinitionId: 12 },
      response: { success: true, data: { message: 'Preferred definition updated', userWordId: 7, preferredWordDefinitionId: 12 } } },
    { method: 'POST', path: '/quiz/start', body: { questionCount: 1, mode: 'word-to-definition' }, response: quiz.start },
    { method: 'POST', path: '/quiz/submit', body: { sessionId: quiz.start.data.sessionId, answers: [] }, response: quiz.submit },
    { method: 'GET', path: '/quiz/history?take=5', body: undefined, response: quiz.history }
  ];

  for (const operation of operations) {
    it(`preserves ${operation.method} ${operation.path} and the current server success fixture`, () => {
      authService.getToken.and.returnValue('test-bearer');
      const response$ = operation.method === 'POST'
        ? service.post<unknown>(operation.path, operation.body)
        : operation.method === 'PUT'
          ? service.put<unknown>(operation.path, operation.body)
          : service.get<unknown>(operation.path);
      let received: unknown;
      response$.subscribe(value => received = value);
      const request = http.expectOne(environment.apiUrl + operation.path);
      expect(request.request.method).toBe(operation.method);
      expect(request.request.body).toEqual(operation.body ?? null);
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      expect(request.request.headers.get('Content-Type')).toBe('application/json');
      request.flush(operation.response);
      // Deep comparison keeps null keys/nested flags/GUID strings and numeric IDs visible.
      expect(received).toEqual(operation.response);
    });
  }

  for (const operation of [operations[5], operations[4], operations[7]]) {
    it(`omits Authorization for ${operation.method} when no token exists`, () => {
      const response$ = operation.method === 'POST' ? service.post<unknown>(operation.path, operation.body)
        : operation.method === 'PUT' ? service.put<unknown>(operation.path, operation.body) : service.get<unknown>(operation.path);
      const error = jasmine.createSpy('anonymous challenge');
      response$.subscribe({ next: () => fail('Expected an authentication challenge'), error });
      const request = http.expectOne(environment.apiUrl + operation.path);
      expect(request.request.method).toBe(operation.method);
      expect(request.request.headers.has('Authorization')).toBeFalse();
      // Flush the framework challenge; this checks header construction, not anonymous access.
      request.flush(null, { status: 401, statusText: 'Unauthorized' });
      expect(error).toHaveBeenCalledTimes(1);
    });
  }

  it('preserves a caller-encoded search query without double encoding', () => {
    const term = 'a & b+c/?';
    service.get<unknown>(`/words/vocabulary/search?term=${encodeURIComponent(term)}`).subscribe();
    const request = http.expectOne(environment.apiUrl + '/words/vocabulary/search?term=a%20%26%20b%2Bc%2F%3F');
    expect(request.request.method).toBe('GET');
    request.flush({ success: true, data: { words: [], totalCount: 0, page: 1, pageSize: 5, totalPages: 0 } });
  });

  // Statuses are supplied HTTP failures, not assertions about a server's current
  // classification. Future server remapping must still stay on the error channel.
  for (const status of [400, 401, 404, 409, 500, 503]) {
    it(`keeps HTTP ${status} on the error channel`, () => {
      const next = jasmine.createSpy('next');
      const error = jasmine.createSpy('error');
      service.get<unknown>('/words/vocabulary').subscribe({ next, error });
      const body = { success: false, error: 'Test failure' };
      http.expectOne(environment.apiUrl + '/words/vocabulary').flush(body, { status, statusText: 'Test failure' });
      expect(next).not.toHaveBeenCalled();
      expect(error).toHaveBeenCalledTimes(1);
      const failure: HttpErrorResponse = error.calls.mostRecent().args[0];
      expect(failure.status).toBe(status);
      expect(failure.error).toEqual(body);
    });
  }
  it('preserves a typed add request and numeric acknowledgement fields', () => {
    const body: AddWordRequest = { word: 'test', preferredWordDefinitionId: 11, example: null };
    let received: AddToVocabularyResult | null | undefined;
    service.post<AddToVocabularyResult, AddWordRequest>('/words/vocabulary/add', body).subscribe(response => received = response.data);
    const request = http.expectOne(environment.apiUrl + '/words/vocabulary/add');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    const data = { userWordId: 7, wordId: 3, alreadyExisted: false, message: 'Word added to your vocabulary' };
    request.flush({ success: true, data });
    expect(received).toEqual(data);
  });
  it('preserves a typed favorite request including explicit false', () => {
    const body: FavoriteRequest = { isFavorite: false };
    let received: FavoriteResponse | null | undefined;
    service.put<FavoriteResponse, FavoriteRequest>('/words/vocabulary/7/favorite', body).subscribe(response => received = response.data);
    const request = http.expectOne(environment.apiUrl + '/words/vocabulary/7/favorite');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(body);
    const data = { userWordId: 7, isFavorite: false, message: 'Word removed from favorites' };
    request.flush({ success: true, data });
    expect(received).toEqual(data);
  });

  for (const term of [undefined, '', '   ', 'a & b+c/?']) {
    it(`sends typed vocabulary search for ${JSON.stringify(term)} with auth and complete metadata`, () => {
      authService.getToken.and.returnValue('test-bearer');
      let received: VocabularyResponseDto | null | undefined;
      service.get<VocabularyResponseDto>(vocabularySearchPath(term)).subscribe(response => received = response.data);
      const path = term === undefined ? '/words/vocabulary/search' : `/words/vocabulary/search?term=${encodeURIComponent(term)}`;
      const request = http.expectOne(environment.apiUrl + path);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      const data: VocabularyResponseDto = { words: [], totalCount: 0, page: 1, pageSize: 5, totalPages: 0 };
      request.flush({ success: true, data });
      expect(received).toEqual(data);
    });
  }

  it('preserves typed quiz start transport and string session/date fields', () => {
    authService.getToken.and.returnValue('test-bearer');
    const body: StartQuizRequest = { questionCount: 1, mode: 'word-to-definition' };
    let received: QuizStartResponse | null | undefined;
    service.post<QuizStartResponse, StartQuizRequest>('/quiz/start', body).subscribe(response => received = response.data);
    const request = http.expectOne(environment.apiUrl + '/quiz/start');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(body);
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    request.flush(quiz.start);
    expect(received).toEqual(quiz.start.data);
    expect(received?.expiresAtUtc).toBe(quiz.start.data.expiresAtUtc);
  });

  for (const answered of [false, true]) {
    it(`preserves typed quiz submit transport for ${answered ? 'explicit zero' : 'unanswered'} answers`, () => {
      authService.getToken.and.returnValue('test-bearer');
      const body: QuizSubmitRequest = { sessionId: quiz.start.data.sessionId,
        answers: answered ? [{ questionId: quiz.start.data.questions[0].questionId, selectedOptionId: 0 }] : [] };
      let received: QuizSubmitResponse | null | undefined;
      service.post<QuizSubmitResponse, QuizSubmitRequest>('/quiz/submit', body).subscribe(response => received = response.data);
      const request = http.expectOne(environment.apiUrl + '/quiz/submit');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(body);
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      const data: QuizSubmitResponse = answered ? { ...quiz.submit.data, correctAnswers: 1, scorePercentage: 100,
        questionResults: quiz.submit.data.questionResults.map(result => ({ ...result, selectedAnswer: 'A trial', isCorrect: true })) } : quiz.submit.data;
      request.flush({ success: true, data });
      expect(received).toEqual(data);
      expect(received?.questionResults[0].selectedAnswer).toBe(answered ? 'A trial' : null);
    });
  }

  it('preserves typed quiz history transport and timestamps as strings', () => {
    authService.getToken.and.returnValue('test-bearer');
    let received: QuizHistoryResponse | null | undefined;
    service.get<QuizHistoryResponse>('/quiz/history?take=5').subscribe(response => received = response.data);
    const request = http.expectOne(environment.apiUrl + '/quiz/history?take=5');
    expect(request.request.method).toBe('GET');
    expect(request.request.body).toBeNull();
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    request.flush(quiz.history);
    expect(received).toEqual(quiz.history.data);
    expect(received?.items[0].attemptedAtUtc).toBe(quiz.history.data.items[0].attemptedAtUtc);
  });

  const quizFailures = [
    { status: 400, body: quizValidationFailure() },
    { status: 401, body: null },
    { status: 404, body: quizApplicationFailure('quiz_session_unavailable') },
    { status: 409, body: quizApplicationFailure('quiz_submission_conflict') },
    { status: 409, body: quizApplicationFailure('quiz_vocabulary_changed') },
    { status: 500, body: quizApplicationFailure('internal_error') }
  ];
  for (const failure of quizFailures) {
    it(`keeps typed quiz submission HTTP ${failure.status} on the HttpClient error channel without retry`, () => {
      authService.getToken.and.returnValue('test-bearer');
      const body: QuizSubmitRequest = { sessionId: quiz.start.data.sessionId, answers: [] };
      const next = jasmine.createSpy('quiz success');
      const error = jasmine.createSpy('quiz error');
      service.post<QuizSubmitResponse, QuizSubmitRequest>('/quiz/submit', body).subscribe({ next, error });
      const request = http.expectOne(environment.apiUrl + '/quiz/submit');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(body);
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      request.flush(failure.body, { status: failure.status, statusText: 'Failure' });
      expect(next).not.toHaveBeenCalled();
      expect(error).toHaveBeenCalledTimes(1);
      const actual: HttpErrorResponse = error.calls.mostRecent().args[0];
      expect(actual.status).toBe(failure.status);
      expect(actual.error).toEqual(failure.body);
      http.expectNone(environment.apiUrl + '/quiz/submit');
    });
  }

  it('retains the error channel for typed vocabulary search', () => {
    authService.getToken.and.returnValue('test-bearer');
    const error = jasmine.createSpy('search error');
    service.get<VocabularyResponseDto>(vocabularySearchPath('test')).subscribe({ next: () => fail('Expected failure'), error });
    const request = http.expectOne(environment.apiUrl + '/words/vocabulary/search?term=test');
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    request.flush({ code: 'internal_error' }, { status: 500, statusText: 'Failure' });
    expect(error).toHaveBeenCalledTimes(1);
  });
});
