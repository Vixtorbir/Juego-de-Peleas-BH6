using UnityEngine;

/// <summary>
/// Scene-view drawing helpers shared by combat volumes so hit, hurt and push boxes
/// render consistently regardless of the underlying collider type.
/// </summary>
public static class CombatGizmos
{
    private const int CapsuleRingSegments = 16;

    public static void DrawCollider(Collider collider, Color fillColor)
    {
        Color wireColor = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);

        switch (collider)
        {
            case BoxCollider box:
                DrawBox(box, fillColor, wireColor);
                break;
            case SphereCollider sphere:
                DrawSphere(sphere, fillColor, wireColor);
                break;
            case CapsuleCollider capsule:
                DrawCapsule(capsule.transform, capsule.center, capsule.radius, capsule.height, fillColor, wireColor);
                break;
            case CharacterController controller:
                DrawCapsule(controller.transform, controller.center, controller.radius, controller.height, fillColor, wireColor);
                break;
            default:
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
                break;
        }
    }

    private static void DrawBox(BoxCollider box, Color fillColor, Color wireColor)
    {
        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.color = fillColor;
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = wireColor;
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = Matrix4x4.identity;
    }

    private static void DrawSphere(SphereCollider sphere, Color fillColor, Color wireColor)
    {
        Vector3 center = sphere.transform.TransformPoint(sphere.center);
        float radius = sphere.radius * MaxAbsScale(sphere.transform.lossyScale);

        Gizmos.color = fillColor;
        Gizmos.DrawSphere(center, radius);
        Gizmos.color = wireColor;
        Gizmos.DrawWireSphere(center, radius);
    }

    /// <summary>
    /// Approximates a vertical capsule with two hemispheres and connecting edges, since
    /// Gizmos has no native capsule primitive. Assumes uniform scale on the owner transform.
    /// </summary>
    private static void DrawCapsule(Transform owner, Vector3 localCenter, float radius, float height, Color fillColor, Color wireColor)
    {
        float scale = MaxAbsScale(owner.lossyScale);
        float worldRadius = radius * scale;
        float halfSegment = Mathf.Max(0f, height * scale * 0.5f - worldRadius);

        Vector3 center = owner.TransformPoint(localCenter);
        Vector3 up = owner.up;
        Vector3 top = center + up * halfSegment;
        Vector3 bottom = center - up * halfSegment;

        Gizmos.color = fillColor;
        Gizmos.DrawSphere(top, worldRadius);
        Gizmos.DrawSphere(bottom, worldRadius);

        Gizmos.color = wireColor;
        Gizmos.DrawWireSphere(top, worldRadius);
        Gizmos.DrawWireSphere(bottom, worldRadius);

        Vector3 right = owner.right * worldRadius;
        Vector3 forward = owner.forward * worldRadius;
        Gizmos.DrawLine(top + right, bottom + right);
        Gizmos.DrawLine(top - right, bottom - right);
        Gizmos.DrawLine(top + forward, bottom + forward);
        Gizmos.DrawLine(top - forward, bottom - forward);

        DrawRing(center, owner.right, owner.forward, worldRadius);
    }

    private static void DrawRing(Vector3 center, Vector3 axisA, Vector3 axisB, float radius)
    {
        float step = Mathf.PI * 2f / CapsuleRingSegments;
        Vector3 previous = center + axisA * radius;

        for (int i = 1; i <= CapsuleRingSegments; i++)
        {
            float angle = step * i;
            Vector3 next = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private static float MaxAbsScale(Vector3 scale)
    {
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }
}
