using UnityEngine;

[DisallowMultipleComponent]
public class DownedBodyRagdoll : MonoBehaviour
{
    [Header("Optional Root")]
    [Tooltip("Optional. If assigned, only ragdoll rigidbodies under this transform are controlled. Usually use mixamorig:Hips.")]
    [SerializeField]
    private Transform ragdollRoot;

    [Header("Startup")]
    [SerializeField]
    private bool forceDisabledOnAwake = true;

    [Header("Activation")]
    [SerializeField]
    private bool useGravityWhenActive = true;

    [SerializeField]
    private CollisionDetectionMode activeCollisionDetectionMode = CollisionDetectionMode.Continuous;

    [SerializeField]
    private CollisionDetectionMode inactiveCollisionDetectionMode = CollisionDetectionMode.Discrete;

    private Rigidbody[] ragdollRigidbodies;
    private Collider[] ragdollColliders;

    public bool IsRagdollActive { get; private set; }

    private void Reset()
    {
        AutoFindRagdollRoot();
        CacheRagdollParts();
    }

    private void Awake()
    {
        AutoFindRagdollRoot();
        CacheRagdollParts();

        if (forceDisabledOnAwake)
            SetRagdollActive(false);
    }

    private void AutoFindRagdollRoot()
    {
        if (ragdollRoot != null)
            return;

        Transform hips = FindChildRecursive(transform, "mixamorig:Hips");
        if (hips != null)
            ragdollRoot = hips;
    }

    private void CacheRagdollParts()
    {
        Transform searchRoot = ragdollRoot != null ? ragdollRoot : transform;

        ragdollRigidbodies = searchRoot.GetComponentsInChildren<Rigidbody>(true);

        // Only collect colliders that belong to ragdoll rigidbody objects.
        // This avoids accidentally disabling non-ragdoll helper colliders later.
        System.Collections.Generic.List<Collider> colliders = new System.Collections.Generic.List<Collider>();

        for (int i = 0; i < ragdollRigidbodies.Length; i++)
        {
            Rigidbody rb = ragdollRigidbodies[i];

            if (rb == null)
                continue;

            Collider[] rbColliders = rb.GetComponents<Collider>();

            for (int c = 0; c < rbColliders.Length; c++)
            {
                if (rbColliders[c] != null && !colliders.Contains(rbColliders[c]))
                    colliders.Add(rbColliders[c]);
            }
        }

        ragdollColliders = colliders.ToArray();
    }

    public void SetRagdollActive(bool active)
    {
        CacheIfNeeded();

        IsRagdollActive = active;

        for (int i = 0; i < ragdollRigidbodies.Length; i++)
        {
            Rigidbody rb = ragdollRigidbodies[i];

            if (rb == null)
                continue;

            rb.isKinematic = !active;
            rb.useGravity = active && useGravityWhenActive;
            rb.detectCollisions = active;
            rb.collisionDetectionMode = active
                ? activeCollisionDetectionMode
                : inactiveCollisionDetectionMode;

            if (!active)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        for (int i = 0; i < ragdollColliders.Length; i++)
        {
            Collider col = ragdollColliders[i];

            if (col == null)
                continue;

            col.enabled = active;
        }
    }

    public void ActivateRagdoll(Vector3 force, Vector3 forcePosition, ForceMode forceMode = ForceMode.Impulse)
    {
        SetRagdollActive(true);

        Rigidbody bestRigidbody = GetBestForceTarget(forcePosition);

        if (bestRigidbody == null)
            return;

        bestRigidbody.AddForceAtPosition(force, forcePosition, forceMode);
    }

    public void DeactivateRagdoll()
    {
        SetRagdollActive(false);
    }

    private Rigidbody GetBestForceTarget(Vector3 forcePosition)
    {
        CacheIfNeeded();

        Rigidbody best = null;
        float bestSqrDistance = float.MaxValue;

        for (int i = 0; i < ragdollRigidbodies.Length; i++)
        {
            Rigidbody rb = ragdollRigidbodies[i];

            if (rb == null)
                continue;

            float sqrDistance = (rb.worldCenterOfMass - forcePosition).sqrMagnitude;

            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            best = rb;
        }

        return best;
    }

    private void CacheIfNeeded()
    {
        if (ragdollRigidbodies == null || ragdollColliders == null)
            CacheRagdollParts();
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), childName);

            if (found != null)
                return found;
        }

        return null;
    }
}