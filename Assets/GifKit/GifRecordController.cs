using System.IO;
using UnityEngine;

namespace GifKit
{
    /// Example controller — shows how to wire GifRecorder in your project.
    /// Attach this to the same GameObject as GifRecorder, assign the fields, call StartRecord / StopRecord.
    public class GifRecordController : MonoBehaviour
    {
        [SerializeField] private GifRecorder recorder;

        public string gifName  = "output";
        public string savePath = "";  // defaults to Application.persistentDataPath

        private void Awake()
        {
            recorder.OnEncoded += SaveToDisk;
            Debug.Log($"[GifRecordController] Awake — recorder: {(recorder != null ? "OK" : "MISSING")}");
        }

        private void OnDestroy()
        {
            recorder.OnEncoded -= SaveToDisk;
        }

        [ContextMenu("Start Record")]
        public void StartRecord()
        {
            Debug.Log($"[GifRecordController] StartRecord — gif: {gifName}, fps: {recorder.Settings.fps}, maxDim: {recorder.Settings.maxDimension}");
            recorder.StartRecording();
        }

        [ContextMenu("Stop Record")]
        public void StopRecord()
        {
            Debug.Log($"[GifRecordController] StopRecord — extraDuration: {recorder.Settings.extraDuration}s");
            recorder.FinishRecording(recorder.Settings.extraDuration);
        }

        private void SaveToDisk(byte[] gifBytes)
        {
            if (gifBytes == null)
            {
                Debug.LogWarning("[GifRecordController] Encode returned null — no frames were captured.");
                return;
            }
            string dir  = string.IsNullOrEmpty(savePath) ? Application.persistentDataPath : savePath;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, gifName + ".gif");
            File.WriteAllBytes(path, gifBytes);
            Debug.Log($"[GifRecordController] Saved {gifBytes.Length / 1024} KB → {path}");
        }
    }
}
