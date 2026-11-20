using System;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace BlueReaper.Network.Editor
{
    internal static class NetworkPrefabAuthoring
    {
        internal const string ListPath = "Assets/DefaultNetworkPrefabs.asset";

        internal static NetworkPrefabsList LoadList() => AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ListPath);

        internal static NetworkPrefabsList RequireEditableList()
        {
            var list = LoadList();
            if (!list || !AssetDatabase.IsOpenForEdit(list))
                throw new IOException("쓰기 가능한 기본 네트워크 프리팹 목록이 필요합니다.");
            return list;
        }

        internal static void EnsureMotionComponents(GameObject root)
        {
            foreach (var childObject in root.GetComponentsInChildren<NetworkObject>(true))
                if (childObject.gameObject != root)
                    throw new InvalidOperationException("프리팹의 하위 NetworkObject를 확인하세요. NGO는 네트워크 프리팹에 루트 NetworkObject 하나를 사용합니다.");

            if (!root.GetComponent<NetworkObject>()) root.AddComponent<NetworkObject>();
            if (!root.GetComponent<NetworkTransform>()) root.AddComponent<NetworkTransform>();
        }

        internal static void Register(NetworkPrefabsList list, GameObject prefab)
        {
            // NGO may already have added the imported prefab.
            if (!list.Contains(prefab))
            {
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
            }
            AssetDatabase.SaveAssetIfDirty(list);
        }
    }
}
