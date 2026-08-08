import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { ApiService } from '../../services/api.service';
import { QuizHistoryItem, QuizHistoryResponse } from '../../models/quiz.model';

type TrendWindow = '14d' | '30d' | '90d' | 'all';

interface TrendPoint {
  attemptedAt: Date;
  score: number;
}

@Component({
  selector: 'app-learning-analytics',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './learning-analytics.component.html',
  styleUrl: './learning-analytics.component.scss'
})
export class LearningAnalyticsComponent implements OnInit {
  readonly windows: Array<{ key: TrendWindow; label: string; take: number }> = [
    { key: '14d', label: 'Last 14 days', take: 30 },
    { key: '30d', label: 'Last 30 days', take: 60 },
    { key: '90d', label: 'Last 90 days', take: 120 },
    { key: 'all', label: 'All', take: 200 }
  ];

  readonly chartWidth = 900;
  readonly chartHeight = 320;
  readonly chartPadding = { top: 20, right: 24, bottom: 44, left: 38 };
  readonly gridLevels = [0, 25, 50, 75, 100];

  isLoading = false;
  errorMessage = '';
  selectedWindow: TrendWindow = '30d';
  historyItems: QuizHistoryItem[] = [];

  constructor(
    private apiService: ApiService,
    private router: Router
  ) { }

  ngOnInit(): void {
    this.loadTrendHistory();
  }

  backToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }

  setWindow(window: TrendWindow): void {
    if (this.selectedWindow === window) {
      return;
    }

    this.selectedWindow = window;
    this.loadTrendHistory();
  }

  get trendPoints(): TrendPoint[] {
    const points = this.historyItems
      .map(item => ({
        attemptedAt: new Date(item.attemptedAtUtc),
        score: Number(item.scorePercentage) || 0
      }))
      .filter(point => !Number.isNaN(point.attemptedAt.getTime()))
      .sort((a, b) => a.attemptedAt.getTime() - b.attemptedAt.getTime());

    if (this.selectedWindow === 'all') {
      return points;
    }

    const days = this.selectedWindow === '14d' ? 14 : this.selectedWindow === '30d' ? 30 : 90;
    const cutoff = new Date();
    cutoff.setDate(cutoff.getDate() - days);

    return points.filter(point => point.attemptedAt >= cutoff);
  }

  get hasData(): boolean {
    return this.trendPoints.length > 0;
  }

  get averageScore(): number {
    if (!this.hasData) {
      return 0;
    }

    const sum = this.trendPoints.reduce((acc, point) => acc + point.score, 0);
    return Math.round((sum / this.trendPoints.length) * 10) / 10;
  }

  get bestScore(): number {
    if (!this.hasData) {
      return 0;
    }

    return Math.max(...this.trendPoints.map(point => point.score));
  }

  get latestScore(): number {
    if (!this.hasData) {
      return 0;
    }

    return this.trendPoints[this.trendPoints.length - 1].score;
  }

  get chartPathPoints(): string {
    if (!this.hasData) {
      return '';
    }

    return this.trendPoints
      .map((point, index) => {
        const coordinate = this.getCoordinate(index, point.score);
        return `${coordinate.x},${coordinate.y}`;
      })
      .join(' ');
  }

  get firstLabel(): string {
    if (!this.hasData) {
      return '';
    }

    return this.formatDate(this.trendPoints[0].attemptedAt);
  }

  get lastLabel(): string {
    if (!this.hasData) {
      return '';
    }

    return this.formatDate(this.trendPoints[this.trendPoints.length - 1].attemptedAt);
  }

  getPointX(index: number): number {
    return this.getCoordinate(index, 0).x;
  }

  getPointY(score: number): number {
    return this.getCoordinate(0, score).y;
  }

  formatTooltip(point: TrendPoint): string {
    return `${this.formatDateTime(point.attemptedAt)} - ${point.score}%`;
  }

  private loadTrendHistory(): void {
    this.errorMessage = '';
    this.isLoading = true;

    const selected = this.windows.find(item => item.key === this.selectedWindow) ?? this.windows[1];

    this.apiService.get<QuizHistoryResponse>(`/quiz/history?take=${selected.take}`).subscribe({
      next: response => {
        if (response.success && response.data) {
          this.historyItems = response.data.items || [];
        } else {
          this.historyItems = [];
          this.errorMessage = response.message || 'Unable to load analytics data.';
        }

        this.isLoading = false;
      },
      error: error => {
        this.historyItems = [];
        this.errorMessage = error?.error?.error || error?.error?.errorMessage || 'Unable to load analytics data.';
        this.isLoading = false;
      }
    });
  }

  private getCoordinate(index: number, score: number): { x: number; y: number } {
    const usableWidth = this.chartWidth - this.chartPadding.left - this.chartPadding.right;
    const usableHeight = this.chartHeight - this.chartPadding.top - this.chartPadding.bottom;
    const denominator = Math.max(this.trendPoints.length - 1, 1);

    const x = this.chartPadding.left + (index / denominator) * usableWidth;
    const y = this.chartPadding.top + ((100 - score) / 100) * usableHeight;

    return {
      x: Math.round(x * 100) / 100,
      y: Math.round(y * 100) / 100
    };
  }

  private formatDate(value: Date): string {
    return value.toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric'
    });
  }

  private formatDateTime(value: Date): string {
    return value.toLocaleString(undefined, {
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit'
    });
  }
}
