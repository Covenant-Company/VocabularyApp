import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { currentAuthSuccess } from '../../testing/api-contract.fixtures';
import { ApiContractError, INTERNAL_ERROR } from '../../services/api-error';
import { AuthService } from '../../services/auth.service';

import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let authService: jasmine.SpyObj<AuthService>;
  let router: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['login', 'isAuthenticated']);
    authService.isAuthenticated.and.returnValue(false);
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.returnValue(Promise.resolve(true));
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { queryParams: of({}) } }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('submits credentials unchanged and navigates after nested auth success', () => {
    authService.login.and.returnValue(of(currentAuthSuccess()));
    component.loginForm.setValue({ username: ' user ', password: ' password ' });
    component.onSubmit();
    expect(authService.login).toHaveBeenCalledWith({ username: ' user ', password: ' password ' });
    expect(router.navigate).toHaveBeenCalledWith(['/dashboard']);
    expect(component.isLoading).toBeFalse();
  });

  const failures = [
    { status: 401, body: null, message: 'Invalid username or password' },
    { status: 500, body: { message: 'SECRET', error: 'SECRET' }, message: INTERNAL_ERROR },
    { status: 400, body: { status: 400, title: 'Validation', errors: { Username: ['The Username field is required.'] } }, message: 'The Username field is required.' }
  ];
  for (const test of failures) {
    it(`renders a safe login HTTP ${test.status} message without navigation`, () => {
      authService.login.and.returnValue(throwError(() => new HttpErrorResponse({ status: test.status, error: test.body })));
      component.loginForm.setValue({ username: 'user', password: 'password' });
      component.onSubmit();
      fixture.detectChanges();
      expect(component.errorMessage).toBe(test.message);
      expect(fixture.nativeElement.textContent).toContain(test.message);
      expect(fixture.nativeElement.textContent).not.toContain('SECRET');
      expect(router.navigate).not.toHaveBeenCalled();
      expect(component.isLoading).toBeFalse();
    });
  }
  it('does not navigate on a controlled malformed-success error', () => {
    authService.login.and.returnValue(throwError(() => new ApiContractError()));
    component.loginForm.setValue({ username: 'user', password: 'password' });
    component.onSubmit();
    expect(component.errorMessage).toBe('Unable to complete login. Please try again.');
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
