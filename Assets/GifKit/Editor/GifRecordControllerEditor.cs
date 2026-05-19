using UnityEditor;
using UnityEngine;

namespace GifKit
{
    [CustomEditor(typeof(GifRecordController))]
    public class GifRecordControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (GifRecordController)target;

            EditorGUILayout.Space();

            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Start Record", GUILayout.Height(30)))
                controller.StartRecord();

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Stop Record", GUILayout.Height(30)))
                controller.StopRecord();

            GUI.backgroundColor = Color.white;
        }
    }
}
