using UnityEngine;

namespace GifKit
{
    [CreateAssetMenu(menuName = "Create GifSettings", fileName = "GifSettings", order = 0)]
    public class GifSettings : ScriptableObject
    {
        [Tooltip("Max pixel dimension (width or height). Lower = smaller file, faster encode.")]
        public int maxDimension = 480;

        [Tooltip("Frames per second to capture.")]
        public int fps = 10;

        [Tooltip("Extra seconds to keep recording after FinishRecording() is called.")]
        public float extraDuration = 1f;
    }
}
