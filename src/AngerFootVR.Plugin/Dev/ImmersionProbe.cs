using System.Linq;
using System.Text;
using Framework;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>
/// Dev: what the grip, wound and bleeding plans need to know about the running game. The first-person hand rig's
/// finger bones (the game's own per-gun grip poses), the held gun's meshes (readable? for fingers meeting its real
/// shape) and materials (depth testing: the gun looked see-through over the fingers), the muzzle flashes and ejected
/// casings (where they are, on which layer), and an enemy's skin (its baked mesh's UVs, for wounds painted on it).
/// </summary>
internal static class ImmersionProbe
{
    public static string Run()
    {
        var sb = new StringBuilder("PROBE");
        if (!SingletonBehaviour<Player>.HasInstance) return sb.Append(" no player").ToString();
        var player = SingletonBehaviour<Player>.Instance;
        var rig = player.HandRig;
        sb.Append($"\n casings setting {GameSettings.SpawnBulletCasings}; weapon {(player.Weapon != null ? player.Weapon.name : "none")}");
        if (rig != null)
        {
            var fingers = rig.GetComponentsInChildren<Transform>(true).Where(t => t.name.IndexOf("Finger", System.StringComparison.OrdinalIgnoreCase) >= 0 || t.name.Contains("Wrist") || t.name.Contains("Thumb")).Select(t => t.name.Replace("FPSHands_03_Rig:", "")).ToArray();
            sb.Append($"\n FP RIG {rig.name} scale {rig.transform.lossyScale:F3}: {fingers.Length} hand bones: {string.Join(" ", fingers)}");
            foreach (var r in rig.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.gameObject.activeInHierarchy) continue;
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                sb.Append($"\n  RENDERER {r.name} [{r.GetType().Name}] layer {r.gameObject.layer} enabled {r.enabled} mesh {(m != null ? $"{m.name} readable {m.isReadable} verts {m.vertexCount}" : "-")}");
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    var props = new[] { "_ZTest", "_ZWrite", "_Cull", "_SrcBlend", "_DstBlend" }.Where(mat.HasProperty).Select(p => $"{p}={mat.GetFloat(p)}");
                    sb.Append($"\n    MAT {mat.name} shader '{mat.shader.name}' queue {mat.renderQueue} {string.Join(" ", props)} keywords [{string.Join(",", mat.shaderKeywords)}]");
                }
            }
        }

        // what the last shots left: muzzle flashes (layer 13) and casings, and how far from the gun hand
        var hand = Core.VrHands.GetRaw(Input.GunHold.Hand).Position;
        foreach (var e in Object.FindObjectsOfType<EjectedShell>())
            sb.Append($"\n CASING {e.name} at {e.transform.position:F2} ({(e.transform.position - hand).magnitude:F2} m from the hand) layer {e.gameObject.layer}");
        foreach (var go in Object.FindObjectsOfType<GameObject>().Where(g => g.layer == 13 && g.activeInHierarchy && g.name.IndexOf("Muzzle", System.StringComparison.OrdinalIgnoreCase) >= 0).Take(6))
            sb.Append($"\n FLASH {go.name} at {go.transform.position:F2} ({(go.transform.position - hand).magnitude:F2} m from the hand) layer 13");
        var pc = SingletonBehaviour<PlayerCamera>.HasInstance ? SingletonBehaviour<PlayerCamera>.Instance : null;
        if (pc != null) sb.Append($"\n cameras: main mask {pc.MainCamera.cullingMask:X} enabled {pc.MainCamera.enabled}, weapon mask {pc.WeaponCamera.cullingMask:X} enabled {pc.WeaponCamera.enabled} target {(pc.WeaponCamera.targetTexture != null ? pc.WeaponCamera.targetTexture.name : "screen")}");

        // an enemy's skin
        var enemies = Object.FindObjectsOfType<Enemy>();
        sb.Append($"\n enemies in the scene: {enemies.Length} ({enemies.Count(x => x != null && x.Health != null && x.Health.IsAlive)} alive)");
        var skins = Object.FindObjectsOfType<SkinnedMeshRenderer>().Where(r => r.GetComponentInParent<Player>() == null && r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("CHARACTERS")).Take(3).ToArray();
        foreach (var smr in skins)
        {
            var enemy = smr.transform.root;
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                sb.Append($"\n ENEMY {enemy.name} skin {smr.name} mesh readable {smr.sharedMesh.isReadable} subs {smr.sharedMesh.subMeshCount} baked verts {baked.vertexCount} uv {baked.uv.Length} uv2 {baked.uv2.Length} normals {baked.normals.Length} tangents {baked.tangents.Length} bones {smr.bones.Length}");
                foreach (var mat in smr.sharedMaterials)
                    if (mat != null)
                        sb.Append($"\n    MAT {mat.name} shader '{mat.shader.name}' queue {mat.renderQueue} tex [{string.Join(",", mat.GetTexturePropertyNames().Where(p => mat.GetTexture(p) != null))}]");
                Object.Destroy(baked);
            }
        }

        return sb.ToString();
    }
}
