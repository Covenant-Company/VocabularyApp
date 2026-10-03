import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

import { SignupComponent } from './signup.component';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { currentAuthSuccess } from '../../testing/api-contract.fixtures';
import { ApiContractError, INTERNAL_ERROR } from '../../services/api-error';

describe('SignupComponent', () => {
  let component: SignupComponent;
  let fixture: ComponentFixture<SignupComponent>;
  let authService: jasmine.SpyObj<AuthService>;
  let router: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    authService = jasmine.createSpyObj<AuthService>('AuthService', ['register']);
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    router.navigate.and.returnValue(Promise.resolve(true));
    await TestBed.configureTestingModule({
      imports: [SignupComponent],
      providers: [
        { provide: AuthService, useValue: authService },
        { provide: Router, useValue: router }
      ]
    })
    .compileComponents();

    fixture = TestBed.createComponent(SignupComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  function fillForm(): void {
    component.signupForm.setValue({ username: 'user', email: 'user@example.test', password: 'password', confirmPassword: 'password' });
  }
  it('accepts 100 characters and rejects 101 with matching template text', () => {
    fillForm();
    const username = component.signupForm.get('username')!;
    username.setValue('a'.repeat(100));
    expect(component.signupForm.valid).toBeTrue();
    username.setValue('a'.repeat(101));
    username.markAsTouched();
    fixture.detectChanges();
    expect(username.hasError('maxlength')).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Username cannot exceed 100 characters');
    username.setValue('ab');
    expect(username.hasError('minlength')).toBeTrue();
  });
  it('navigates to login and excludes confirmPassword after nested registration success', () => {
    authService.register.and.returnValue(of(currentAuthSuccess()));
    fillForm();
    component.onSubmit();
    expect(authService.register).toHaveBeenCalledWith({ username: 'user', email: 'user@example.test', password: 'password' });
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { message: 'Registration successful! Please log in.' } });
  });
  const failures = [
    { status: 400, body: { error: 'Username is already taken' }, message: 'Username is already taken' },
    { status: 400, body: { success: false, data: null, error: 'old', message: 'old', code: 'email_taken', traceId: 'opaque' }, message: 'Email is already registered' },
    { status: 500, body: { error: 'SECRET' }, message: INTERNAL_ERROR },
    { status: 400, body: { status: 400, title: 'Validation', errors: { Email: ['The Email field is not a valid e-mail address.'] } }, message: 'The Email field is not a valid e-mail address.' }
  ];
  for (const test of failures) {
    it(`renders compatible registration errors safely: ${test.message}`, () => {
      authService.register.and.returnValue(throwError(() => new HttpErrorResponse({ status: test.status, error: test.body })));
      fillForm();
      component.onSubmit();
      fixture.detectChanges();
      expect(component.errorMessage).toBe(test.message);
      expect(fixture.nativeElement.textContent).toContain(test.message);
      expect(fixture.nativeElement.textContent).not.toContain('SECRET');
      expect(router.navigate).not.toHaveBeenCalled();
      expect(component.isLoading).toBeFalse();
    });
  }
  it('does not navigate after a malformed registration response', () => {
    authService.register.and.returnValue(throwError(() => new ApiContractError()));
    fillForm();
    component.onSubmit();
    expect(component.errorMessage).toBe('Unable to complete registration. Please try again.');
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
