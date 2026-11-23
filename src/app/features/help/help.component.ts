import { Component } from '@angular/core';

@Component({
  selector: 'app-help',
  template: `
    <div class="container page">
      <div class="row">
        <div class="col-md-10 offset-md-1 col-xs-12">
          <h1>Help</h1>
          <p>Welcome to the Angular RealWorld Example App!</p>
          <p>This is a help page for the application.</p>
        </div>
      </div>
    </div>
  `,
})
export default class HelpComponent {}
