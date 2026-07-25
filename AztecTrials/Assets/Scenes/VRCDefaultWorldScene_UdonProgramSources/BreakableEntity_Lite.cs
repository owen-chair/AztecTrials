using Miner28.UdonUtils.Network;
using SaccFlightAndVehicles;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class BreakableEntity_Lite : NetworkInterface
{
    [Header("References")]
    [Tooltip("Visible before breaking and disabled when broken.")]
    public GameObject UnbrokenObject;

    [Tooltip("Played once when the object breaks.")]
    public ParticleSystem GibParticles;

    [Tooltip("Played once when the object breaks.")]
    public AudioSource OnBreakAudio;

    [Tooltip("Optional object disabled while intact and enabled when broken.")]
    public GameObject BrokenObject;

    [Tooltip("Trigger used to detect occupied Sacc vehicles.")]
    public Collider TriggerCollider;

    [Header("Health")]
    [Min(0.000001f)]
    public float MaxHP = 100f;

    [System.NonSerialized]
    public float CurrentHP;

    [Header("Networking")]
    [Tooltip("Unique non-negative ID. Use the inspector button to assign one manually.")]
    public int BreakableID = -1;

    [Header("Vehicle Collision")]
    [Tooltip("Multiplier applied to an occupied local vehicle's velocity when it hits this trigger.")]
    [Range(0f, 1f)]
    public float VelocityMultiplier = 0.8f;

    [Tooltip("Optional velocity change pushing the occupied local vehicle away from the trigger contact.")]
    [Min(0f)]
    public float PushbackForce;

    [Header("Particle Damage")]
    [Tooltip("Fallback damage when the particle system has no d:<amount> child marker.")]
    [Min(0f)]
    public float DefaultParticleDamage = 10f;

    private bool _broken;

    private void Start()
    {
        if (BreakableID < 0)
        {
            Debug.LogWarning(
                "[BreakableEntity_Lite] BreakableID is unset on " + gameObject.name +
                ". Use Generate Unique Breakable ID in the inspector.");
        }
    }

    private void OnEnable()
    {
        CurrentHP = Mathf.Max(MaxHP, 0.000001f);
        _broken = false;

        if (UnbrokenObject != null)
        {
            UnbrokenObject.SetActive(true);
        }

        if (BrokenObject != null)
        {
            BrokenObject.SetActive(false);
        }

        if (GibParticles != null)
        {
            GibParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (OnBreakAudio != null)
        {
            OnBreakAudio.Stop();
        }
    }

    private void OnParticleCollision(GameObject other)
    {
        if (!gameObject.activeInHierarchy) { return; }
        if (_broken || other == null) { return; }

        float damage = DefaultParticleDamage;
        byte weaponType = 1;

        foreach (Transform child in other.transform)
        {
            string parameterName = child.name;
            if (parameterName.StartsWith("d:"))
            {
                float parsedDamage;
                if (float.TryParse(parameterName.Substring(2), out parsedDamage))
                {
                    damage = parsedDamage;
                }
            }
            else if (parameterName.StartsWith("t:"))
            {
                byte parsedWeaponType;
                if (byte.TryParse(parameterName.Substring(2), out parsedWeaponType))
                {
                    weaponType = parsedWeaponType;
                }
            }
        }

        if (damage <= 0f) { return; }

        CurrentHP = Mathf.Max(0f, CurrentHP - damage);
        if (CurrentHP <= 0f)
        {
            Break();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!gameObject.activeInHierarchy) { return; }
        if (_broken || other == null) { return; }
        if (TriggerCollider != null && !TriggerCollider.enabled) { return; }

        SaccEntity entity = FindSaccEntity(other.gameObject);
        if (entity == null) { return; }
        if (!entity.Piloting && !entity.Passenger) { return; }

        ApplyVehicleCollisionResponse(entity);
        Break();
    }

    public void Break()
    {
        Break(true);
    }

    public void Break(bool sendNetworkEvent = true)
    {
        if (_broken)
        {
            return;
        }

        _broken = true;
        CurrentHP = 0f;

        if (UnbrokenObject != null)
        {
            UnbrokenObject.SetActive(false);
        }

        if (BrokenObject != null)
        {
            BrokenObject.SetActive(true);
        }

        if (GibParticles != null)
        {
            GibParticles.Play();
        }

        if (OnBreakAudio != null)
        {
            OnBreakAudio.Play();
        }

        if (sendNetworkEvent && BreakableID >= 0)
        {
            SendMethodNetworked(
                nameof(NetBreak),
                SyncTarget.All,
                new DataToken(BreakableID)
            );
        }
    }

    [NetworkedMethod]
    public void NetBreak(int breakableID)
    {
        if (breakableID != BreakableID)
        {
            return;
        }

        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        Break(false);
    }

    public float GetHealth()
    {
        return CurrentHP;
    }

    public bool GetBroken()
    {
        return _broken;
    }

    private void ApplyVehicleCollisionResponse(SaccEntity entity)
    {
        Rigidbody vehicleRigidbody = entity.VehicleRigidbody;
        if (vehicleRigidbody == null)
        {
            vehicleRigidbody = entity.GetComponent<Rigidbody>();
        }

        if (vehicleRigidbody == null || vehicleRigidbody.isKinematic) { return; }

        float multiplier = Mathf.Clamp01(VelocityMultiplier);
        vehicleRigidbody.velocity *= multiplier;

        if (PushbackForce > 0f)
        {
            Vector3 pushDirection = vehicleRigidbody.worldCenterOfMass - transform.position;
            if (pushDirection.sqrMagnitude > 0.000001f)
            {
                vehicleRigidbody.AddForce(
                    pushDirection.normalized * PushbackForce,
                    ForceMode.VelocityChange);
            }
        }
    }

    private static SaccEntity FindSaccEntity(GameObject damagingObject)
    {
        if (damagingObject == null) { return null; }

        GameObject currentObject = damagingObject;
        SaccEntity entity = currentObject.GetComponent<SaccEntity>();
        while (entity == null && currentObject.transform.parent != null)
        {
            currentObject = currentObject.transform.parent.gameObject;
            entity = currentObject.GetComponent<SaccEntity>();
        }

        if (entity != null) { return entity; }

        currentObject = damagingObject;
        UdonBehaviour entityBehaviour =
            (UdonBehaviour)currentObject.GetComponent(typeof(UdonBehaviour));
        while (entityBehaviour == null && currentObject.transform.parent != null)
        {
            currentObject = currentObject.transform.parent.gameObject;
            entityBehaviour =
                (UdonBehaviour)currentObject.GetComponent(typeof(UdonBehaviour));
        }

        if (entityBehaviour == null) { return null; }

        object entityControl = entityBehaviour.GetProgramVariable("EntityControl");
        return entityControl != null ? (SaccEntity)entityControl : null;
    }
}