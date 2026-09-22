using UnityEngine;

public enum HitsparkKind
{
    Hit,
    Block,
    CounterHit,
    Deflect
}

public class HitsparkPool : MonoBehaviour
{
    private const int PoolSize = 8;
    private const float Lifetime = 0.14f;
    private const float CameraFacingOffset = -0.3f;

    public static HitsparkPool Instance { get; private set; }

    [SerializeField] private Hitspark sparkPrefab;
    [SerializeField] private Material hitMaterial;
    [SerializeField] private Material blockMaterial;
    [SerializeField] private Material counterHitMaterial;
    [SerializeField] private Material deflectMaterial;

    private Hitspark[] sparks;
    private Transform[] sparkTransforms;
    private float[] ages;
    private int nextSpark;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        sparks = new Hitspark[PoolSize];
        sparkTransforms = new Transform[PoolSize];
        ages = new float[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            sparks[i] = Instantiate(sparkPrefab, transform);
            sparkTransforms[i] = sparks[i].transform;
            sparks[i].gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Unscaled time so the sparks keep playing while both fighters are frozen in hitstop.
    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;

        for (int i = 0; i < sparks.Length; i++)
        {
            if (!sparks[i].gameObject.activeSelf) continue;

            ages[i] += deltaTime;
            float progress = ages[i] / Lifetime;

            if (progress >= 1f)
            {
                sparks[i].gameObject.SetActive(false);
                continue;
            }

            sparks[i].Animate(progress);
        }
    }

    public void Play(Vector3 contactPoint, HitsparkKind kind)
    {
        Hitspark spark = sparks[nextSpark];
        Transform sparkTransform = sparkTransforms[nextSpark];
        ages[nextSpark] = 0f;
        nextSpark = (nextSpark + 1) % sparks.Length;

        spark.SetMaterial(SelectMaterial(kind));
        sparkTransform.SetPositionAndRotation(
            contactPoint + new Vector3(0f, 0f, CameraFacingOffset),
            Quaternion.AngleAxis(Random.Range(0f, 60f), Vector3.forward));
        spark.gameObject.SetActive(true);
        spark.Animate(0f);
    }

    private Material SelectMaterial(HitsparkKind kind)
    {
        switch (kind)
        {
            case HitsparkKind.Block: return blockMaterial;
            case HitsparkKind.CounterHit: return counterHitMaterial;
            case HitsparkKind.Deflect: return deflectMaterial != null ? deflectMaterial : blockMaterial;
            default: return hitMaterial;
        }
    }
}
