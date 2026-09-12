using System.Reflection;
using AIFarm.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace AIFarm.Tests.EditMode
{
    public sealed class MeadowPresentationTests
    {
        [Test]
        public void CameraInitialization_PreservesExistingScenePose()
        {
            var cameraObject = new GameObject("MeadowCameraTest");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 16f;
                Vector3 position = new Vector3(16f, 20f, -22f);
                Quaternion rotation = Quaternion.LookRotation(new Vector3(1f, 0f, 2f) - position);
                cameraObject.transform.SetPositionAndRotation(position, rotation);
                MeadowCameraController controller = cameraObject.AddComponent<MeadowCameraController>();
                InvokeLifecycle(controller, "Awake");

                Assert.That(Vector3.Distance(cameraObject.transform.position, position), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(cameraObject.transform.rotation, rotation), Is.LessThan(0.001f));
                Assert.That(camera.orthographicSize, Is.EqualTo(16f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CameraReset_RestoresConfiguredStartingView()
        {
            var cameraObject = new GameObject("MeadowCameraResetTest");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                cameraObject.transform.SetPositionAndRotation(
                    new Vector3(18f, 22f, -26f), Quaternion.Euler(44f, -32f, 0f));
                MeadowCameraController controller = cameraObject.AddComponent<MeadowCameraController>();
                controller.Configure(camera, new Vector3(2f, 0f, 4f), 18f);
                Vector3 startingPosition = cameraObject.transform.position;
                Quaternion startingRotation = cameraObject.transform.rotation;
                cameraObject.transform.SetPositionAndRotation(Vector3.one, Quaternion.identity);
                camera.orthographicSize = 8f;

                controller.ResetView();

                Assert.That(Vector3.Distance(cameraObject.transform.position, startingPosition), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(cameraObject.transform.rotation, startingRotation), Is.LessThan(0.001f));
                Assert.That(camera.orthographicSize, Is.EqualTo(18f));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void ModelAnimation_DisableRestoresRestPoseWithoutMovingOwner()
        {
            var owner = new GameObject("ResidentOwnerTest");
            try
            {
                owner.transform.position = new Vector3(4f, 0f, -3f);
                var model = new GameObject("ImportedModel");
                model.transform.SetParent(owner.transform, false);
                model.transform.localPosition = new Vector3(0f, -1f, 0f);
                model.transform.localScale = Vector3.one * 0.8f;
                var limb = new GameObject("LeftArm");
                limb.transform.SetParent(model.transform, false);
                Quaternion restRotation = Quaternion.Euler(7f, 12f, -18f);
                limb.transform.localRotation = restRotation;
                ResidentModelAnimation animation = model.AddComponent<ResidentModelAnimation>();
                InvokeLifecycle(animation, "Awake");
                InvokeLifecycle(animation, "LateUpdate");
                limb.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);

                InvokeLifecycle(animation, "OnDisable");

                Assert.That(owner.transform.position, Is.EqualTo(new Vector3(4f, 0f, -3f)));
                Assert.That(model.transform.localPosition, Is.EqualTo(new Vector3(0f, -1f, 0f)));
                Assert.That(model.transform.localScale, Is.EqualTo(Vector3.one * 0.8f));
                Assert.That(Quaternion.Angle(limb.transform.localRotation, restRotation), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static void InvokeLifecycle(object component, string methodName)
        {
            MethodInfo method = component.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(component, null);
        }
    }
}
