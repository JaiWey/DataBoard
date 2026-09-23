import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MarkdownComponent } from './markdown.component';

describe('MarkdownComponent', () => {
  let fixture: ComponentFixture<MarkdownComponent>;
  const el = () => fixture.nativeElement as HTMLElement;

  const create = (text: string, align?: string, size?: string) => {
    fixture = TestBed.createComponent(MarkdownComponent);
    fixture.componentRef.setInput('text', text);
    if (align) fixture.componentRef.setInput('align', align);
    if (size) fixture.componentRef.setInput('size', size);
    fixture.detectChanges();
  };

  it('draws headings, paragraphs, lists and marks', () => {
    create('# Title\n## Sub\nSome **bold** and *slanted* and `code`.\n\n- one\n- two\n\n1. first\n\n---');

    expect(el().querySelector('h3')?.textContent).toBe('Title');
    expect(el().querySelector('h4')?.textContent).toBe('Sub');
    expect(el().querySelector('p strong')?.textContent).toBe('bold');
    expect(el().querySelector('p em')?.textContent).toBe('slanted');
    expect(el().querySelector('p code')?.textContent).toBe('code');
    expect(Array.from(el().querySelectorAll('ul li')).map((l) => l.textContent?.trim())).toEqual(['one', 'two']);
    expect(el().querySelector('ol li')?.textContent?.trim()).toBe('first');
    expect(el().querySelector('hr')).not.toBeNull();
  });

  it('draws a line break inside a paragraph', () => {
    create('first\nsecond');

    expect(el().querySelectorAll('p br').length).toBe(1);
  });

  it('makes links that open in a new tab and keep the page to themselves', () => {
    create('[docs](https://example.com/docs)');

    const a = el().querySelector('a')!;
    expect(a.getAttribute('href')).toBe('https://example.com/docs');
    expect(a.getAttribute('target')).toBe('_blank');
    expect(a.getAttribute('rel')).toBe('noopener noreferrer');
    expect(a.textContent).toBe('docs');
  });

  it('never makes HTML, a script or a dangerous link out of what was written', () => {
    create('<script>window.hacked = 1</script><img src=x onerror="window.hacked = 1">\n\n[x](javascript:window.hacked=1)\n\n**<b>y</b>**');

    expect(el().querySelector('script, img, b')).toBeNull();
    expect(el().querySelector('a')).toBeNull();
    expect(el().textContent).toContain('<script>');
    expect(el().textContent).toContain('[x](javascript:window.hacked=1)');
    expect((window as unknown as { hacked?: number }).hacked).toBeUndefined();
  });

  it('is aligned and sized by its inputs', () => {
    create('words', 'center', 'large');

    expect(el().classList).toContain('center');
    expect(el().classList).toContain('large');
  });

  it('is left and normal by default, and follows a new text', () => {
    create('one');
    expect(el().classList).not.toContain('center');
    expect(el().textContent).toContain('one');

    fixture.componentRef.setInput('text', 'two');
    fixture.detectChanges();
    expect(el().textContent).toContain('two');
    expect(el().textContent).not.toContain('one');
  });
});
