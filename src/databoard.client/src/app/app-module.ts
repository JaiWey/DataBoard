import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { NgModule, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';
import { BrowserModule } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';
import { AppRoutingModule } from './app-routing-module';
import { App } from './app';
import { authInterceptor } from './auth/auth.interceptor';
import { AuthService } from './auth/auth.service';

@NgModule({
  imports: [
    BrowserModule,
    AppRoutingModule,
    App
  ],
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: MAT_FORM_FIELD_DEFAULT_OPTIONS, useValue: { appearance: 'outline' } },
    provideHttpClient(withInterceptors([authInterceptor])),
    // Restore the session on page load from the HttpOnly refresh cookie (resolves to null when signed out).
    provideAppInitializer(() => firstValueFrom(inject(AuthService).refresh())),
  ],
  bootstrap: [App]
})
export class AppModule { }
