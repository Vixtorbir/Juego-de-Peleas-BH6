using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates the impact-flash materials (unlit HDR colours so the bloom pass catches them) and
/// installs the <see cref="HitsparkPool"/> on the scene's managers object with those materials wired.
/// </summary>
public static class CombatVfxSceneSetup
{
    private const string MaterialsRoot = "Assets/_Project/Art/Materials";
    private const string UnlitShaderName = "Universal Render Pipeline/Unlit";
    private const string ManagersObjectName = "Managers";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static readonly Color HitColor = new Color(1f, 0.82f, 0.45f) * 5f;
    private static readonly Color BlockColor = new Color(0.45f, 0.85f, 1f) * 4f;
    private static readonly Color CounterHitColor = new Color(1f, 0.35f, 0.2f) * 6f;

    [MenuItem("Fighting Game/Setup Combat VFX", priority = 45)]
    public static void SetupCombatVfx()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != FighterSceneSetup.ScenePath)
            scene = EditorSceneManager.OpenScene(FighterSceneSetup.ScenePath, OpenSceneMode.Single);

        Material hit = CreateFlashMaterial("M_HitsparkHit", HitColor);
        Material block = CreateFlashMaterial("M_HitsparkBlock", BlockColor);
        Material counter = CreateFlashMaterial("M_HitsparkCounter", CounterHitColor);

        HitsparkPool pool = Object.FindFirstObjectByType<HitsparkPool>();
        if (pool == null)
        {
            GameObject host = GameObject.Find(ManagersObjectName) ?? new GameObject(ManagersObjectName);
            pool = host.AddComponent<HitsparkPool>();
        }

        var serialized = new SerializedObject(pool);
        serialized.FindProperty("hitMaterial").objectReferenceValue = hit;
        serialized.FindProperty("blockMaterial").objectReferenceValue = block;
        serialized.FindProperty("counterHitMaterial").objectReferenceValue = counter;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[CombatVfxSceneSetup] Hitspark pool installed with hit, block and counter-hit materials.");
    }

    private static Material CreateFlashMaterial(string name, Color hdrColor)
    {
        string path = $"{MaterialsRoot}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(UnlitShaderName);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.SetColor(BaseColorId, hdrColor);
        EditorUtility.SetDirty(material);
        return material;
    }
}
