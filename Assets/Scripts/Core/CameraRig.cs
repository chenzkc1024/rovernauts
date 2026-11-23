using UnityEngine;

namespace Rovernauts
{
    /// <summary>Angled top-down follow camera with screen shake.</summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public Camera Cam { get; private set; }

        static readonly Vector3 Offset = new Vector3(0f, 34f, -22f);
        static readonly Vector3 MenuOffset = new Vector3(6f, 3.75f, -11f);
        Transform target;
        Skybox sky;
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
            var space = Resources.Load<Shader>("DeepSpace");
            if (space != null)
            {
                rig.sky = go.AddComponent<Skybox>();
                rig.sky.material = new Material(space);
            }
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
            if (sky != null) Destroy(sky.material);
        }

        public static void Shake(float amount)
        {
            if (I != null) I.shake = Mathf.Min(1.2f, I.shake + amount);
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (GameManager.I != null && GameManager.I.State == GameState.Menu)
            {
                Cam.clearFlags = sky != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                var sweep = Quaternion.Euler(0f, Mathf.Sin(Time.unscaledTime * 0.16f) * 14f, 0f) * MenuOffset;
                var lookAt = target.position + Vector3.up * 0.6f;
                var rotation = Quaternion.LookRotation(-sweep);
                var offset = rotation * Vector3.right * 3.4f;
                transform.SetPositionAndRotation(lookAt + sweep - offset, rotation);
                return;
            }

            Cam.clearFlags = CameraClearFlags.SolidColor;
            basePosition = Vector3.SmoothDamp(basePosition, target.position + Offset, ref velocity, 0.22f);
            shake = Mathf.Max(0f, shake - Time.unscaledDeltaTime * 2.2f);
            Vector3 jitter = shake > 0f ? Random.insideUnitSphere * shake * shake * 1.5f : Vector3.zero;
            transform.SetPositionAndRotation(basePosition + jitter, Quaternion.LookRotation(-Offset));
        }
    }
}
