using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Rendering;
using UnityEditor.Rendering;

namespace XRP
{
    [CustomEditor(typeof(BasePipelineAsset))]
    public class BasePipelineAssetEditor : Editor
    {
        SerializedProperty shadowDistance;
        SerializedProperty shadowCascades;
        SerializedProperty twoCascadesSplit;
        SerializedProperty fourCascadesSplit;

        void OnEnable ()
        {
            shadowDistance = serializedObject.FindProperty("shadowDistance");
            shadowCascades = serializedObject.FindProperty("shadowCascades");
            twoCascadesSplit = serializedObject.FindProperty("twoCascadesSplit");
            fourCascadesSplit = serializedObject.FindProperty("fourCascadesSplit");
        }

        public override void OnInspectorGUI ()
        {
            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
