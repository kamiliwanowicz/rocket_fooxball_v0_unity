using System;
using UnityEditor;
using UnityEngine;

namespace RocketFooxball.Editor
{
    internal static class MovementLabSerializedProperties
    {
        internal static T Require<T>(T value, string label) where T : UnityEngine.Object
        {
            if (value == null) throw new InvalidOperationException("Missing required " + label + ".");
            return value;
        }

        internal static void ValidateReference(UnityEngine.Object target, string propertyName, UnityEngine.Object expected, string label)
        {
            if (target == null || expected == null) throw new InvalidOperationException(label + " reference is null.");
            var property = Find(target, propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != expected)
            {
                throw new InvalidOperationException(label + " reference is broken.");
            }
            ValidatePersistentIdentity(expected, label);
        }

        internal static void ValidateSerializedFloat(UnityEngine.Object target, string propertyName, float expected, string label)
        {
            var property = Find(target, propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Float || Mathf.Abs(property.floatValue - expected) > 0.001f)
            {
                throw new InvalidOperationException(label + " tuning mismatch.");
            }
        }

        internal static void ValidateSerializedInteger(UnityEngine.Object target, string propertyName, int expected, string label)
        {
            var property = Find(target, propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Integer || property.intValue != expected)
            {
                throw new InvalidOperationException(label + " tuning mismatch.");
            }
        }

        internal static void ValidateSerializedVector3(UnityEngine.Object target, string propertyName, Vector3 expected, string label)
        {
            var property = Find(target, propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Vector3 || Vector3.Distance(property.vector3Value, expected) > 0.001f)
            {
                throw new InvalidOperationException(label + " tuning mismatch.");
            }
        }

        internal static void ValidatePrefabReference(UnityEngine.Object target, string propertyName, string prefabPath, string label)
        {
            if (target == null) throw new InvalidOperationException(label + " target is null.");
            var property = Find(target, propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
            {
                throw new InvalidOperationException(label + " reference is null.");
            }
            var component = property.objectReferenceValue as Component;
            var sourcePath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
            if (string.IsNullOrEmpty(sourcePath))
            {
                var source = component != null ? PrefabUtility.GetCorrespondingObjectFromSource(component) : PrefabUtility.GetCorrespondingObjectFromSource(property.objectReferenceValue);
                sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
            }
            if (sourcePath != prefabPath) throw new InvalidOperationException(label + " prefab provenance mismatch: " + sourcePath);
            ValidatePersistentIdentity(property.objectReferenceValue, label);
            if (component != null)
            {
                var sourceComponent = EditorUtility.IsPersistent(component)
                    ? component
                    : PrefabUtility.GetCorrespondingObjectFromSource(component);
                if (sourceComponent == null || !string.Equals(AssetDatabase.GetAssetPath(sourceComponent), prefabPath, StringComparison.Ordinal))
                    throw new InvalidOperationException(label + " prefab component provenance is not persisted: " + prefabPath);
                ValidatePersistentIdentity(sourceComponent, label + " source component");
            }
        }

        internal static T GetSerializablePrefabComponent<T>(GameObject prefabAsset, out GameObject instance) where T : Component
        {
            instance = null;
            if (prefabAsset == null) return null;
            instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (instance != null) instance.hideFlags = HideFlags.HideAndDontSave;
            var instanceComponent = instance != null ? instance.GetComponent<T>() : null;
            return instanceComponent != null ? PrefabUtility.GetCorrespondingObjectFromSource(instanceComponent) : null;
        }

        internal static void ValidateArrayContains(UnityEngine.Object target, string propertyName, UnityEngine.Object first, UnityEngine.Object second, string label)
        {
            var property = Find(target, propertyName);
            if (property == null || !property.isArray || property.arraySize != 2)
            {
                throw new InvalidOperationException(label + " must contain two colliders.");
            }
            var a = property.GetArrayElementAtIndex(0).objectReferenceValue;
            var b = property.GetArrayElementAtIndex(1).objectReferenceValue;
            if (!((a == first && b == second) || (a == second && b == first)))
            {
                throw new InvalidOperationException(label + " does not contain both goal shields.");
            }
            ValidatePersistentIdentity(a, label + "[0]");
            ValidatePersistentIdentity(b, label + "[1]");
        }

        internal static void ValidatePersistentIdentity(UnityEngine.Object value, string label)
        {
            if (value == null) throw new InvalidOperationException(label + " persistent identity is null.");
            if ((value is Component || value is GameObject) && !EditorUtility.IsPersistent(value))
            {
                try
                {
                    var global = GlobalObjectId.GetGlobalObjectIdSlow(value);
                    if (global.identifierType == 0) throw new InvalidOperationException(label + " scene object GlobalObjectId is zero.");
                }
                catch (Exception exception) when (!(exception is InvalidOperationException))
                {
                    throw new InvalidOperationException(label + " scene object GlobalObjectId is unavailable.", exception);
                }
                return;
            }

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out var guid, out long localId) ||
                string.IsNullOrEmpty(guid) || localId == 0)
            {
                throw new InvalidOperationException(label + " asset GUID/local file ID is zero.");
            }
        }

        internal static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException(target.GetType().Name + " has no serialized field '" + propertyName + "'.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetObjectArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray) throw new InvalidOperationException(target.GetType().Name + " has no serialized array '" + propertyName + "'.");
            property.arraySize = values == null ? 0 : values.Length;
            for (var i = 0; values != null && i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetFloat(UnityEngine.Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException(target.GetType().Name + " has no serialized float '" + propertyName + "'.");
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetInteger(UnityEngine.Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Integer) throw new InvalidOperationException(target.GetType().Name + " has no serialized integer '" + propertyName + "'.");
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetVector3(UnityEngine.Object target, string propertyName, Vector3 value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException(target.GetType().Name + " has no serialized Vector3 '" + propertyName + "'.");
            property.vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetEnum(UnityEngine.Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Enum) throw new InvalidOperationException(target.GetType().Name + " has no enum '" + propertyName + "'.");
            var index = Array.IndexOf(property.enumDisplayNames, value);
            if (index < 0) throw new InvalidOperationException("Unknown enum value " + value + " for " + propertyName + ".");
            property.enumValueIndex = index;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty Find(UnityEngine.Object target, string propertyName)
        {
            return new SerializedObject(target).FindProperty(propertyName);
        }
    }
}
