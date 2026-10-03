import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { QuizComponent } from './quiz.component';
import { environment } from '../../../environments/environment';
import { currentQuizSuccess, quizApplicationFailure, quizValidationFailure } from '../../testing/api-contract.fixtures';
import { INTERNAL_ERROR } from '../../services/api-error';
import { AuthService } from '../../services/auth.service';
import { QuizMode } from '../../models/quiz.model';

describe('QuizComponent quiz contracts', () => {
  let component: QuizComponent;
  let fixture: ComponentFixture<QuizComponent>;
  let http: HttpTestingController;
  beforeEach(async () => {
    spyOn(localStorage, 'getItem').and.returnValue(null);
    await TestBed.configureTestingModule({ imports: [QuizComponent], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] }).compileComponents();
    spyOn(TestBed.inject(AuthService), 'getToken').and.returnValue('test-bearer');
    fixture = TestBed.createComponent(QuizComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); fixture.destroy(); });

  it('normalizes a legacy start failure without creating a session', () => {
    component.startQuiz();
    const request = http.expectOne(environment.apiUrl + '/quiz/start');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ questionCount: 10, mode: 'mixed' });
    request.flush({ error: 'You need at least 4 saved words with definitions to start a quiz.' }, { status: 400, statusText: 'Failure' });
    expect(component.errorMessage).toBe('You need at least 4 saved words with definitions to start a quiz.');
    expect(component.quizSession).toBeNull();
    expect(component.isLoading).toBeFalse();
  });
  it('preserves the session and option zero on a failed submission without retrying', () => {
    const fixture = currentQuizSuccess();
    component.startQuiz();
    http.expectOne(environment.apiUrl + '/quiz/start').flush(fixture.start);
    const session = component.quizSession;
    component.selectOption(0);
    component.nextQuestion();
    const request = http.expectOne(environment.apiUrl + '/quiz/submit');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ sessionId: fixture.start.data.sessionId,
      answers: [{ questionId: fixture.start.data.questions[0].questionId, selectedOptionId: 0 }] });
    request.flush({ success: false, data: null, error: 'SECRET', message: 'SECRET', code: 'internal_error', traceId: 'opaque' }, { status: 500, statusText: 'Failure' });
    expect(component.errorMessage).toBe(INTERNAL_ERROR);
    expect(component.quizSession).toBe(session);
    expect(component.selectedOptionId).toBe(0);
    expect(component.quizResult).toBeNull();
    expect(component.isSubmitting).toBeFalse();
    http.expectNone(environment.apiUrl + '/quiz/submit');
  });
  it('reports history failure without replacing previous results', () => {
    const previous = currentQuizSuccess().history.data.items;
    component.toggleQuizHistory();
    component.quizHistory = previous;
    http.expectOne(environment.apiUrl + '/quiz/history?take=5').flush({ error: 'SECRET' }, { status: 500, statusText: 'Failure' });
    expect(component.quizHistory).toBe(previous);
    expect(component.quizHistoryError).toBe(INTERNAL_ERROR);
    expect(component.quizHistoryLoading).toBeFalse();
  });

  for (const mode of ['mixed', 'word-to-definition', 'definition-to-word'] as QuizMode[]) {
    it(`starts ${mode} with the typed body, Bearer header and string identifiers/dates`, () => {
      component.mode = mode;
      component.questionCount = 1;
      component.startQuiz();
      component.startQuiz();
      const request = http.expectOne(environment.apiUrl + '/quiz/start');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ questionCount: 1, mode });
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      expect(component.isLoading).toBeTrue();
      const response = currentQuizSuccess().start;
      const questions = mode === 'definition-to-word' ? response.data.questions.map(question => ({
        ...question, questionType: 'definition-to-word', prompt: 'Choose the correct word for this definition: "A trial"',
        options: [{ optionId: 0, text: 'test' }, { optionId: 1, text: 'apple' },
          { optionId: 2, text: 'car' }, { optionId: 3, text: 'house' }]
      })) : response.data.questions;
      request.flush({ ...response, data: { ...response.data, mode, questions } });
      expect(component.quizSession?.sessionId).toBe(response.data.sessionId);
      expect(component.quizSession?.expiresAtUtc).toBe(response.data.expiresAtUtc);
      expect(component.quizSession?.mode).toBe(mode);
      expect(component.currentQuestion?.options[0].optionId).toBe(0);
      expect(component.progressText).toBe('1 / 1');
      expect(component.isLoading).toBeFalse();
      http.expectNone(environment.apiUrl + '/quiz/start');
    });
  }

  it('submits explicit option zero once and renders a successful result', () => {
    const response = currentQuizSuccess();
    component.startQuiz();
    http.expectOne(environment.apiUrl + '/quiz/start').flush(response.start);
    component.selectOption(0);
    component.nextQuestion();
    component.nextQuestion();
    component.selectOption(1);
    component.previousQuestion();
    component.restartQuiz();
    const request = http.expectOne(environment.apiUrl + '/quiz/submit');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
    expect(request.request.body).toEqual({ sessionId: response.start.data.sessionId,
      answers: [{ questionId: response.start.data.questions[0].questionId, selectedOptionId: 0 }] });
    expect(component.isSubmitting).toBeTrue();
    expect(component.selectedOptionId).toBe(0);
    fixture.detectChanges();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    expect(buttons.find(button => button.textContent?.includes('Submit Quiz'))?.disabled).toBeTrue();
    expect(buttons.find(button => button.textContent?.includes('A trial'))?.disabled).toBeTrue();
    http.expectNone(environment.apiUrl + '/quiz/start');
    const data = { ...response.submit.data, correctAnswers: 1, scorePercentage: 100,
      questionResults: response.submit.data.questionResults.map(result => ({ ...result, selectedAnswer: 'A trial', isCorrect: true })) };
    request.flush({ success: true, data });
    expect(component.quizResult).toEqual(data);
    expect(component.quizResult?.questionResults[0].selectedAnswer).toBe('A trial');
    expect(component.isSubmitting).toBeFalse();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Quiz Complete');
    expect(fixture.nativeElement.textContent).toContain('Your answer: A trial');
    component.nextQuestion();
    http.expectNone(environment.apiUrl + '/quiz/submit');
  });

  it('encodes an unanswered submission as an empty array and preserves and renders its nullable answer', () => {
    const response = currentQuizSuccess();
    component.startQuiz();
    http.expectOne(environment.apiUrl + '/quiz/start').flush(response.start);
    component['submitQuiz']();
    const request = http.expectOne(environment.apiUrl + '/quiz/submit');
    expect(request.request.body).toEqual({ sessionId: response.start.data.sessionId, answers: [] });
    request.flush(response.submit);
    expect(component.quizResult?.questionResults[0].selectedAnswer).toBeNull();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Your answer: No answer');
  });

  it('retains answers across navigation and omits genuinely unselected questions from a partial request', () => {
    const response = currentQuizSuccess();
    const first = response.start.data.questions[0];
    const second = { ...first, questionId: '20000000-0000-0000-0000-000000000002',
      prompt: 'Choose the correct definition for "apple"',
      options: [{ optionId: 0, text: 'A fruit' }, { optionId: 1, text: 'A trial' },
        { optionId: 2, text: 'A vehicle' }, { optionId: 3, text: 'A building' }] };
    component.startQuiz();
    http.expectOne(environment.apiUrl + '/quiz/start').flush({ ...response.start,
      data: { ...response.start.data, questionCount: 2, questions: [first, second] } });
    component.selectOption(0);
    component.nextQuestion();
    component.previousQuestion();
    expect(component.selectedOptionId).toBe(0);
    component.nextQuestion();
    expect(component.selectedOptionId).toBeNull();
    // The normal UI requires a selection to advance. Exercise its existing private
    // transport path directly to protect the backend's valid partial-answer encoding.
    component['submitQuiz']();
    const request = http.expectOne(environment.apiUrl + '/quiz/submit');
    expect(request.request.body).toEqual({ sessionId: response.start.data.sessionId,
      answers: [{ questionId: first.questionId, selectedOptionId: 0 }] });
    request.flush(quizApplicationFailure('internal_error'), { status: 500, statusText: 'Failure' });
    component.previousQuestion();
    expect(component.selectedOptionId).toBe(0);
    http.expectNone(environment.apiUrl + '/quiz/submit');
  });

  it('requires a selected answer before advancing without issuing a submission', () => {
    component.startQuiz();
    http.expectOne(environment.apiUrl + '/quiz/start').flush(currentQuizSuccess().start);
    component.nextQuestion();
    expect(component.errorMessage).toBe('Please select an answer before continuing.');
    expect(component.isSubmitting).toBeFalse();
    http.expectNone(environment.apiUrl + '/quiz/submit');
  });

  const submitFailures = [
    { status: 400, body: quizValidationFailure(), message: 'One or more request fields are invalid.', restart: false },
    { status: 400, body: quizApplicationFailure('invalid_quiz_answers'), message: 'Please check your quiz answers.', restart: false },
    { status: 404, body: quizApplicationFailure('quiz_session_unavailable'), message: quizApplicationFailure('quiz_session_unavailable').message, restart: true },
    { status: 409, body: quizApplicationFailure('quiz_submission_conflict'), message: quizApplicationFailure('quiz_submission_conflict').message, restart: false },
    { status: 409, body: quizApplicationFailure('quiz_vocabulary_changed'), message: quizApplicationFailure('quiz_vocabulary_changed').message, restart: true },
    { status: 401, body: null, message: 'Please sign in to continue.', restart: false },
    { status: 500, body: { error: 'System.Exception SECRET', message: 'SECRET' }, message: INTERNAL_ERROR, restart: false }
  ];
  for (const failure of submitFailures) {
    it(`handles submission HTTP ${failure.status} ${failure.body && 'code' in failure.body ? failure.body.code : 'without code'} safely and preserves state without retry`, () => {
      component.startQuiz();
      http.expectOne(environment.apiUrl + '/quiz/start').flush(currentQuizSuccess().start);
      const session = component.quizSession;
      component.selectOption(0);
      component.nextQuestion();
      http.expectOne(environment.apiUrl + '/quiz/submit').flush(failure.body, { status: failure.status, statusText: 'Failure' });
      expect(component.errorMessage).toBe(failure.message);
      expect(component.quizSession).toBe(session);
      expect(component.selectedOptionId).toBe(0);
      expect(component.quizResult).toBeNull();
      expect(component.isSubmitting).toBeFalse();
      expect(component.quizRecoveryRequiresRestart).toBe(failure.restart);
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain(failure.message);
      expect(fixture.nativeElement.textContent).not.toContain('SECRET');
      const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
      expect(buttons.some(button => button.textContent?.includes('Start a new quiz'))).toBe(failure.restart);
      if (failure.restart) {
        expect(buttons.find(button => button.textContent?.includes('Submit Quiz'))?.disabled).toBeTrue();
        component.nextQuestion();
      }
      http.expectNone(environment.apiUrl + '/quiz/start');
      http.expectNone(environment.apiUrl + '/quiz/submit');
    });
  }

  it('starts a replacement only after an explicit recovery action and clears old selections', () => {
    component.startQuiz();
    http.expectOne(environment.apiUrl + '/quiz/start').flush(currentQuizSuccess().start);
    component.selectOption(0);
    component.nextQuestion();
    http.expectOne(environment.apiUrl + '/quiz/submit').flush(quizApplicationFailure('quiz_session_unavailable'), { status: 404, statusText: 'Not found' });
    http.expectNone(environment.apiUrl + '/quiz/start');
    fixture.detectChanges();
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    buttons.find(button => button.textContent?.includes('Start a new quiz'))!.click();
    const request = http.expectOne(environment.apiUrl + '/quiz/start');
    expect(component.quizSession).toBeNull();
    expect(component.quizRecoveryRequiresRestart).toBeFalse();
    expect(component.errorMessage).toBe('');
    request.flush(currentQuizSuccess().start);
    expect(component.selectedOptionId).toBeNull();
    expect(component.quizResult).toBeNull();
  });

  for (const empty of [false, true]) {
    it(`loads ${empty ? 'empty' : 'populated'} history with the existing route, header and date strings`, () => {
      component.toggleQuizHistory();
      const request = http.expectOne(environment.apiUrl + '/quiz/history?take=5');
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-bearer');
      const items = empty ? [] : currentQuizSuccess().history.data.items;
      request.flush({ success: true, data: { items } });
      expect(component.quizHistory).toEqual(items);
      expect(component.quizHistoryLoading).toBeFalse();
      expect(component.quizHistoryError).toBe('');
      if (!empty) expect(typeof component.quizHistory[0].attemptedAtUtc).toBe('string');
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent.includes('No quiz attempts yet.')).toBe(empty);
    });
  }

  it('does not render failed initial history as a successful empty history', () => {
    component.toggleQuizHistory();
    http.expectOne(environment.apiUrl + '/quiz/history?take=5').flush(quizApplicationFailure('internal_error'), { status: 500, statusText: 'Failure' });
    fixture.detectChanges();
    expect(component.quizHistoryError).toBe(INTERNAL_ERROR);
    expect(fixture.nativeElement.textContent).toContain(INTERNAL_ERROR);
    expect(fixture.nativeElement.textContent).not.toContain('No quiz attempts yet.');
  });

  it('rejects a malformed successful submission without losing the session or retrying', () => {
    component.startQuiz();
    http.expectOne(environment.apiUrl + '/quiz/start').flush(currentQuizSuccess().start);
    const session = component.quizSession;
    component.selectOption(0);
    component.nextQuestion();
    http.expectOne(environment.apiUrl + '/quiz/submit').flush({ success: true, data: null });
    expect(component.errorMessage).toBe('Unable to submit quiz.');
    expect(component.quizSession).toBe(session);
    expect(component.quizResult).toBeNull();
    expect(component.isSubmitting).toBeFalse();
    http.expectNone(environment.apiUrl + '/quiz/submit');
  });
});
