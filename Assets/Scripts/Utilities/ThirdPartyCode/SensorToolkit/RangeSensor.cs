using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SensorToolkit
{
    /*
     * A sensor that detects colliders within a specified range using Physics.OverlapSphere. Detects colliders or rigid
     * bodies on the chosen physics layers. Can be configured to pulse automatically at fixed intervals or manually. Has
     * optional support for line of sight testing.
     */
    [ExecuteInEditMode]
    public class RangeSensor : BaseVolumeSensor
    {
        // Should the sensor be pulsed automatically at fixed intervals or should it be pulsed manually.
        public enum UpdateMode { FixedInterval, Manual }

        // The radius in world units that the sensor detects colliders in.
        public float SensorRange = 10f;

        // The physics layer mask that the sensor detects colliders on.
        public LayerMask DetectsOnLayers;

        // Automatic or manually pulsing mode.
        public UpdateMode SensorUpdateMode;

        // If the chosen update mode is automatic then this is the interval in seconds between each automatic pulse.
        public float CheckInterval = 1f;

        // Event that is called each time the sensor is pulsed. Used by the editor extensions, you shouldn't need to listen to it.
        public delegate void SensorUpdateHandler();
        public event SensorUpdateHandler OnSensorUpdate;

        // Pulses the sensor to update its list of detected objects
        public override void Pulse()
        {
            if (isActiveAndEnabled) testSensor();
            
        }

        HashSet<GameObject> previousDetectedObjects;

        protected override void OnEnable()
        {
            base.OnEnable();
            previousDetectedObjects = new HashSet<GameObject>();
            if (Application.isPlaying) StartCoroutine(sensorRoutine());
        }

        IEnumerator sensorRoutine()
        {
            while (true)
            {
                if (SensorUpdateMode == UpdateMode.FixedInterval) testSensor();
                if (CheckInterval > 0f)
                    yield return new WaitForSeconds(CheckInterval);
                else
                    yield return null;
            }
        }

        void testSensor()
        {
            clearColliders();
            var sensedColliders = Physics.OverlapSphere(transform.position, SensorRange, DetectsOnLayers);
            for (int i = 0; i < sensedColliders.Length; i++)
            {
                var newDetection = addCollider(sensedColliders[i]);
                if (newDetection != null)
                {
                    if (previousDetectedObjects.Contains(newDetection))
                    {
                        previousDetectedObjects.Remove(newDetection);
                    }
                    else
                    {
                        OnDetected.Invoke(newDetection);
                    }
                }
            }

            // Any entries still in previousDetectedObjects are no longer detected
            var previousDetectedEnumerator = previousDetectedObjects.GetEnumerator();
            while (previousDetectedEnumerator.MoveNext())
            {
                var lostDetection = previousDetectedEnumerator.Current;
                OnLostDetection.Invoke(lostDetection);
            }

            previousDetectedObjects.Clear();
            var detectedEnumerator = DetectedObjects.GetEnumerator();
            while (detectedEnumerator.MoveNext())
            {
                previousDetectedObjects.Add(detectedEnumerator.Current);
            }

            if (OnSensorUpdate != null) OnSensorUpdate();
        }

        void reset()
        {
            clearColliders();
        }

        public override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            if (!isActiveAndEnabled) return;
            Gizmos.color = GizmoColor;
            Gizmos.DrawWireSphere(transform.position, SensorRange);
        }
    }
}