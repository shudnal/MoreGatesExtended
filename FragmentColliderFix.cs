using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace MoreGatesExtended
{
    [HarmonyPatch(typeof(Destructible), nameof(Destructible.CreateFragments))]
    internal static class FragmentColliderFix
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            int colliderCall = -1;
            int matches = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) &&
                    code.operand is MethodInfo method && method.DeclaringType == typeof(GameObject) &&
                    method.Name == nameof(GameObject.AddComponent) && method.IsGenericMethod &&
                    method.ReturnType == typeof(BoxCollider) && method.GetParameters().Length == 0)
                {
                    colliderCall = i;
                    matches++;
                }
            }

            if (matches != 1)
            {
                Debug.LogWarning($"{MoreGatesExtended.pluginName}: expected one BoxCollider creation in Destructible.CreateFragments, " +
                    $"found {matches}. The fragment collider fix was not applied.");
                return codes;
            }

            // Replace only the collider factory; preserve the game's fragment selection, rendering and physics.
            CodeInstruction call = codes[colliderCall];
            CodeInstruction loadRoot = new CodeInstruction(OpCodes.Ldarg_0);
            loadRoot.labels.AddRange(call.labels);
            call.labels.Clear();
            foreach (ExceptionBlock block in call.blocks)
                if (block.blockType != ExceptionBlockType.EndExceptionBlock)
                    loadRoot.blocks.Add(block);
            call.blocks.RemoveAll(block => block.blockType != ExceptionBlockType.EndExceptionBlock);
            call.opcode = OpCodes.Call;
            call.operand = AccessTools.Method(typeof(FragmentColliderFix), nameof(AddFragmentCollider));
            codes.Insert(colliderCall, loadRoot);
            return codes;
        }

        private static BoxCollider AddFragmentCollider(GameObject fragment, GameObject sourceRoot)
        {
            Transform fragmentTransform = fragment.transform;
            Vector3 scale = fragmentTransform.localScale;
            if (!(scale.x < 0f || scale.y < 0f || scale.z < 0f) || sourceRoot == null || fragmentTransform.parent != null)
                return fragment.AddComponent<BoxCollider>();

            // WearNTear can pass a child from m_fragmentRoots rather than the piece root itself.
            Piece piece = sourceRoot.GetComponentInParent<Piece>();
            CustomPiece definition = piece == null ? null : PieceManager.Instance.GetPiece(Utils.GetPrefabName(piece.gameObject));
            if (definition?.SourceMod?.GUID != MoreGatesExtended.pluginID)
                return fragment.AddComponent<BoxCollider>();

            MeshFilter meshFilter = fragment.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return fragment.AddComponent<BoxCollider>();

            Vector3 reflection = new Vector3(scale.x < 0f ? -1f : 1f, scale.y < 0f ? -1f : 1f, scale.z < 0f ? -1f : 1f);
            Bounds bounds = meshFilter.sharedMesh.bounds;
            GameObject colliderObject = new GameObject("MoreGatesFragmentCollider");
            colliderObject.layer = fragment.layer;
            Transform colliderTransform = colliderObject.transform;
            colliderTransform.SetParent(fragmentTransform, false);
            colliderTransform.localPosition = Vector3.zero;
            colliderTransform.localRotation = Quaternion.identity;

            // Cancel the parent's reflection before adding the collider, so its effective scale is non-negative.
            // The original mesh, renderer, material overrides and Rigidbody stay on the untouched fragment.
            colliderTransform.localScale = reflection;
            BoxCollider collider = colliderObject.AddComponent<BoxCollider>();
            collider.center = Vector3.Scale(bounds.center, reflection);
            collider.size = bounds.size;
            // This child uses the fragment's Rigidbody and is removed by its existing TimedDestruction.
            return collider;
        }
    }
}
