import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { User } from '../../models/user.model';

import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;
  const user: User = { id: 1, username: 'test-user', email: 'test@example.com' };

  beforeEach(async () => {
    const authService = jasmine.createSpyObj<AuthService>(
      'AuthService', ['isAuthenticated', 'logout'], { currentUser$: of({ ...user }) });
    authService.isAuthenticated.and.returnValue(true);
    const router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.returnValue(Promise.resolve(true));
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router }
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
});
