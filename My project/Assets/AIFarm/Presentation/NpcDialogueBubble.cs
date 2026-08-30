using AIFarm.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AIFarm.Presentation
{
    [DisallowMultipleComponent]
    public sealed class NpcDialogueBubble : MonoBehaviour
    {
        [SerializeField]
        private ReplanController replanController;

        [SerializeField]
        private GameObject bubbleRoot;

        [SerializeField]
        private Text dialogueText;

        [SerializeField]
        private Text emojiText;

        [SerializeField]
        private Text moodText;

        [SerializeField]
        private Transform billboardTransform;

        public ActionResult Configure(
            ReplanController controller,
            GameObject root,
            Text dialogue,
            Text emoji,
            Text mood,
            Transform billboard)
        {
            if (controller == null || root == null || dialogue == null || emoji == null ||
                mood == null || billboard == null)
            {
                return ActionResult.Failure(
                    ActionFailureReason.InvalidArgument,
                    "NPC dialogue bubble requires its controller and all UI references.");
            }

            replanController = controller;
            bubbleRoot = root;
            dialogueText = dialogue;
            emojiText = emoji;
            moodText = mood;
            billboardTransform = billboard;
            Refresh();
            return ActionResult.Success("NPC dialogue bubble configured.");
        }

        public void Refresh()
        {
            if (replanController == null || bubbleRoot == null || dialogueText == null ||
                emojiText == null || moodText == null)
            {
                return;
            }

            bubbleRoot.SetActive(true);
            dialogueText.text = replanController.NpcExpression;
            emojiText.text = replanController.CurrentEmoji;
            moodText.text = replanController.CurrentMood.ToString();
        }

        private void Update()
        {
            Refresh();
        }

        private void LateUpdate()
        {
            Camera mainCamera = Camera.main;
            if (billboardTransform == null || mainCamera == null)
            {
                return;
            }

            billboardTransform.LookAt(
                billboardTransform.position + mainCamera.transform.rotation * Vector3.forward,
                mainCamera.transform.rotation * Vector3.up);
        }
    }
}
