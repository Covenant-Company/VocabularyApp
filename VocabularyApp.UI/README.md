# VocabularyAppUI

This project was generated with [Angular CLI](https://github.com/angular/angular-cli) version 18.1.1.

## Development server

Run `ng serve` for a dev server. Navigate to `http://localhost:4200/`. The application will automatically reload if you change any of the source files.

## Code scaffolding

Run `ng generate component component-name` to generate a new component. You can also use `ng generate directive|pipe|service|class|guard|interface|enum|module`.

## Build

Run `ng build` to build the project. The build artifacts will be stored in the `dist/` directory.

## Deploy To SmarterASP.NET (IIS)

1. Build for production:

```bash
npm install
npm run build
```

2. Upload everything from:

```text
dist/vocabulary-app.ui/browser
```

to your SmarterASP site root (`wwwroot`) by FTP or File Manager.

3. Ensure `web.config` is included in the uploaded files. This file enables Angular client-side routing on IIS.

4. Production API URL is configured as `/api` in `src/environments/environment.prod.ts`, so the UI calls the same domain API.

5. If your app is deployed in a subfolder instead of site root, build with base href:

```bash
ng build --configuration production --base-href /your-subfolder/
```

## Running unit tests

Run `ng test` to execute the unit tests via [Karma](https://karma-runner.github.io).

## Running end-to-end tests

Run `ng e2e` to execute the end-to-end tests via a platform of your choice. To use this command, you need to first add a package that implements end-to-end testing capabilities.

## Further help

To get more help on the Angular CLI use `ng help` or go check out the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
