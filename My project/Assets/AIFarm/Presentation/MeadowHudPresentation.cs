using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    /// <summary>Use the details button or F1 to reveal the optional information panels.</summary>
    [DisallowMultipleComponent]
    public sealed class MeadowHudPresentation : MonoBehaviour
    {
        [SerializeField] private GameObject[] detailPanels = Array.Empty<GameObject>();
        [SerializeField] private Button toggleButton;
        [SerializeField] private Text toggleLabel;
        [SerializeField] private bool detailsVisible;

        private bool listenerAttached;

        public bool IsDetailsVisible => detailsVisible;

        public void Configure(GameObject[] details, Button toggle, Text label)
        {
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }

            if (toggle == null)
            {
                throw new ArgumentNullException(nameof(toggle));
            }

            RemoveListener();
            detailPanels = (GameObject[])details.Clone();
            toggleButton = toggle;
            toggleLabel = label;
            SetDetailsVisible(false);
            if (isActiveAndEnabled)
            {
                AddListener();
            }
        }

        public void SetDetailsVisible(bool visible)
        {
            detailsVisible = visible;
            foreach (GameObject panel in detailPanels)
            {
                if (panel != null)
                {
                    panel.SetActive(visible);
                }
            }

            if (toggleLabel != null)
            {
                toggleLabel.text = visible ? "隐藏详情 · F1" : "显示详情 · F1";
            }
        }

        private void Awake()
        {
            SetDetailsVisible(detailsVisible);
        }

        private void OnEnable()
        {
            SetDetailsVisible(detailsVisible);
            AddListener();
        }

        private void OnDisable()
        {
            RemoveListener();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f1Key.wasPressedThisFrame)
            {
                return;
            }

            EventSystem eventSystem = EventSystem.current;
            GameObject selection = eventSystem == null ? null : eventSystem.currentSelectedGameObject;
            if (selection != null && selection.GetComponentInParent<InputField>() != null)
            {
                return;
            }

            ToggleDetails();
        }

        private void ToggleDetails()
        {
            SetDetailsVisible(!detailsVisible);
        }

        private void AddListener()
        {
            if (toggleButton != null && !listenerAttached)
            {
                toggleButton.onClick.AddListener(ToggleDetails);
                listenerAttached = true;
            }
        }

        private void RemoveListener()
        {
            if (toggleButton != null && listenerAttached)
            {
                toggleButton.onClick.RemoveListener(ToggleDetails);
            }

            listenerAttached = false;
        }
    }
}
