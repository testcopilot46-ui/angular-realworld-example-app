import { inject, Pipe, PipeTransform, SecurityContext } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import DOMPurify from 'dompurify';

@Pipe({
  name: 'markdown',
  standalone: true,
})
export class MarkdownPipe implements PipeTransform {
  domSanitizer = inject(DomSanitizer);
  async transform(content: string): Promise<string> {
    const { marked } = await import('marked');
    const html = await marked.parse(content);
    // Sanitize with DOMPurify first, then with Angular's DomSanitizer
    const sanitized = DOMPurify.sanitize(html);
    return this.domSanitizer.sanitize(SecurityContext.HTML, sanitized) || '';
  }
}
