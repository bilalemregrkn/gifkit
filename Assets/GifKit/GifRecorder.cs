using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace GifKit
{
    public class GifRecorder : MonoBehaviour
    {
        public GifSettings Settings;

        public bool IsRecording => _recording;

        // Invoked when encode completes. byte[] = gif bytes, null = no frames.
        public event Action<byte[]> OnEncoded;

        private float FrameInterval => 1f / Settings.fps;

        private bool _recording;
        private readonly List<Texture2D> _frames = new();
        private Coroutine _captureCoroutine;

        public void StartRecording()
        {
            if (_captureCoroutine != null) StopCoroutine(_captureCoroutine);
            ClearFrames();
            _recording = true;
            _captureCoroutine = StartCoroutine(CaptureRoutine());
        }

        public void StopRecording()
        {
            _recording = false;
            StopCaptureCoroutine();
        }

        // Stops recording and then captures for extraDuration seconds before encoding.
        public void FinishRecording(float extraDuration)
        {
            _recording = false;
            StartCoroutine(FinalizeAfterDelay(extraDuration));
        }

        public void EncodeNow()
        {
            StopCaptureCoroutine();
            StartCoroutine(EncodeNextFrame());
        }

        private IEnumerator CaptureRoutine()
        {
            while (_recording)
            {
                yield return new WaitForEndOfFrame();
                _frames.Add(ScreenCapture.CaptureScreenshotAsTexture());
                yield return new WaitForSeconds(FrameInterval);
            }
        }

        private IEnumerator FinalizeAfterDelay(float delay)
        {
            StopCaptureCoroutine();
            _recording = true;
            _captureCoroutine = StartCoroutine(CaptureRoutine());
            yield return new WaitForSeconds(delay);
            _recording = false;
            StopCaptureCoroutine();
            yield return new WaitForEndOfFrame();
            Encode();
        }

        private IEnumerator EncodeNextFrame()
        {
            yield return new WaitForEndOfFrame();
            Encode();
        }

        private void Encode()
        {
            if (_frames.Count == 0)
            {
                Debug.LogWarning("[GifRecorder] No frames to encode.");
                OnEncoded?.Invoke(null);
                return;
            }

            int srcW = _frames[0].width;
            int srcH = _frames[0].height;
            CalcOutputSize(srcW, srcH, Settings.maxDimension, out int outW, out int outH);

            // Convert textures to raw bytes on main thread (GetPixels32 requires it),
            // then encode on a background thread so Unity doesn't freeze.
            var rawFrames = new List<byte[]>(_frames.Count);
            foreach (var frame in _frames)
            {
                rawFrames.Add(ToRgb(frame, outW, outH));
                Destroy(frame);
            }
            _frames.Clear();

            Task.Run(() =>
            {
                var encoder = new GifEncoder(outW, outH);
                foreach (var raw in rawFrames)
                    encoder.AddFrame(raw);
                var gifBytes = encoder.Encode();
                UnityEngine.Debug.Log($"[GifRecorder] Encoded {outW}x{outH} — {gifBytes.Length / 1024} KB");
                _pendingResult = gifBytes; // volatile write — Update picks this up next frame
            });
        }

        private volatile byte[] _pendingResult;

        private void Update()
        {
            var result = _pendingResult;
            if (result == null) return;
            _pendingResult = null;
            OnEncoded?.Invoke(result);
        }

        private void StopCaptureCoroutine()
        {
            if (_captureCoroutine == null) return;
            StopCoroutine(_captureCoroutine);
            _captureCoroutine = null;
        }

        private void ClearFrames()
        {
            foreach (var t in _frames) Destroy(t);
            _frames.Clear();
        }

        private static void CalcOutputSize(int srcW, int srcH, int maxDim, out int outW, out int outH)
        {
            if (srcW <= maxDim && srcH <= maxDim) { outW = srcW; outH = srcH; return; }
            if (srcW >= srcH) { outW = maxDim; outH = Mathf.Max(1, Mathf.RoundToInt(maxDim * (float)srcH / srcW)); }
            else              { outH = maxDim; outW = Mathf.Max(1, Mathf.RoundToInt(maxDim * (float)srcW / srcH)); }
        }

        // GetPixels32 is bottom-left origin; GIF is top-left — flip Y while scaling.
        // Channel order is BGR because NeuQuant/LUT expects BGR input.
        private static byte[] ToRgb(Texture2D src, int outW, int outH)
        {
            var pixels = src.GetPixels32();
            int srcW = src.width, srcH = src.height;
            byte[] dst = new byte[outW * outH * 3];
            for (int y = 0; y < outH; y++)
            for (int x = 0; x < outW; x++)
            {
                int sx = x * srcW / outW;
                int sy = (outH - 1 - y) * srcH / outH;
                var c  = pixels[sy * srcW + sx];
                int i  = (y * outW + x) * 3;
                dst[i]     = c.b;
                dst[i + 1] = c.g;
                dst[i + 2] = c.r;
            }
            return dst;
        }
    }
}
