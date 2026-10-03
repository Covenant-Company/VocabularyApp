import { HttpErrorResponse } from '@angular/common/http';

export type ApiOperation = 'login' | 'register' | 'password-change' | 'lookup'
  | 'vocabulary-add' | 'vocabulary-list' | 'vocabulary-search' | 'favorite'
  | 'preferred-definition' | 'quiz-start' | 'quiz-submit' | 'quiz-history';

export interface ApiError {
  message: string;
  code?: string;
  fieldErrors?: Record<string, string[]>;
}

export const INTERNAL_ERROR = 'An internal error occurred. Please try again.';
export const DICTIONARY_UNAVAILABLE = 'Dictionary service is temporarily unavailable. Please try again.';
const VALIDATION_ERROR = 'One or more request fields are invalid.';
const SIGN_IN_REQUIRED = 'Please sign in to continue.';
const WORD_NOT_FOUND = 'Word not found. Please check the spelling and try again.';
const VOCABULARY_UNAVAILABLE = 'This word is unavailable in your vocabulary.';

const codeMessages: Readonly<Record<string, string>> = {
  internal_error: INTERNAL_ERROR,
  dictionary_unavailable: DICTIONARY_UNAVAILABLE,
  invalid_credentials: 'Invalid username or password',
  username_taken: 'Username is already taken',
  email_taken: 'Email is already registered',
  invalid_token: SIGN_IN_REQUIRED,
  user_unavailable: SIGN_IN_REQUIRED,
  user_not_found: SIGN_IN_REQUIRED,
  current_password_incorrect: 'Current password is incorrect',
  credentials_changed: 'Your credentials changed. Please try again.',
  validation_failed: VALIDATION_ERROR,
  invalid_request: VALIDATION_ERROR,
  word_not_found: WORD_NOT_FOUND,
  vocabulary_not_found: VOCABULARY_UNAVAILABLE,
  canonical_word_required: 'Look up this word before adding it to your vocabulary.',
  invalid_preferred_definition: 'Selected definition is not valid for this word.',
  quiz_unavailable: 'You need at least 4 saved words with definitions to start a quiz.',
  invalid_quiz_answers: 'Please check your quiz answers.',
  quiz_session_unavailable: 'This quiz session is unavailable. Please start a new quiz.',
  quiz_submission_conflict: 'This quiz submission is already being processed or has been submitted.',
  quiz_vocabulary_changed: 'Your vocabulary changed. Please start a new quiz.'
};

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) && !(value instanceof Error);
}

function displayText(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined;
  const text = value.trim();
  // Recognized API message fields only; never render HTML, stack/binding or provider diagnostics.
  if (!text || text.length > 300 || /[<>\r\n]|Exception|System\.|Microsoft\.|SqlClient|stack\s*trace|connection\s*string|RapidAPI|WordsAPI|Bearer\s|password\s*[=:]|api[-_ ]?key\s*[=:]/i.test(text)) return undefined;
  return text;
}

function validationFields(value: unknown): Record<string, string[]> | undefined {
  if (!record(value)) return undefined;
  const result: Record<string, string[]> = {};
  // Only known field names and exact annotation messages can be forwarded.
  // Binding messages often embed attempted secrets or CLR names: replace them.
  const fields = ['Username', 'Email', 'Password', 'CurrentPassword', 'NewPassword', 'Word',
    'PreferredWordDefinitionId', 'IsFavorite', 'SessionId', 'Answers', 'QuestionId', 'SelectedOptionId'];
  for (const field of fields) {
    const messages = value[field] ?? value[field[0].toLowerCase() + field.slice(1)];
    if (!Array.isArray(messages) || !messages.length) continue;
    const allowed = new Set([
      `The ${field} field is required.`,
      ...(field === 'Username' ? ["The field Username must be a string with a minimum length of '3' and a maximum length of '100'."] : []),
      ...(field === 'Email' ? ['The Email field is not a valid e-mail address.', 'The field Email must be a string with a maximum length of 200.'] : []),
      ...(['Password', 'NewPassword'].includes(field) ? [`The field ${field} must be a string with a minimum length of '6' and a maximum length of '100'.`] : [])
    ]);
    result[field] = messages.slice(0, 3).map(message =>
      typeof message === 'string' && allowed.has(message.trim()) ? message.trim() : 'Invalid value.');
  }
  return Object.keys(result).length ? result : undefined;
}

export function normalizeApiError(input: unknown, operation: ApiOperation, fallback: string): ApiError {
  const http = input instanceof HttpErrorResponse ? input : undefined;
  const inputRecord = record(input) ? input : undefined;
  const rawStatus = inputRecord?.['status'];
  const status = http?.status ?? (typeof rawStatus === 'number' ? rawStatus : undefined);
  if (status === 0) return { message: 'Unable to connect. Please check your connection and try again.' };
  if (status !== undefined && status >= 500) {
    return { message: status === 503 && operation === 'lookup' ? DICTIONARY_UNAVAILABLE : INTERNAL_ERROR };
  }
  // A 200 contract failure is not a business error with trusted server text.
  if (status !== undefined && status < 400) return { message: fallback };
  const body: unknown = http ? http.error
    : inputRecord && status !== undefined && !('title' in inputRecord) && !('success' in inputRecord) && 'error' in inputRecord
      ? inputRecord['error'] : input;
  const data = record(body) ? body : undefined;
  const fields = validationFields(data?.['errors']);
  const rawCode = data?.['code'];
  const code = typeof rawCode === 'string' && Object.hasOwn(codeMessages, rawCode) ? rawCode : undefined;
  const contextMessage = status === 401
    ? operation === 'login' ? 'Invalid username or password'
      : operation === 'password-change' ? 'Current password is incorrect' : SIGN_IN_REQUIRED
    : status === 404 && operation === 'lookup' ? WORD_NOT_FOUND
      : status === 404 && (operation === 'favorite' || operation === 'preferred-definition') ? VOCABULARY_UNAVAILABLE
        : status === 404 && operation === 'quiz-submit' ? codeMessages['quiz_session_unavailable']
          : status === 409 && operation === 'quiz-submit' ? codeMessages['quiz_submission_conflict'] : undefined;
  const isValidation = !!data && record(data['errors']);
  const problemDetails = !!data && typeof data['status'] === 'number' && typeof data['title'] === 'string';
  const message = (code ? codeMessages[code] : undefined) ?? contextMessage
    ?? (isValidation ? VALIDATION_ERROR : displayText(data?.['error']) ?? displayText(data?.['message']) ?? displayText(data?.['errorMessage']))
    ?? (problemDetails ? 'The request could not be completed. Please check your input.' : fallback);
  return { message, ...(code ? { code } : {}), ...(fields ? { fieldErrors: fields } : {}) };
}

// Thrown inside RxJS map so malformed 200 bodies use the ordinary error channel.
export class ApiContractError extends Error {
  constructor() { super('The server response did not contain the required data.'); }
}
