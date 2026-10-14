import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ReviewService } from '../../services/review.service';
import { currentReviewSuccess, reviewApplicationFailure } from '../../testing/api-contract.fixtures';
import { AuthService } from '../../services/auth.service';
import { User } from '../../models/user.model';

import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;
  let reviews: jasmine.SpyObj<ReviewService>;
  const user: User = { id: 1, username: 'test-user', email: 'test@example.com', createdAt: '2026-01-02T03:04:05Z', lastLoginAt: null };

  beforeEach(async () => {
    const authService = jasmine.createSpyObj<AuthService>(
      'AuthService', ['isAuthenticated', 'logout'], { currentUser$: of({ ...user }) });
    authService.isAuthenticated.and.returnValue(true);
    reviews = jasmine.createSpyObj<ReviewService>('ReviewService', ['summary']);
    reviews.summary.and.returnValue(of(currentReviewSuccess().summary.data));
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        provideRouter([]),
        { provide: ReviewService, useValue: reviews }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
    expect(component.currentUser).toEqual(user);
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('test-user');
  });
  it('shows independent server review counts and an accessible entry', () => {
    expect(fixture.nativeElement.textContent).toContain('2 due reviews');
    const link = fixture.nativeElement.querySelector('a[href="/reviews"]');
    expect(link.textContent).toContain('Review Vocabulary');
    expect(reviews.summary).toHaveBeenCalledTimes(1);
  });
  it('hides the review action when disabled without suggesting zero due words', () => {
    reviews.summary.and.returnValue(throwError(() => new HttpErrorResponse({ status: 503, error: reviewApplicationFailure('reviews_disabled') })));
    component.loadReviewSummary(); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Vocabulary reviews are not available right now.');
    expect(fixture.nativeElement.querySelector('a[href="/reviews"]')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('0 due reviews');
  });
  it('keeps infrastructure failure separate from a successful zero count and allows retry', () => {
    reviews.summary.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500, error: { error: 'SECRET' } })));
    component.loadReviewSummary(); fixture.detectChanges(); expect(fixture.nativeElement.textContent).not.toContain('SECRET');
    expect(fixture.nativeElement.textContent).not.toContain('2 due reviews');
    reviews.summary.and.returnValue(of(currentReviewSuccess().summary.data)); component.loadReviewSummary(); fixture.detectChanges();
    expect(component.reviewSummaryError).toBe(''); expect(fixture.nativeElement.textContent).toContain('2 due reviews');
  });
  it('preserves vocabulary navigation and logout', () => {
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    component.onCardClick(component.dashboardCards[0]); expect(navigate).toHaveBeenCalledWith(['/vocabulary']);
    component.logout(); expect(navigate).toHaveBeenCalledWith(['/login']); expect(TestBed.inject(AuthService).logout).toHaveBeenCalled();
  });
});
