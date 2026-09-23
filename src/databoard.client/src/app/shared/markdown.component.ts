import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { parseMarkdown } from './markdown';

/**
 * Draws simple Markdown (see `parseMarkdown`) with the page's own templates: there is no HTML in what is drawn, so the
 * text of a dashboard can never run script. Links open in a new tab.
 */
@Component({
  selector: 'app-markdown',
  imports: [NgTemplateOutlet],
  template: `
    <ng-template #inline let-nodes>
      @for (node of nodes; track $index) {
        @switch (node.t) {
          @case ('text') { <ng-container>{{ node.v }}</ng-container> }
          @case ('br') { <br /> }
          @case ('code') { <code>{{ node.v }}</code> }
          @case ('strong') { <strong><ng-container *ngTemplateOutlet="inline; context: { $implicit: node.c }" /></strong> }
          @case ('em') { <em><ng-container *ngTemplateOutlet="inline; context: { $implicit: node.c }" /></em> }
          @case ('link') {
            <a [href]="node.href" target="_blank" rel="noopener noreferrer"><ng-container *ngTemplateOutlet="inline; context: { $implicit: node.c }" /></a>
          }
        }
      }
    </ng-template>

    @for (block of blocks(); track $index) {
      @switch (block.t) {
        @case ('heading') {
          @switch (block.level) {
            @case (1) { <h3 class="h1"><ng-container *ngTemplateOutlet="inline; context: { $implicit: block.c }" /></h3> }
            @case (2) { <h4 class="h2"><ng-container *ngTemplateOutlet="inline; context: { $implicit: block.c }" /></h4> }
            @default { <h5 class="h3"><ng-container *ngTemplateOutlet="inline; context: { $implicit: block.c }" /></h5> }
          }
        }
        @case ('p') { <p><ng-container *ngTemplateOutlet="inline; context: { $implicit: block.c }" /></p> }
        @case ('ul') {
          <ul>@for (item of block.items; track $index) { <li><ng-container *ngTemplateOutlet="inline; context: { $implicit: item }" /></li> }</ul>
        }
        @case ('ol') {
          <ol>@for (item of block.items; track $index) { <li><ng-container *ngTemplateOutlet="inline; context: { $implicit: item }" /></li> }</ol>
        }
        @case ('hr') { <hr /> }
      }
    }
  `,
  styles: `
    :host { display: block; overflow-wrap: anywhere; }
    :host(.small) { font-size: 0.85rem; }
    :host(.large) { font-size: 1.3rem; }
    :host(.center) { text-align: center; }
    :host(.right) { text-align: right; }
    h3, h4, h5, p, ul, ol { margin: 0 0 0.5em; }
    .h1 { font-size: 1.6em; line-height: 1.2; font-weight: 600; }
    .h2 { font-size: 1.3em; line-height: 1.25; font-weight: 600; }
    .h3 { font-size: 1.1em; line-height: 1.3; font-weight: 600; }
    ul, ol { padding-left: 1.4em; text-align: left; }
    :host(.center) ul, :host(.center) ol, :host(.right) ul, :host(.right) ol { display: inline-block; }
    code { font-family: ui-monospace, monospace; background: var(--mat-sys-surface-container); padding: 0 0.25em; border-radius: 0.25em; }
    hr { border: 0; border-top: 1px solid var(--mat-sys-outline-variant); margin: 0.75em 0; }
    :host > :last-child { margin-bottom: 0; }
  `,
  host: { '[class.small]': "size() === 'small'", '[class.large]': "size() === 'large'", '[class.center]': "align() === 'center'", '[class.right]': "align() === 'right'" },
})
export class MarkdownComponent {
  readonly text = input('');
  readonly align = input<'left' | 'center' | 'right'>('left');
  readonly size = input<'small' | 'normal' | 'large'>('normal');

  protected readonly blocks = computed(() => parseMarkdown(this.text()));
}
