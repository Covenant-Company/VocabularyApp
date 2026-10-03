import { HttpErrorResponse } from '@angular/common/http';
import { ApiOperation, DICTIONARY_UNAVAILABLE, INTERNAL_ERROR, normalizeApiError } from './api-error';

describe('normalizeApiError', () => {
  const fallback = 'Please try the operation again.';
  function normalize(body: unknown, status = 400, operation: ApiOperation = 'register') {
    return normalizeApiError(new HttpErrorResponse({ status, error: body }), operation, fallback);
  }

  for (const field of ['error', 'message', 'errorMessage']) {
    it(`supports the current ${field} message`, () => {
      expect(normalize({ [field]: 'Username is already taken' }).message).toBe('Username is already taken');
    });
  }
  it('prefers recognized future codes over error/message aliases', () => {
    expect(normalize({ success: false, data: null, error: 'old text', message: 'alias',
      code: 'username_taken', traceId: 'opaque' })).toEqual({ message: 'Username is already taken', code: 'username_taken' });
  });
  it('uses legacy text when the future code is unknown', () => {
    expect(normalize({ code: 'future_unknown', error: 'Please select a definition.' })).toEqual({ message: 'Please select a definition.' });
  });
  it('prefers error then message then errorMessage and ignores blanks', () => {
    expect(normalize({ error: 'First', message: 'Second', errorMessage: 'Third' }).message).toBe('First');
    expect(normalize({ error: ' ', message: 'Second', errorMessage: 'Third' }).message).toBe('Second');
  });
  for (const extended of [false, true]) {
    it(`supports ${extended ? 'extended' : 'current'} validation ProblemDetails safely`, () => {
      const result = normalize({ type: 'https://example.test/problem', title: 'One or more validation errors occurred.',
        status: 400, errors: { Username: ['The Username field is required.'] },
        ...(extended ? { success: false, data: null, error: 'Validation', message: 'Validation', code: 'validation_failed', traceId: 'opaque' } : {}) });
      expect(result.message).toBe('One or more request fields are invalid.');
      expect(result.fieldErrors).toEqual({ Username: ['The Username field is required.'] });
    });
  }
  it('replaces binding diagnostics and ignores unknown validation fields', () => {
    const result = normalize({ status: 400, title: 'Bad input', errors: {
      Password: ["The value 'SECRET' could not be converted to System.String.", '<b>SECRET</b>', 'SECRET', 'fourth'],
      '$.password': ['System.Exception SECRET'], Unknown: ['SECRET']
    } });
    expect(result.fieldErrors).toEqual({ Password: ['Invalid value.', 'Invalid value.', 'Invalid value.'] });
    expect(JSON.stringify(result)).not.toContain('SECRET');
  });
  it('uses a safe ProblemDetails summary without reflecting title or detail', () => {
    expect(normalize({ status: 400, title: 'System.Exception SECRET', detail: 'SECRET' }).message)
      .toBe('The request could not be completed. Please check your input.');
  });
  const contextual: { status: number; operation: ApiOperation; message: string }[] = [
    { status: 401, operation: 'login', message: 'Invalid username or password' },
    { status: 401, operation: 'vocabulary-list', message: 'Please sign in to continue.' },
    { status: 401, operation: 'password-change', message: 'Current password is incorrect' },
    { status: 404, operation: 'lookup', message: 'Word not found. Please check the spelling and try again.' },
    { status: 404, operation: 'favorite', message: 'This word is unavailable in your vocabulary.' },
    { status: 404, operation: 'preferred-definition', message: 'This word is unavailable in your vocabulary.' },
    { status: 404, operation: 'quiz-submit', message: 'This quiz session is unavailable. Please start a new quiz.' },
    { status: 409, operation: 'quiz-submit', message: 'This quiz submission is already being processed or has been submitted.' },
    { status: 503, operation: 'lookup', message: DICTIONARY_UNAVAILABLE },
    { status: 0, operation: 'lookup', message: 'Unable to connect. Please check your connection and try again.' }
  ];
  for (const test of contextual) {
    it(`handles ${test.operation} HTTP ${test.status} by context`, () => {
      expect(normalize(null, test.status, test.operation).message).toBe(test.message);
    });
  }
  for (const body of [
    { error: 'System.Exception SECRET', message: 'SECRET', code: 'username_taken' },
    '<html>SECRET</html>', new Error('SECRET'),
    { errors: { Password: ['SECRET'] }, title: 'SECRET', status: 500 }
  ]) {
    it('never exposes a raw or malicious 500 body', () => {
      expect(normalize(body, 500)).toEqual({ message: INTERNAL_ERROR });
    });
  }
  for (const body of ['<html>SECRET</html>', { exception: 'SECRET' }, new Error('SECRET'), null,
    { error: '<script>SECRET</script>' }, { error: { nested: 'SECRET' } }, { message: 'System.Exception SECRET' }]) {
    it('uses fallback for unrecognized or unsafe bodies', () => {
      expect(normalize(body)).toEqual({ message: fallback });
    });
  }
  it('does not display HttpErrorResponse.message or a 200 contract body', () => {
    expect(normalizeApiError(new Error('SECRET'), 'login', fallback).message).toBe(fallback);
    expect(normalize({ error: 'SECRET' }, 200).message).toBe(fallback);
    expect(normalize(undefined).message).toBe(fallback);
  });
  it('does not expose provider details on lookup 503', () => {
    expect(normalize({ error: 'Provider key SECRET' }, 503, 'lookup')).toEqual({ message: DICTIONARY_UNAVAILABLE });
  });
  it('handles structural HTTP errors and direct validation fixtures without trusting status text', () => {
    expect(normalizeApiError({ status: 500, error: { message: 'SECRET' } }, 'login', fallback)).toEqual({ message: INTERNAL_ERROR });
    expect(normalizeApiError({ status: 400, title: 'Validation', errors: { Password: ['SECRET'] } }, 'register', fallback))
      .toEqual({ message: 'One or more request fields are invalid.', fieldErrors: { Password: ['Invalid value.'] } });
  });
});
