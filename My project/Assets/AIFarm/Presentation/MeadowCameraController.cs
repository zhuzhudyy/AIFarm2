using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    /// <summary>
    /// Explore with WASD or arrows, right drag to orbit, middle drag to pan,
    /// scroll to zoom, and Home to restore the starting view. UI input keeps focus.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class MeadowCameraController : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Vector3 focusPoint;
        [SerializeField] private float yaw;
        [SerializeField] private float pitch = 48f;
        [SerializeField] private float distance = 40f;
        [SerializeField] private float viewSize = 15f;
        [SerializeField] private bool hasViewState;
        [SerializeField] private Vector3 homeFocus;
        [SerializeField] private float homeYaw;
        [SerializeField] private float homePitch;
        [SerializeField] private float homeDistance;
        [SerializeField] private float homeSize;

        private bool orbiting;
        private bool panning;

        public Vector3 FocusPoint => focusPoint;
        public float ViewSize => viewSize;

        public void Configure(Camera camera, Vector3 focus, float size)
        {
            if (camera == null)
            {
                throw new System.ArgumentNullException(nameof(camera));
            }

            viewCamera = camera;
            focusPoint = ClampFocus(focus);
            yaw = camera.transform.eulerAngles.y;
            pitch = Mathf.Clamp(camera.transform.eulerAngles.x, 25f, 75f);
            distance = Mathf.Max(10f, Vector3.Distance(camera.transform.position, focusPoint));
            viewSize = Mathf.Clamp(size, 7f, 30f);
            hasViewState = true;
            CaptureHome();
            ApplyView();
        }

        public void ApplyView()
        {
            EnsureViewState();
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            viewCamera.transform.SetPositionAndRotation(
                focusPoint - rotation * Vector3.forward * distance,
                rotation);
            viewCamera.orthographic = true;
            viewCamera.orthographicSize = viewSize;
        }

        public void ResetView()
        {
            EnsureViewState();
            focusPoint = homeFocus;
            yaw = homeYaw;
            pitch = homePitch;
            distance = homeDistance;
            viewSize = homeSize;
            ApplyView();
        }

        private void Awake()
        {
            EnsureViewState();
        }

        private void LateUpdate()
        {
            EnsureViewState();
            EventSystem eventSystem = EventSystem.current;
            GameObject selection = eventSystem == null ? null : eventSystem.currentSelectedGameObject;
            bool editingText = selection != null && selection.GetComponentInParent<InputField>() != null;
            bool pointerOverUi = eventSystem != null && eventSystem.IsPointerOverGameObject();
            bool changed = false;
            Keyboard keyboard = Keyboard.current;

            if (!editingText && keyboard != null)
            {
                if (keyboard.homeKey.wasPressedThisFrame)
                {
                    ResetView();
                    return;
                }

                Vector2 direction = new Vector2(
                    (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f));
                if (direction.sqrMagnitude > 0f)
                {
                    direction = Vector2.ClampMagnitude(direction, 1f);
                    Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
                    focusPoint = ClampFocus(focusPoint +
                        heading * new Vector3(direction.x, 0f, direction.y) *
                        (viewSize * 0.9f * UnityEngine.Time.unscaledDeltaTime));
                    changed = true;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    orbiting = !pointerOverUi;
                }

                if (mouse.middleButton.wasPressedThisFrame)
                {
                    panning = !pointerOverUi;
                }

                if (!mouse.rightButton.isPressed)
                {
                    orbiting = false;
                }

                if (!mouse.middleButton.isPressed)
                {
                    panning = false;
                }

                if (!pointerOverUi)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    if (orbiting && delta.sqrMagnitude > 0f)
                    {
                        yaw = Mathf.Repeat(yaw + delta.x * 0.2f, 360f);
                        pitch = Mathf.Clamp(pitch - delta.y * 0.15f, 25f, 75f);
                        changed = true;
                    }
                    else if (panning && delta.sqrMagnitude > 0f)
                    {
                        Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
                        float unitsPerPixel = 2f * viewSize / Mathf.Max(1, viewCamera.pixelHeight);
                        float depthScale = 1f / Mathf.Max(0.3f, Mathf.Sin(pitch * Mathf.Deg2Rad));
                        focusPoint = ClampFocus(focusPoint - heading *
                            new Vector3(delta.x, 0f, delta.y * depthScale) * unitsPerPixel);
                        changed = true;
                    }

                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        viewSize = Mathf.Clamp(viewSize * Mathf.Exp(-scroll * 0.0015f), 7f, 30f);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                ApplyView();
            }
        }

        private void OnDisable()
        {
            orbiting = false;
            panning = false;
        }

        private void EnsureViewState()
        {
            if (viewCamera == null)
            {
                viewCamera = GetComponent<Camera>();
            }

            if (hasViewState)
            {
                return;
            }

            Ray sightLine = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            var ground = new Plane(Vector3.up, Vector3.zero);
            distance = ground.Raycast(sightLine, out float groundDistance) ? groundDistance : 40f;
            focusPoint = sightLine.GetPoint(distance);
            yaw = viewCamera.transform.eulerAngles.y;
            pitch = viewCamera.transform.eulerAngles.x;
            viewSize = viewCamera.orthographicSize;
            hasViewState = true;
            CaptureHome();
        }

        private void CaptureHome()
        {
            homeFocus = focusPoint;
            homeYaw = yaw;
            homePitch = pitch;
            homeDistance = distance;
            homeSize = viewSize;
        }

        private static Vector3 ClampFocus(Vector3 focus)
        {
            return new Vector3(Mathf.Clamp(focus.x, -22f, 22f), 0f, Mathf.Clamp(focus.z, -16f, 20f));
        }
    }
}
