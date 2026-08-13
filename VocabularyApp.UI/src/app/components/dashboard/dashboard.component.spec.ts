import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { DashboardComponent } from './dashboard.component';
import { AuthService } from '../../services/auth.service';

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;
  let authService: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['isAuthenticated', 'logout'], {
      currentUser$: of({ username: 'testuser', email: 'test@example.com' })
    });
    authService.isAuthenticated.and.returnValue(true);

    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render Vocabulary Builder as a meaningful link to the vocabulary route', () => {
    const link = fixture.nativeElement.querySelector('[data-card-id="main"]') as HTMLAnchorElement;

    expect(link).toBeTruthy();
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe('/vocabulary');
    expect(link.textContent).toContain('Vocabulary Builder');
  });

  it('should render unavailable dashboard concepts as non-interactive content', () => {
    for (const cardId of ['analytics', 'preferences', 'admin']) {
      const card = fixture.nativeElement.querySelector(`[data-card-id="${cardId}"]`) as HTMLElement;

      expect(card).toBeTruthy();
      expect(card.tagName).toBe('DIV');
      expect(card.querySelector('a, button')).toBeNull();
      expect(card.getAttribute('role')).toBeNull();
      expect(card.getAttribute('tabindex')).toBeNull();
      expect(card.textContent).toContain('Coming Soon');
    }
  });

  it('should keep logout as an explicit button', () => {
    const root = fixture.nativeElement as HTMLElement;
    const logoutButton = Array.from(root.querySelectorAll('button'))
      .find(button => button.textContent?.trim() === 'Logout') as HTMLButtonElement;

    expect(logoutButton).toBeTruthy();
    expect(logoutButton.type).toBe('button');
  });
});
