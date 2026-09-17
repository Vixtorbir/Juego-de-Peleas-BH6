using UnityEngine;

/// <summary>
/// Kind of impact a hitspark represents. Each kind swaps the shared material of the flash so the
/// read is instant: warm for a clean hit, cold for a block, hot for a counter hit.
/// </summary>
public enum HitsparkKind
{
    Hit,
    Block,
    CounterHit
}

/// <summary>
/// Fixed pool of impact flashes spawned at the contact point between a hitbox and a hurtbox. Each
/// spark is a small bright core plus a star of thin rays that snaps open and fades over a few
/// frames on unscaled time, so it keeps playing through hitstop while the fighters are frozen.
/// The flash is deliberately compact (under 0.4 m across) so it marks the hit without covering
/// the limbs. Sparks are built once at startup and recycled round-robin; playing one never allocates.
/// </summary>
public class HitsparkPool : MonoBehaviour
{
    private const int PoolSize = 8;
    private const int RayCount = 6;
    private const float Lifetime = 0.14f;
    private const float CoreStartScale = 0.04f;
    private const float CorePeakScale = 0.18f;
    private const float RayLength = 0.3f;
    private const float RayThickness = 0.018f;
    private const float RayAngleOffset = 15f;
    private const float LightPeakIntensity = 2.2f;
    private const float LightRange = 2f;
    private const float CameraFacingOffset = -0.3f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public static HitsparkPool Instance { get; private set; }

    [SerializeField] private Material hitMaterial;
    [SerializeField] private Material blockMaterial;
    [SerializeField] private Material counterHitMaterial;

    private struct Spark
    {
        public Transform Root;
        public Transform Core;
        public Transform[] Rays;
        public MeshRenderer[] Renderers;
        public Light Flash;
        public float Age;
        public bool Active;
    }

    private Spark[] sparks;
    private int nextSpark;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildPool();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;

        for (int i = 0; i < sparks.Length; i++)
        {
            if (!sparks[i].Active) continue;

            sparks[i].Age += deltaTime;
            float progress = sparks[i].Age / Lifetime;

            if (progress >= 1f)
            {
                sparks[i].Active = false;
                sparks[i].Root.gameObject.SetActive(false);
                continue;
            }

            Animate(ref sparks[i], progress);
        }
    }

    public void Play(Vector3 contactPoint, HitsparkKind kind)
    {
        ref Spark spark = ref sparks[nextSpark];
        nextSpark = (nextSpark + 1) % sparks.Length;

        Material material = SelectMaterial(kind);
        for (int i = 0; i < spark.Renderers.Length; i++)
        {
            spark.Renderers[i].sharedMaterial = material;
        }

        spark.Flash.color = material.GetColor(BaseColorId);
        spark.Root.position = contactPoint + new Vector3(0f, 0f, CameraFacingOffset);
        spark.Root.rotation = Quaternion.AngleAxis(Random.Range(0f, 60f), Vector3.forward);
        spark.Age = 0f;
        spark.Active = true;
        spark.Root.gameObject.SetActive(true);
        Animate(ref spark, 0f);
    }

    private Material SelectMaterial(HitsparkKind kind)
    {
        switch (kind)
        {
            case HitsparkKind.Block: return blockMaterial;
            case HitsparkKind.CounterHit: return counterHitMaterial;
            default: return hitMaterial;
        }
    }

    /// <summary>
    /// Core pops to full size in the first third and shrinks out; rays stretch outwards while
    /// thinning so the star reads as a burst rather than a static shape.
    /// </summary>
    private static void Animate(ref Spark spark, float progress)
    {
        float attack = Mathf.Clamp01(progress * 3f);
        float decay = 1f - progress;

        float coreScale = Mathf.Lerp(CoreStartScale, CorePeakScale, EaseOut(attack)) * Mathf.Lerp(0.4f, 1f, decay);
        spark.Core.localScale = new Vector3(coreScale, coreScale, coreScale);

        float length = RayLength * EaseOut(progress);
        float thickness = RayThickness * decay;
        for (int i = 0; i < spark.Rays.Length; i++)
        {
            Transform ray = spark.Rays[i];
            ray.localScale = new Vector3(thickness, length, thickness);
            ray.localPosition = ray.localRotation * new Vector3(0f, length * 0.5f, 0f);
        }

        spark.Flash.intensity = LightPeakIntensity * decay * decay;
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private void BuildPool()
    {
        sparks = new Spark[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            sparks[i] = BuildSpark(i);
        }
    }

    private Spark BuildSpark(int index)
    {
        var root = new GameObject("Hitspark " + index);
        root.transform.SetParent(transform, false);

        var spark = new Spark
        {
            Root = root.transform,
            Rays = new Transform[RayCount],
            Renderers = new MeshRenderer[RayCount + 1]
        };

        GameObject core = CreateShape(PrimitiveType.Sphere, "Core", root.transform);
        spark.Core = core.transform;
        spark.Renderers[0] = core.GetComponent<MeshRenderer>();

        for (int i = 0; i < RayCount; i++)
        {
            GameObject ray = CreateShape(PrimitiveType.Cube, "Ray", root.transform);
            ray.transform.localRotation = Quaternion.AngleAxis(RayAngleOffset + i * (360f / RayCount), Vector3.forward);
            spark.Rays[i] = ray.transform;
            spark.Renderers[i + 1] = ray.GetComponent<MeshRenderer>();
        }

        var lightObject = new GameObject("Flash");
        lightObject.transform.SetParent(root.transform, false);
        spark.Flash = lightObject.AddComponent<Light>();
        spark.Flash.type = LightType.Point;
        spark.Flash.range = LightRange;
        spark.Flash.shadows = LightShadows.None;

        root.SetActive(false);
        return spark;
    }

    private GameObject CreateShape(PrimitiveType type, string name, Transform parent)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = name;
        shape.transform.SetParent(parent, false);
        Destroy(shape.GetComponent<Collider>());

        MeshRenderer renderer = shape.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = hitMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return shape;
    }
}
