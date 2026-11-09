import { shouldMirrorPreview } from './camera-capture.component';

describe('shouldMirrorPreview', () => {
  it('kamera belakang tampil apa adanya', () => {
    expect(shouldMirrorPreview('environment')).toBe(false);
  });

  it('kamera depan dan webcam tanpa keterangan arah dibidik seperti cermin', () => {
    expect(shouldMirrorPreview('user')).toBe(true);
    expect(shouldMirrorPreview(undefined)).toBe(true);
  });
});
