using UnityEditor;
using UnityEngine;

/// <summary>
/// Owns the physics layers the combat objects live on and the collision matrix between them.
/// Fighter bodies (the CharacterController capsule doubles as the hurt volume) collide with the
/// stage and with each other; melee hitboxes only ever test against fighter bodies; projectiles
/// only test against fighter bodies and the boundary walls. Every other pairing is switched off
/// so PhysX never builds broadphase pairs for it. A Hurtbox layer is kept for dedicated hurt
/// volumes should a character ever need limb-level hurtboxes.
/// </summary>
public static class CombatLayers
{
    public const string Default = "Default";
    public const string Ground = "Ground";
    public const string Wall = "Wall";
    public const string Fighter = "Fighter";
    public const string Hitbox = "Hitbox";
    public const string Hurtbox = "Hurtbox";
    public const string Projectile = "Projectile";

    private const string TagManagerPath = "ProjectSettings/TagManager.asset";
    private const string LayersProperty = "layers";
    private const int FirstUserLayer = 8;
    private const int LayerCount = 32;

    private static readonly string[] RequiredLayers = { Ground, Wall, Fighter, Hitbox, Hurtbox, Projectile };

    private readonly struct CollisionRule
    {
        public readonly string Layer;
        public readonly string[] CollidesWith;

        public CollisionRule(string layer, params string[] collidesWith)
        {
            Layer = layer;
            CollidesWith = collidesWith;
        }
    }

    private static readonly CollisionRule[] CollisionRules =
    {
        new CollisionRule(Default, Default, Ground, Wall, Fighter),
        new CollisionRule(Ground, Default, Fighter),
        new CollisionRule(Wall, Default, Fighter, Projectile),
        new CollisionRule(Fighter, Default, Ground, Wall, Fighter, Hitbox, Projectile),
        new CollisionRule(Hitbox, Fighter, Hurtbox),
        new CollisionRule(Hurtbox, Hitbox, Projectile),
        new CollisionRule(Projectile, Fighter, Hurtbox, Wall)
    };

    public static int FighterLayer => LayerMask.NameToLayer(Fighter);
    public static int HitboxLayer => LayerMask.NameToLayer(Hitbox);
    public static int ProjectileLayer => LayerMask.NameToLayer(Projectile);
    public static int WallLayer => LayerMask.NameToLayer(Wall);
    public static int GroundLayer => LayerMask.NameToLayer(Ground);

    /// <summary>
    /// Adds any missing combat layer to the first free user slot. Existing layers are never
    /// renamed or moved, so scene objects already assigned keep their layer.
    /// </summary>
    public static void EnsureLayers()
    {
        Object[] settings = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
        if (settings.Length == 0)
        {
            Debug.LogError("[CombatLayers] TagManager asset not found; layers were not created.");
            return;
        }

        var tagManager = new SerializedObject(settings[0]);
        SerializedProperty layers = tagManager.FindProperty(LayersProperty);
        bool changed = false;

        foreach (string name in RequiredLayers)
        {
            if (FindLayer(layers, name) >= 0) continue;

            int slot = FindFreeSlot(layers);
            if (slot < 0)
            {
                Debug.LogError($"[CombatLayers] No free layer slot for '{name}'.");
                continue;
            }

            layers.GetArrayElementAtIndex(slot).stringValue = name;
            changed = true;
        }

        if (changed) tagManager.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Rewrites every row of the collision matrix owned by a combat layer. Pairs between layers
    /// this class does not manage are left untouched.
    /// </summary>
    public static void ConfigureCollisionMatrix()
    {
        foreach (CollisionRule rule in CollisionRules)
        {
            int layer = LayerMask.NameToLayer(rule.Layer);
            if (layer < 0) continue;

            for (int other = 0; other < LayerCount; other++)
            {
                string otherName = LayerMask.LayerToName(other);
                bool collides = otherName.Length > 0 && System.Array.IndexOf(rule.CollidesWith, otherName) >= 0;
                Physics.IgnoreLayerCollision(layer, other, !collides);
            }
        }
    }

    public static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static int FindLayer(SerializedProperty layers, string name)
    {
        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;
        }

        return -1;
    }

    private static int FindFreeSlot(SerializedProperty layers)
    {
        for (int i = FirstUserLayer; i < layers.arraySize; i++)
        {
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) return i;
        }

        return -1;
    }
}
