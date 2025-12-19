import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { User } from '../../models/user.model';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
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
    private router: Router
  ) { }

  ngOnInit(): void {
    // Subscribe to current user
    this.authService.currentUser$.subscribe(user => {
      this.currentUser = user;
      if (!user) {
        this.router.navigate(['/login']);
      }
    });

    // Check authentication
    if (!this.authService.isAuthenticated()) {
      this.router.navigate(['/login']);
    }
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
