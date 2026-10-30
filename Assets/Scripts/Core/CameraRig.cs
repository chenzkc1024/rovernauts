using UnityEngine;

namespace Rovernauts
{
    /// <summary>Angled top-down follow camera with screen shake.</summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public Camera Cam { get; private set; }

        static readonly Vector3 Offset = new Vector3(0f, 34f, -22f);
        Transform target;
        Vector3 basePosition, velocity;
        float shake;

        public static CameraRig Create(Transform followTarget)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var rig = go.AddComponent<CameraRig>();
            rig.Cam = go.AddComponent<Camera>();
            rig.Cam.clearFlags = CameraClearFlags.SolidColor;
            rig.Cam.backgroundColor = new Color(0.02f, 0.03f, 0.08f);
            rig.Cam.fieldOfView = 50f;
            rig.Cam.farClipPlane = 800f;
            go.AddComponent<AudioListener>();
            rig.target = followTarget;
            rig.basePosition = followTarget.position + Offset;
            go.transform.SetPositionAndRotation(rig.basePosition, Quaternion.LookRotation(-Offset));
            return rig;
        }

        void Awake() => I = this;

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        public static void Shake(float amount)
        {
            if (I != null) I.shake = Mathf.Min(1.2f, I.shake + amount);
        }

        void LateUpdate()
        {
            if (target != null)
                basePosition = Vector3.SmoothDamp(basePosition, target.position + Offset, ref velocity, 0.22f);
            shake = Mathf.Max(0f, shake - Time.unscaledDeltaTime * 2.2f);
            Vector3 jitter = shake > 0f ? Random.insideUnitSphere * shake * shake * 1.5f : Vector3.zero;
            transform.SetPositionAndRotation(basePosition + jitter, Quaternion.LookRotation(-Offset));
        }
    }
}
