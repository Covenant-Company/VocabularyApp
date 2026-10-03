import { QuizStartResponse, QuizSubmitResponse, QuizHistoryResponse } from '../models/quiz.model';

// Test-only wire fixtures reviewed against backend DTOs/controllers and
// ApiContractShapeTests. Do not cast these to the currently mismatched UI models.
export function currentAuthSuccess(lastLoginAt: string | null = null) {
  return {
    success: true,
    data: {
      success: true, errorMessage: null, token: 'test-only-token',
      user: {
        id: 7, username: 'contract-user', email: 'contract@example.test',
        createdAt: '2026-01-02T03:04:05Z', lastLoginAt
      }
    },
    error: null
  };
}

export function currentLookupSuccess() {
  return {
    success: true,
    data: {
      success: true, errorMessage: null,
      word: {
        id: 3, text: 'test', pronunciation: null, audioUrl: null,
        createdAt: '2026-01-02T03:04:05',
        definitions: [{
          id: 11, definition: 'A trial', example: null,
          partOfSpeech: 'Noun', partOfSpeechAbbreviation: 'n.', displayOrder: 1
        }]
      },
      wasFoundInCache: true, isInUserVocabulary: false
    }
  };
}

export function currentVocabularySuccess(pageSize = 20) {
  return {
    success: true,
    data: {
      words: [{
        id: 7, word: 'test', definition: 'A trial', preferredWordDefinitionId: 11,
        example: null, partOfSpeech: 'Noun', pronunciation: null, audioUrl: null,
        addedAt: '2026-01-02T03:04:05', isFavorite: false, personalNotes: null,
        correctAnswers: 0, totalAttempts: 0, accuracyRate: null
      }],
      totalCount: 1, page: 1, pageSize, totalPages: 1
    }
  };
}

// Quiz transport models now match these wire shapes; pin their compile-time alignment.
export function currentQuizSuccess(): {
  start: { success: true; data: QuizStartResponse };
  submit: { success: true; data: QuizSubmitResponse };
  history: { success: true; data: QuizHistoryResponse };
} {
  const sessionId = '10000000-0000-0000-0000-000000000001';
  const questionId = '20000000-0000-0000-0000-000000000001';
  return {
    start: {
      success: true,
      data: {
        sessionId, mode: 'word-to-definition', questionCount: 1,
        expiresAtUtc: '2026-01-02T03:34:05Z',
        questions: [{
          questionId, questionType: 'word-to-definition',
          prompt: 'Choose the correct definition for "test"',
          options: [
            { optionId: 0, text: 'A trial' }, { optionId: 1, text: 'A fruit' },
            { optionId: 2, text: 'A vehicle' }, { optionId: 3, text: 'A building' }
          ]
        }]
      }
    },
    submit: {
      success: true,
      data: {
        totalQuestions: 1, correctAnswers: 0, scorePercentage: 0,
        questionResults: [{
          questionId, questionType: 'word-to-definition',
          prompt: 'Choose the correct definition for "test"',
          correctAnswer: 'A trial', selectedAnswer: null, isCorrect: false
        }]
      }
    },
    history: {
      success: true,
      data: { items: [{
        attemptedAtUtc: '2026-01-02T03:05:05',
        totalQuestions: 1, correctAnswers: 0, scorePercentage: 0
      }] }
    }
  };
}

// Phase 3 wire shapes; messages match the backend allowlist, including opaque trace IDs.
export function quizApplicationFailure(code: 'quiz_session_unavailable' | 'quiz_submission_conflict' | 'quiz_vocabulary_changed' | 'invalid_quiz_answers' | 'internal_error') {
  const messages = {
    quiz_session_unavailable: 'This quiz session is unavailable. Please start a new quiz.',
    quiz_submission_conflict: 'This quiz submission is already being processed or has been submitted.',
    quiz_vocabulary_changed: 'Your vocabulary changed. Please start a new quiz.',
    invalid_quiz_answers: 'The quiz answers are invalid.',
    internal_error: 'An internal error occurred. Please try again.'
  };
  return { success: false, data: null, error: messages[code], message: messages[code], code, traceId: 'opaque-test-trace' };
}

export function quizValidationFailure() {
  return {
    type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
    title: 'One or more validation errors occurred.', status: 400,
    errors: { '$.answers[0]': ['Invalid value.'] }, traceId: 'opaque-test-trace',
    success: false, data: null, code: 'validation_failed',
    error: 'One or more request fields are invalid.', message: 'One or more request fields are invalid.'
  };
}
