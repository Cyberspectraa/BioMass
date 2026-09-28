#if UNITY_EDITOR
using System.IO;
using BioMass.Runtime.CameraSystem;
using BioMass.Runtime.Debugging;
using BioMass.Runtime.Input;
using BioMass.Runtime.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace BioMass.Editor
{
    public static class BioMassProjectSetup
    {
        private const string ScenePath = "Assets/BioMass/Scenes/MovementLab.unity";
        private const string MaterialFolder = "Assets/BioMass/Materials";

        [MenuItem("bioMass/Build / Rebuild Movement Lab")]
        public static void BuildMovementLab()
        {
            EnsureFolders();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "MovementLab";

            Material environmentMat = CreateOrLoadMaterial("LabEnvironment", new Color(0.16f, 0.18f, 0.20f), 0.55f, 0.05f);
            Material accentMat = CreateOrLoadMaterial("LabAccent", new Color(0.37f, 0.41f, 0.43f), 0.42f, 0.1f);
            Material biomassMat = CreateOrLoadMaterial("DebugBiomass", new Color(0.30f, 0.012f, 0.018f), 0.28f, 0.58f);

            CreateLighting();
            CreateLab(environmentMat, accentMat);
            BioMassController creature = CreateCreature(biomassMat);
            CreateCameraAndDebug(creature);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = creature.gameObject;
            EditorGUIUtility.PingObject(creature.gameObject);
            Debug.Log("bioMass Movement Lab rebuilt. v0.1.3 traversal tuning active: faster Stage 1 movement, compact body and floor/wall/ceiling transitions.");
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory("Assets/BioMass/Scenes");
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
        }

        private static void CreateLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.18f, 0.20f, 0.23f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.09f, 0.10f);
            RenderSettings.ambientGroundColor = new Color(0.035f, 0.035f, 0.04f);

            GameObject lightObject = new GameObject("Key Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        }

        private static void CreateLab(Material environment, Material accent)
        {
            GameObject root = new GameObject("MovementLab_Environment");
            CreateBox(root.transform, "Floor", new Vector3(0f, -0.5f, 8f), new Vector3(24f, 1f, 32f), environment);
            CreateBox(root.transform, "LeftWall", new Vector3(-12f, 5f, 8f), new Vector3(1f, 11f, 32f), environment);
            CreateBox(root.transform, "RightWall", new Vector3(12f, 5f, 8f), new Vector3(1f, 11f, 32f), environment);
            CreateBox(root.transform, "BackWall", new Vector3(0f, 5f, 24f), new Vector3(24f, 11f, 1f), environment);
            CreateBox(root.transform, "CeilingBridge", new Vector3(0f, 9f, 8f), new Vector3(13f, 0.7f, 15f), accent);
            CreateBox(root.transform, "CornerWallA", new Vector3(-4f, 2.5f, 3f), new Vector3(0.7f, 5f, 8f), accent);
            CreateBox(root.transform, "CornerWallB", new Vector3(-0.5f, 2.5f, 6.6f), new Vector3(7f, 5f, 0.7f), accent);
            CreateBox(root.transform, "PillarA", new Vector3(4.2f, 2f, 3f), new Vector3(1.2f, 4f, 1.2f), accent);
            CreateBox(root.transform, "PillarB", new Vector3(6.7f, 3f, 8.5f), new Vector3(1.4f, 6f, 1.4f), accent);
            CreateBox(root.transform, "Slope", new Vector3(1f, 0.75f, 13f), new Vector3(6f, 0.6f, 5f), accent, Quaternion.Euler(-18f, 0f, 0f));
            CreateBox(root.transform, "Vent_Left", new Vector3(-7.2f, 1.6f, 14f), new Vector3(0.5f, 3.2f, 8f), accent);
            CreateBox(root.transform, "Vent_Right", new Vector3(-4.6f, 1.6f, 14f), new Vector3(0.5f, 3.2f, 8f), accent);
            CreateBox(root.transform, "Vent_Top", new Vector3(-5.9f, 3.15f, 14f), new Vector3(3.1f, 0.3f, 8f), accent);

            for (int i = 0; i < 8; i++)
            {
                GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prop.name = $"PhysicsProp_{i:00}";
                prop.transform.SetParent(root.transform);
                prop.transform.position = new Vector3(3f + (i % 4) * 1.15f, 0.35f + (i / 4) * 0.75f, 17f + (i % 2) * 1.1f);
                prop.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
                prop.GetComponent<Renderer>().sharedMaterial = accent;
                Rigidbody rb = prop.AddComponent<Rigidbody>();
                rb.mass = i < 5 ? 1.5f : 10f;
            }
        }

        private static BioMassController CreateCreature(Material material)
        {
            GameObject root = new GameObject("bioMass_Player");
            root.transform.position = new Vector3(0f, 1.25f, -4f);

            BioMassInputReader input = root.AddComponent<BioMassInputReader>();
            root.AddComponent<BioMassSurfaceSensor>();
            BioMassController controller = root.AddComponent<BioMassController>();

            Vector3[] offsets =
            {
                new(0f, 0f, 0f),
                new(0.43f, 0.03f, 0.03f),
                new(-0.43f, 0.03f, 0.03f),
                new(0.10f, 0.33f, 0.25f),
                new(-0.10f, -0.25f, 0.28f),
                new(0.33f, 0.17f, -0.30f),
                new(-0.34f, 0.14f, -0.29f),
                new(0.20f, -0.22f, -0.24f),
                new(-0.22f, -0.20f, -0.22f)
            };

            float[] radii = { 0.52f, 0.40f, 0.41f, 0.38f, 0.43f, 0.39f, 0.44f, 0.37f, 0.40f };

            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject nodeObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nodeObject.name = i == 0 ? "CoreNode" : $"BiomassNode_{i:00}";
                nodeObject.transform.SetParent(root.transform);
                nodeObject.transform.localPosition = offsets[i];

                float radius = radii[i];
                nodeObject.transform.localScale = Vector3.one * radius * 2f;
                nodeObject.GetComponent<Renderer>().sharedMaterial = material;

                Rigidbody body = nodeObject.AddComponent<Rigidbody>();
                BioMassNode node = nodeObject.AddComponent<BioMassNode>();
                node.Configure(i == 0, 0.5f, i == 0 ? 2.1f : 1.1f);
                body.position = nodeObject.transform.position;
            }

            return controller;
        }

        private static void CreateCameraAndDebug(BioMassController creature)
        {
            BioMassInputReader input = creature.GetComponent<BioMassInputReader>();

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.08f;
            camera.fieldOfView = 67f;
            cameraObject.AddComponent<AudioListener>();

            BioMassCameraRig rig = cameraObject.AddComponent<BioMassCameraRig>();
            rig.Configure(creature, input);
            input.SetReferenceCamera(camera);
            cameraObject.transform.position = new Vector3(0f, 5f, -12f);

            GameObject debugObject = new GameObject("DebugHUD");
            BioMassDebugHUD debug = debugObject.AddComponent<BioMassDebugHUD>();
            debug.Configure(creature);
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.transform.rotation = rotation ?? Quaternion.identity;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static Material CreateOrLoadMaterial(string fileName, Color color, float smoothness, float metallic)
        {
            string path = $"{MaterialFolder}/{fileName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            bool srpActive = GraphicsSettings.currentRenderPipeline != null;
            Shader shader = srpActive ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (material == null)
            {
                material = new Material(shader) { name = fileName };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null && material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
#endif
