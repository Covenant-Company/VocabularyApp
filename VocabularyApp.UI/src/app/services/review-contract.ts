import { ApiContractError } from './api-error';
import { ApiResponse } from '../models/user.model';
import { ReviewDueResponse, ReviewStartResponse, ReviewSubmitResponse, ReviewSummaryResponse } from '../models/review.model';

type RecordValue = Record<string, unknown>;
const record = (v: unknown): v is RecordValue => typeof v === 'object' && v !== null && !Array.isArray(v);
export const reviewGuid = (v: unknown): v is string => typeof v === 'string' &&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000';
export const reviewOptionId = (v: unknown): v is number => typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= 3;
const count = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;
const id = (v: unknown): v is number => count(v) && v > 0;
const text = (v: unknown): v is string => typeof v === 'string' && v.trim().length > 0;
const utc = (v: unknown): v is string => typeof v === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?Z$/.test(v) && Number.isFinite(Date.parse(v));
const status = (v: unknown) => ['new', 'learning', 'reviewing'].includes(v as string);
function summary(v: unknown): boolean {
  if (!record(v)) return false;
  const { totalItems, acceptedCount, correctCount, incorrectCount, pendingCount, isComplete } = v;
  return id(totalItems) && totalItems <= 20 && count(acceptedCount) && count(correctCount) && count(incorrectCount) && count(pendingCount)
    && correctCount + incorrectCount === acceptedCount && acceptedCount + pendingCount === totalItems && isComplete === (pendingCount === 0);
}
function receipt(v: unknown): boolean {
  return record(v) && reviewGuid(v['sessionId']) && reviewGuid(v['itemId']) && reviewGuid(v['submissionId']) && utc(v['reviewedAtUtc'])
    && typeof v['isCorrect'] === 'boolean' && reviewOptionId(v['selectedOptionId']) && reviewOptionId(v['correctOptionId'])
    && text(v['selectedAnswer']) && text(v['correctAnswer']) && utc(v['nextReviewAt']) && id(v['reviewIntervalDays']) && v['reviewIntervalDays'] <= 90
    && ['learning', 'reviewing'].includes(v['reviewStatus'] as string) && summary(v['sessionSummary']);
}
function unwrap<T>(value: ApiResponse<T>, valid: (data: unknown) => boolean): T {
  if (!value || value.success !== true || !valid(value.data)) throw new ApiContractError();
  return value.data as T;
}
export function requireReviewDue(value: ApiResponse<ReviewDueResponse>): ReviewDueResponse {
  return unwrap(value, v => record(v) && Array.isArray(v['items']) && v['items'].length <= 100 && utc(v['asOfUtc'])
    && (v['nextCursor'] === null || text(v['nextCursor'])) && v['items'].every(i => record(i) && id(i['userWordId'])
      && typeof i['word'] === 'string' && utc(i['nextReviewAt']) && status(i['reviewStatus']) && typeof i['isUsable'] === 'boolean'));
}
export function requireReviewSummary(value: ApiResponse<ReviewSummaryResponse>): ReviewSummaryResponse {
  return unwrap(value, v => {
    if (!record(v)) return false;
    const { dueCount, newCount, overdueCount, blockedCount, usableDueCount, totalEnrolledCount } = v;
    return count(dueCount) && count(newCount) && count(overdueCount) && count(blockedCount) && count(usableDueCount) && count(totalEnrolledCount)
      && dueCount <= totalEnrolledCount && newCount <= totalEnrolledCount && overdueCount <= dueCount && blockedCount <= dueCount
      && usableDueCount === dueCount - blockedCount && utc(v['asOfUtc']) && (v['nextReviewAt'] === null || utc(v['nextReviewAt']));
  });
}
export function requireReviewReceipt(value: ApiResponse<ReviewSubmitResponse>): ReviewSubmitResponse { return unwrap(value, receipt); }
export function requireReviewStart(value: ApiResponse<ReviewStartResponse>): ReviewStartResponse {
  const result = unwrap(value, v => record(v) && ['ready', 'empty', 'unavailable'].includes(v['availability'] as string) && Array.isArray(v['items']));
  if (result.availability !== 'ready') {
    if (result.sessionId !== null || result.expiresAtUtc !== null || result.sessionSummary !== null || result.items.length !== 0 ||
        (result.availability === 'empty' ? result.reason !== null : !['insufficient_usable_vocabulary', 'ambiguous_options', 'no_usable_due_words'].includes(result.reason ?? '')))
      throw new ApiContractError();
    return result;
  }
  if (result.reason !== null || !reviewGuid(result.sessionId) || !utc(result.expiresAtUtc) || !summary(result.sessionSummary) ||
      result.sessionSummary!.totalItems !== result.items.length || new Set(result.items.map(i => i?.itemId)).size !== result.items.length ||
      new Set(result.items.map(i => i?.userWordId)).size !== result.items.length) throw new ApiContractError();
  for (const item of result.items) {
    if (!item || !reviewGuid(item.itemId) || !id(item.userWordId) || item.questionType !== 'word-to-definition' || !text(item.prompt) ||
        !Array.isArray(item.options) || item.options.length !== 4 || item.options.some(o => !o || !reviewOptionId(o.optionId) || !text(o.text)) ||
        new Set(item.options.map(o => o.optionId)).size !== 4 || new Set(item.options.map(o => o.text.normalize('NFKC').trim().replace(/\s+/g, ' ').toLowerCase())).size !== 4)
      throw new ApiContractError();
    if (item.acceptedReceipt !== null && (!receipt(item.acceptedReceipt) || item.acceptedReceipt.sessionId !== result.sessionId ||
        item.acceptedReceipt.itemId !== item.itemId || item.acceptedReceipt.sessionSummary.totalItems !== result.items.length ||
        !item.options.some(o => o.optionId === item.acceptedReceipt!.selectedOptionId && o.text === item.acceptedReceipt!.selectedAnswer) ||
        !item.options.some(o => o.optionId === item.acceptedReceipt!.correctOptionId && o.text === item.acceptedReceipt!.correctAnswer))) throw new ApiContractError();
  }
  if (result.items.filter(i => i.acceptedReceipt !== null).length !== result.sessionSummary!.acceptedCount) throw new ApiContractError();
  return result;
}
