import { Component, DestroyRef, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReviewService } from '../../services/review.service';
import { ReviewSummaryResponse } from '../../models/review.model';
import { normalizeApiError } from '../../services/api-error';
import { AuthService } from '../../services/auth.service';
import { User } from '../../models/user.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  reviewSummary: ReviewSummaryResponse | null = null;
  reviewSummaryLoading = false;
  reviewSummaryError = '';
  reviewsDisabled = false;
  currentUser: User | null = null;

  dashboardCards = [
    {
      id: 'main',
      title: 'Vocabulary Builder',
      description: 'Add, edit, and manage your personal vocabulary collection',
      icon: '📚',
      isActive: true,
      route: '/vocabulary'
    },
    {
      id: 'analytics',
      title: 'Learning Analytics',
      description: 'Track your progress and learning statistics',
      icon: '📊',
      isActive: false,
      route: '/analytics'
    },
    {
      id: 'preferences',
      title: 'Preferences',
      description: 'Customize your learning experience and settings',
      icon: '⚙️',
      isActive: false,
      route: '/preferences'
    },
    {
      id: 'admin',
      title: 'Admin Panel',
      description: 'Administrative tools and user management',
      icon: '👤',
      isActive: false,
      route: '/admin'
    }
  ];

  constructor(
    private authService: AuthService,
    private router: Router,
    private reviews: ReviewService
  ) { }

  ngOnInit(): void {
    // Subscribe to current user
    this.authService.currentUser$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(user => {
      if (this.currentUser?.id !== user?.id) {
        this.reviewSummary = null; this.reviewSummaryLoading = false; this.reviewSummaryError = ''; this.reviewsDisabled = false;
      }
      this.currentUser = user;
      if (!user) {
        this.router.navigate(['/login']);
      } else if (this.authService.isAuthenticated()) {
        this.loadReviewSummary();
      }
    });

    // Check authentication
    if (!this.authService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }
  }

  loadReviewSummary(): void {
    if (this.reviewSummaryLoading) return;
    this.reviewSummaryLoading = true; this.reviewSummaryError = '';
    this.reviews.summary().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: summary => { this.reviewSummary = summary; this.reviewSummaryLoading = false; this.reviewsDisabled = false; },
      error: error => {
        const failure = normalizeApiError(error, 'review-summary', 'Unable to load review counts.');
        this.reviewSummaryError = failure.message; this.reviewsDisabled = failure.code === 'reviews_disabled';
        this.reviewSummaryLoading = false;
      }
    });
  }

  onCardClick(card: any): void {
    if (card.isActive) {
      this.router.navigate([card.route]);
    } else {
      // Show coming soon message or do nothing
      console.log(`${card.title} is coming soon!`);
    }
  }

  logout(): void {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
