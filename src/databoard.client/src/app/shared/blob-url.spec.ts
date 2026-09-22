import { Subject, of, throwError } from 'rxjs';
import { BlobUrl } from './blob-url';

describe('BlobUrl', () => {
  let created: string[];
  let revoked: string[];

  beforeEach(() => {
    created = [];
    revoked = [];
    let n = 0;
    vi.spyOn(URL, 'createObjectURL').mockImplementation(() => {
      const url = `blob:file-${++n}`;
      created.push(url);
      return url;
    });
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation((url) => void revoked.push(url));
  });

  afterEach(() => vi.restoreAllMocks());

  it('has no address before anything is fetched, and has not failed', () => {
    const blob = new BlobUrl();

    expect(blob.url()).toBeNull();
    expect(blob.failed()).toBe(false);
  });

  it('gives an address for what it fetched', () => {
    const blob = new BlobUrl();

    blob.fetch(of(new Blob(['x'])));

    expect(blob.url()).toBe('blob:file-1');
    expect(blob.failed()).toBe(false);
  });

  it('lets go of the old address when the next file comes', () => {
    const blob = new BlobUrl();
    blob.fetch(of(new Blob(['a'])));

    blob.fetch(of(new Blob(['b'])));

    expect(blob.url()).toBe('blob:file-2');
    expect(revoked).toEqual(['blob:file-1']);
  });

  it('says it failed, with no address, and clears that when the next fetch starts', () => {
    const blob = new BlobUrl();
    blob.fetch(of(new Blob(['a'])));

    blob.fetch(throwError(() => new Error('gone')));
    expect(blob.url()).toBeNull();
    expect(blob.failed()).toBe(true);
    expect(revoked).toEqual(['blob:file-1']);

    blob.fetch(new Subject<Blob>()); // on its way
    expect(blob.failed()).toBe(false);
  });

  it('drops a fetch that is still on its way when another starts', () => {
    const blob = new BlobUrl();
    const slow = new Subject<Blob>();
    blob.fetch(slow);

    blob.fetch(of(new Blob(['new'])));
    slow.next(new Blob(['old']));

    expect(created).toEqual(['blob:file-1']);
    expect(blob.url()).toBe('blob:file-1');
  });

  it('lets go of everything when disposed of, and ignores a file that arrives later', () => {
    const blob = new BlobUrl();
    const late = new Subject<Blob>();
    blob.fetch(of(new Blob(['a'])));
    blob.fetch(late);

    blob.dispose();
    late.next(new Blob(['b']));

    expect(blob.url()).toBeNull();
    expect(created).toEqual(['blob:file-1']);
  });
});
