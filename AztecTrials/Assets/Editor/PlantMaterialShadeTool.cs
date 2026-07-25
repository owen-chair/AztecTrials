using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class PlantMaterialShadeTool : EditorWindow
{
	private const string ShadeSuffix = "_shade";
	private const string UndoName = "Assign Shadow Materials";
	private static readonly string[] TintPropertyNames = { "_Tint", "_Color", "_BaseColor" };
	private const float DefaultShadowThreshold = 0.35f;
	private const float FootprintSampleScale = 0.65f;
	private const float MinimumFootprintSampleOffset = 0.15f;
	private const float RegionalShadowSampleRatio = 0.3f;
	private const float ColumnPrefabWidth = 190f;
	private const float ColumnMaterialWidth = 142f;
	private const float ColumnTintWidth = 44f;
	private const float ColumnStatusWidth = 130f;
	private const int ProgressUpdateCount = 100;

	[SerializeField] private UnityEngine.Object parentHierarchy;
	[SerializeField] private List<GameObject> terrainMeshObjects = new List<GameObject>();
	[SerializeField, Range(0f, 1f)] private float shadowThreshold = DefaultShadowThreshold;
	[SerializeField] private List<PlantTypeEntry> plantTypes = new List<PlantTypeEntry>();
	[SerializeField] private Vector2 tableScroll;

	[MenuItem("Tools/Lighting/Plant Material Shade Tool")]
	public static void Open()
	{
		GetWindow<PlantMaterialShadeTool>("Plant Shade");
	}

	private void OnGUI()
	{
		EditorGUILayout.LabelField(
			"Assigns light or shadow foliage materials by sampling the selected terrain mesh lightmaps.",
			EditorStyles.wordWrappedLabel);

		EditorGUILayout.Space(8f);
		DrawObjectFields();

		EditorGUILayout.Space(6f);
		using (new EditorGUI.DisabledScope(ResolveRootTransform(false) == null))
		{
			if (GUILayout.Button("Discover Plant Types"))
			{
				DiscoverPlantTypes();
			}
		}

		EditorGUILayout.Space(8f);
		DrawPlantTypeTable();

		bool hasMissingMaterials = HasMissingMaterials();
		if (hasMissingMaterials)
		{
			EditorGUILayout.HelpBox(
				"Create the missing material(s) then press Discover Plant Types again.",
				MessageType.Warning);
		}

		EditorGUILayout.Space(8f);
		shadowThreshold = EditorGUILayout.Slider("Shadow Threshold", shadowThreshold, 0f, 1f);

		EditorGUILayout.Space(8f);
		using (new EditorGUI.DisabledScope(!CanProcess()))
		{
			if (GUILayout.Button("Assign Shadow Materials"))
			{
				AssignShadowMaterials();
			}
		}
	}

	private void DrawObjectFields()
	{
		EditorGUI.BeginChangeCheck();
		UnityEngine.Object newParent = EditorGUILayout.ObjectField(
			new GUIContent("Parent Hierarchy", "A Transform or GameObject that contains all placed foliage."),
			parentHierarchy,
			typeof(UnityEngine.Object),
			true);

		if (EditorGUI.EndChangeCheck())
		{
			if (newParent == null || newParent is Transform || newParent is GameObject)
			{
				parentHierarchy = newParent;
			}
			else
			{
				parentHierarchy = null;
				ShowNotification(new GUIContent("Parent must be a Transform or GameObject."));
			}

			plantTypes.Clear();
		}

		DrawTerrainMeshFields();
	}

	private void DrawTerrainMeshFields()
	{
		EditorGUILayout.LabelField("Terrain Meshes", EditorStyles.boldLabel);

		if (terrainMeshObjects == null)
		{
			terrainMeshObjects = new List<GameObject>();
		}

		int removeIndex = -1;
		for (int i = 0; i < terrainMeshObjects.Count; i++)
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUI.BeginChangeCheck();
				terrainMeshObjects[i] = (GameObject)EditorGUILayout.ObjectField(
					new GUIContent($"Terrain Mesh {i + 1}", "GameObject with the MeshFilter and MeshRenderer that owns a baked lightmap."),
					terrainMeshObjects[i],
					typeof(GameObject),
					true);

				if (EditorGUI.EndChangeCheck())
				{
					Repaint();
				}

				if (GUILayout.Button("-", GUILayout.Width(24f)))
				{
					removeIndex = i;
				}
			}
		}

		if (removeIndex >= 0)
		{
			terrainMeshObjects.RemoveAt(removeIndex);
			Repaint();
		}

		if (GUILayout.Button("Add Terrain Mesh"))
		{
			terrainMeshObjects.Add(null);
			Repaint();
		}
	}

	private void DrawPlantTypeTable()
	{
		EditorGUILayout.LabelField("Discovered Plant Types", EditorStyles.boldLabel);

		if (plantTypes == null || plantTypes.Count == 0)
		{
			EditorGUILayout.HelpBox("Press Discover Plant Types to scan the selected parent hierarchy.", MessageType.Info);
			return;
		}

		using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
		{
			GUILayout.Label("Prefab name", EditorStyles.boldLabel, GUILayout.Width(ColumnPrefabWidth));
			GUILayout.Label("Light material", EditorStyles.boldLabel, GUILayout.Width(ColumnMaterialWidth));
			GUILayout.Label("Tint", EditorStyles.boldLabel, GUILayout.Width(ColumnTintWidth));
			GUILayout.Label("Shadow material", EditorStyles.boldLabel, GUILayout.Width(ColumnMaterialWidth));
			GUILayout.Label("Tint", EditorStyles.boldLabel, GUILayout.Width(ColumnTintWidth));
			GUILayout.Label("Status", EditorStyles.boldLabel, GUILayout.Width(ColumnStatusWidth));
		}

		tableScroll = EditorGUILayout.BeginScrollView(tableScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(320f));
		for (int i = 0; i < plantTypes.Count; i++)
		{
			PlantTypeEntry entry = plantTypes[i];
			if (entry == null)
				continue;

			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUILayout.LabelField(entry.PrefabName, GUILayout.Width(ColumnPrefabWidth));
				entry.lightMaterial = DrawMaterialWithTintPicker(entry.lightMaterial);
				entry.shadowMaterial = DrawMaterialWithTintPicker(entry.shadowMaterial);
				EditorGUILayout.LabelField(entry.Status, GUILayout.Width(ColumnStatusWidth));
			}
		}

		EditorGUILayout.EndScrollView();
	}

	private static Material DrawMaterialWithTintPicker(Material material)
	{
		Material newMaterial = (Material)EditorGUILayout.ObjectField(material, typeof(Material), false, GUILayout.Width(ColumnMaterialWidth));
		DrawTintPicker(newMaterial);
		return newMaterial;
	}

	private static void DrawTintPicker(Material material)
	{
		if (material == null || !TryGetTintColor(material, out string propertyName, out Color tint))
		{
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ColorField(GUIContent.none, Color.white, false, true, false, GUILayout.Width(ColumnTintWidth));
			}

			return;
		}

		EditorGUI.BeginChangeCheck();
		Color newTint = EditorGUILayout.ColorField(GUIContent.none, tint, false, true, false, GUILayout.Width(ColumnTintWidth));
		if (EditorGUI.EndChangeCheck())
		{
			Undo.RecordObject(material, "Edit Plant Material Tint");
			material.SetColor(propertyName, newTint);
			EditorUtility.SetDirty(material);
		}
	}

	private static bool TryGetTintColor(Material material, out string propertyName, out Color color)
	{
		propertyName = null;
		color = Color.white;

		if (material == null)
			return false;

		for (int i = 0; i < TintPropertyNames.Length; i++)
		{
			string candidate = TintPropertyNames[i];
			if (!material.HasProperty(candidate))
				continue;

			propertyName = candidate;
			color = material.GetColor(candidate);
			return true;
		}

		return false;
	}

	private void DiscoverPlantTypes()
	{
		Transform root = ResolveRootTransform(true);
		if (root == null)
			return;

		plantTypes.Clear();

		var discoveredByPrefab = new Dictionary<string, PlantTypeEntry>();
		var stack = new Stack<Transform>(1024);
		stack.Push(root);

		while (stack.Count > 0)
		{
			Transform current = stack.Pop();
			if (current == null)
				continue;

			for (int childIndex = current.childCount - 1; childIndex >= 0; childIndex--)
			{
				stack.Push(current.GetChild(childIndex));
			}

			MeshRenderer renderer = current.GetComponent<MeshRenderer>();
			if (renderer == null)
				continue;

			GameObject prefabAsset = GetPrefabAssetRoot(renderer.gameObject);
			if (prefabAsset == null)
				continue;

			string prefabKey = GetPrefabKey(prefabAsset);
			if (!discoveredByPrefab.TryGetValue(prefabKey, out PlantTypeEntry entry))
			{
				entry = new PlantTypeEntry(prefabAsset);
				discoveredByPrefab.Add(prefabKey, entry);
			}

			if (!entry.HasBothMaterials)
			{
				TryPopulateMaterialPair(entry, renderer);
			}
		}

		plantTypes.AddRange(discoveredByPrefab.Values);
		plantTypes.Sort((left, right) => string.Compare(left.PrefabName, right.PrefabName, StringComparison.OrdinalIgnoreCase));

		Debug.Log($"PlantMaterialShadeTool: discovered {plantTypes.Count} unique prefab type(s) under '{root.name}'.");
		Repaint();
	}

	private void AssignShadowMaterials()
	{
		Transform root = ResolveRootTransform(true);
		if (root == null)
			return;

		if (!CanProcess())
			return;

		TerrainLightmapSamplerSet terrainSampler = null;
		int undoGroup = -1;
		bool undoGroupStarted = false;

		try
		{
			if (!TerrainLightmapSamplerSet.TryCreate(terrainMeshObjects, out terrainSampler, out string samplerError))
			{
				EditorUtility.DisplayDialog("Plant Material Shade Tool", samplerError, "OK");
				return;
			}

			Dictionary<Material, PlantTypeEntry> materialLookup = BuildMaterialLookup();
			if (materialLookup.Count == 0)
			{
				EditorUtility.DisplayDialog("Plant Material Shade Tool", "No valid plant materials were discovered.", "OK");
				return;
			}

			var transforms = new List<Transform>(4096);
			GatherTransforms(root, transforms);

			var pendingChanges = new List<RendererMaterialChange>();
			var shadowByPlantRoot = new Dictionary<Transform, bool>(4096);
			int renderersUsingPlantMaterials = 0;
			int skippedSampleFailures = 0;
			int progressStride = Mathf.Max(1, transforms.Count / ProgressUpdateCount);

			for (int i = 0; i < transforms.Count; i++)
			{
				if ((i % progressStride) == 0)
				{
					EditorUtility.DisplayProgressBar(
						"Plant Material Shade Tool",
						$"Sampling foliage {i + 1}/{transforms.Count}",
						transforms.Count == 0 ? 1f : (float)i / transforms.Count);
				}

				Transform transform = transforms[i];
				if (transform == null)
					continue;

				MeshRenderer renderer = transform.GetComponent<MeshRenderer>();
				if (renderer == null)
					continue;

				Material[] currentMaterials = renderer.sharedMaterials;
				if (currentMaterials == null || currentMaterials.Length == 0)
					continue;

				if (!RendererUsesDiscoveredMaterial(currentMaterials, materialLookup))
					continue;

				renderersUsingPlantMaterials++;

				Transform sampleTransform = GetPlantSampleTransform(renderer, root);
				if (!shadowByPlantRoot.TryGetValue(sampleTransform, out bool isInShadow))
				{
					if (!TrySamplePlantShadow(terrainSampler, sampleTransform, renderer, materialLookup, shadowThreshold, out isInShadow, out _))
					{
						skippedSampleFailures++;
						continue;
					}

					shadowByPlantRoot.Add(sampleTransform, isInShadow);
				}

				Material[] replacementMaterials = null;
				for (int materialIndex = 0; materialIndex < currentMaterials.Length; materialIndex++)
				{
					Material currentMaterial = currentMaterials[materialIndex];
					if (currentMaterial == null || !materialLookup.TryGetValue(currentMaterial, out PlantTypeEntry entry))
						continue;

					Material targetMaterial = isInShadow ? entry.shadowMaterial : entry.lightMaterial;
					if (targetMaterial == null || targetMaterial == currentMaterial)
						continue;

					if (replacementMaterials == null)
					{
						replacementMaterials = (Material[])currentMaterials.Clone();
					}

					replacementMaterials[materialIndex] = targetMaterial;
				}

				if (replacementMaterials != null)
				{
					pendingChanges.Add(new RendererMaterialChange(renderer, replacementMaterials));
				}
			}

			if (pendingChanges.Count == 0)
			{
				ShowNotification(new GUIContent("No renderer materials needed changing."));
				Debug.Log(
					$"PlantMaterialShadeTool: no renderer materials needed changing. " +
					$"Matched {renderersUsingPlantMaterials}, skipped {skippedSampleFailures} sample failure(s).");
				return;
			}

			UnityEngine.Object[] undoObjects = new UnityEngine.Object[pendingChanges.Count];
			for (int i = 0; i < pendingChanges.Count; i++)
			{
				undoObjects[i] = pendingChanges[i].renderer;
			}

			Undo.IncrementCurrentGroup();
			undoGroup = Undo.GetCurrentGroup();
			undoGroupStarted = true;
			Undo.SetCurrentGroupName(UndoName);
			Undo.RecordObjects(undoObjects, UndoName);

			int applyStride = Mathf.Max(1, pendingChanges.Count / ProgressUpdateCount);
			for (int i = 0; i < pendingChanges.Count; i++)
			{
				if ((i % applyStride) == 0)
				{
					EditorUtility.DisplayProgressBar(
						"Plant Material Shade Tool",
						$"Applying material changes {i + 1}/{pendingChanges.Count}",
						(float)i / pendingChanges.Count);
				}

				RendererMaterialChange change = pendingChanges[i];
				if (change.renderer == null)
					continue;

				change.renderer.sharedMaterials = change.materials;
				EditorUtility.SetDirty(change.renderer);

				if (PrefabUtility.IsPartOfPrefabInstance(change.renderer))
				{
					PrefabUtility.RecordPrefabInstancePropertyModifications(change.renderer);
				}
			}

			if (root.gameObject.scene.IsValid())
			{
				EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
			}

			ShowNotification(new GUIContent($"Updated {pendingChanges.Count} renderer(s)."));

			Debug.Log(
				$"PlantMaterialShadeTool: updated {pendingChanges.Count} renderer(s), " +
				$"matched {renderersUsingPlantMaterials}, skipped {skippedSampleFailures} sample failure(s).");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			EditorUtility.DisplayDialog("Plant Material Shade Tool", exception.Message, "OK");
		}
		finally
		{
			if (undoGroupStarted)
			{
				Undo.CollapseUndoOperations(undoGroup);
			}

			terrainSampler?.Dispose();
			EditorUtility.ClearProgressBar();
		}
	}

	private bool CanProcess()
	{
		return ResolveRootTransform(false) != null &&
			   HasTerrainMeshes() &&
			   plantTypes != null &&
			   plantTypes.Count > 0 &&
			   !HasMissingMaterials();
	}

	private bool HasTerrainMeshes()
	{
		if (terrainMeshObjects == null)
			return false;

		for (int i = 0; i < terrainMeshObjects.Count; i++)
		{
			if (terrainMeshObjects[i] != null)
				return true;
		}

		return false;
	}

	private bool HasMissingMaterials()
	{
		if (plantTypes == null || plantTypes.Count == 0)
			return false;

		for (int i = 0; i < plantTypes.Count; i++)
		{
			PlantTypeEntry entry = plantTypes[i];
			if (entry == null || !entry.HasBothMaterials)
				return true;
		}

		return false;
	}

	private Transform ResolveRootTransform(bool logErrors)
	{
		if (parentHierarchy == null)
			return null;

		if (parentHierarchy is Transform transform)
			return transform;

		if (parentHierarchy is GameObject gameObject)
			return gameObject.transform;

		if (logErrors)
		{
			Debug.LogError("PlantMaterialShadeTool: Parent Hierarchy must be a Transform or GameObject.");
		}

		return null;
	}

	private static void GatherTransforms(Transform root, List<Transform> result)
	{
		result.Clear();
		if (root == null)
			return;

		var stack = new Stack<Transform>(1024);
		stack.Push(root);

		while (stack.Count > 0)
		{
			Transform current = stack.Pop();
			if (current == null)
				continue;

			result.Add(current);

			for (int childIndex = current.childCount - 1; childIndex >= 0; childIndex--)
			{
				stack.Push(current.GetChild(childIndex));
			}
		}
	}

	private static GameObject GetPrefabAssetRoot(GameObject instanceObject)
	{
		if (instanceObject == null)
			return null;

		GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(instanceObject);
		if (instanceRoot == null)
			return null;

		GameObject prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
		if (prefabAsset == null)
		{
			prefabAsset = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instanceRoot);
		}

		return prefabAsset;
	}

	private static string GetPrefabKey(GameObject prefabAsset)
	{
		string assetPath = AssetDatabase.GetAssetPath(prefabAsset);
		return string.IsNullOrEmpty(assetPath) ? prefabAsset.GetInstanceID().ToString() : assetPath;
	}

	private static void TryPopulateMaterialPair(PlantTypeEntry entry, MeshRenderer instanceRenderer)
	{
		if (entry == null || instanceRenderer == null)
			return;

		MeshRenderer sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(instanceRenderer);
		Material representativeMaterial = FindFirstSharedMaterial(sourceRenderer);
		if (representativeMaterial == null)
		{
			representativeMaterial = FindFirstSharedMaterial(instanceRenderer);
		}

		if (representativeMaterial == null)
			return;

		ResolveMaterialPair(representativeMaterial, out Material lightMaterial, out Material shadowMaterial);
		if (entry.lightMaterial == null)
		{
			entry.lightMaterial = lightMaterial;
		}

		if (entry.shadowMaterial == null)
		{
			entry.shadowMaterial = shadowMaterial;
		}
	}

	private static Material FindFirstSharedMaterial(Renderer renderer)
	{
		if (renderer == null)
			return null;

		Material[] materials = renderer.sharedMaterials;
		if (materials == null)
			return null;

		for (int i = 0; i < materials.Length; i++)
		{
			if (materials[i] != null)
				return materials[i];
		}

		return null;
	}

	private static void ResolveMaterialPair(Material currentMaterial, out Material lightMaterial, out Material shadowMaterial)
	{
		lightMaterial = null;
		shadowMaterial = null;

		if (currentMaterial == null)
			return;

		string assetPath = AssetDatabase.GetAssetPath(currentMaterial);
		string materialName = string.IsNullOrEmpty(assetPath)
			? currentMaterial.name
			: GetFileNameWithoutExtension(assetPath);

		bool currentIsShadow = materialName.EndsWith(ShadeSuffix, StringComparison.OrdinalIgnoreCase);
		if (currentIsShadow)
		{
			shadowMaterial = currentMaterial;
			string lightName = materialName.Substring(0, materialName.Length - ShadeSuffix.Length);
			lightMaterial = LoadSiblingMaterial(assetPath, lightName);
		}
		else
		{
			lightMaterial = currentMaterial;
			shadowMaterial = LoadSiblingMaterial(assetPath, materialName + ShadeSuffix);
		}
	}

	private static Material LoadSiblingMaterial(string sourceAssetPath, string siblingNameWithoutExtension)
	{
		if (string.IsNullOrEmpty(sourceAssetPath) || string.IsNullOrEmpty(siblingNameWithoutExtension))
			return null;

		int slashIndex = sourceAssetPath.LastIndexOf('/');
		string folder = slashIndex >= 0 ? sourceAssetPath.Substring(0, slashIndex + 1) : string.Empty;
		string siblingPath = folder + siblingNameWithoutExtension + ".mat";
		return AssetDatabase.LoadAssetAtPath<Material>(siblingPath);
	}

	private static string GetFileNameWithoutExtension(string assetPath)
	{
		int slashIndex = assetPath.LastIndexOf('/');
		string fileName = slashIndex >= 0 ? assetPath.Substring(slashIndex + 1) : assetPath;
		int dotIndex = fileName.LastIndexOf('.');
		return dotIndex >= 0 ? fileName.Substring(0, dotIndex) : fileName;
	}

	private Dictionary<Material, PlantTypeEntry> BuildMaterialLookup()
	{
		var lookup = new Dictionary<Material, PlantTypeEntry>();
		if (plantTypes == null)
			return lookup;

		for (int i = 0; i < plantTypes.Count; i++)
		{
			PlantTypeEntry entry = plantTypes[i];
			if (entry == null || !entry.HasBothMaterials)
				continue;

			if (!lookup.ContainsKey(entry.lightMaterial))
			{
				lookup.Add(entry.lightMaterial, entry);
			}

			if (!lookup.ContainsKey(entry.shadowMaterial))
			{
				lookup.Add(entry.shadowMaterial, entry);
			}
		}

		return lookup;
	}

	private static bool RendererUsesDiscoveredMaterial(Material[] materials, Dictionary<Material, PlantTypeEntry> materialLookup)
	{
		for (int i = 0; i < materials.Length; i++)
		{
			Material material = materials[i];
			if (material != null && materialLookup.ContainsKey(material))
				return true;
		}

		return false;
	}

	private static Transform GetPlantSampleTransform(MeshRenderer renderer, Transform selectedRoot)
	{
		GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject);
		if (instanceRoot != null && IsSameOrChildOf(instanceRoot.transform, selectedRoot))
			return instanceRoot.transform;

		return renderer.transform;
	}

	private static bool TrySamplePlantShadow(
		TerrainLightmapSamplerSet terrainSampler,
		Transform plantRoot,
		MeshRenderer fallbackRenderer,
		Dictionary<Material, PlantTypeEntry> materialLookup,
		float threshold,
		out bool isInShadow,
		out float luminance)
	{
		isInShadow = false;
		luminance = 1f;

		if (terrainSampler == null || plantRoot == null || fallbackRenderer == null)
			return false;

		Bounds bounds = GetPlantSamplingBounds(plantRoot, fallbackRenderer, materialLookup);
		float sampleY = Mathf.Min(bounds.min.y, plantRoot.position.y);
		Vector3 center = new Vector3(bounds.center.x, sampleY, bounds.center.z);
		float offsetX = Mathf.Max(bounds.extents.x * FootprintSampleScale, MinimumFootprintSampleOffset);
		float offsetZ = Mathf.Max(bounds.extents.z * FootprintSampleScale, MinimumFootprintSampleOffset);

		int sampledCount = 0;
		int shadowSampleCount = 0;
		float luminanceSum = 0f;
		bool centerInShadow = TryAccumulateShadowSample(
			terrainSampler,
			center,
			threshold,
			ref sampledCount,
			ref shadowSampleCount,
			ref luminanceSum,
			out _);

		TryAccumulateShadowSample(terrainSampler, center + new Vector3(offsetX, 0f, 0f), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);
		TryAccumulateShadowSample(terrainSampler, center + new Vector3(-offsetX, 0f, 0f), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);
		TryAccumulateShadowSample(terrainSampler, center + new Vector3(0f, 0f, offsetZ), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);
		TryAccumulateShadowSample(terrainSampler, center + new Vector3(0f, 0f, -offsetZ), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);
		TryAccumulateShadowSample(terrainSampler, center + new Vector3(offsetX, 0f, offsetZ), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);
		TryAccumulateShadowSample(terrainSampler, center + new Vector3(offsetX, 0f, -offsetZ), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);
		TryAccumulateShadowSample(terrainSampler, center + new Vector3(-offsetX, 0f, offsetZ), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);
		TryAccumulateShadowSample(terrainSampler, center + new Vector3(-offsetX, 0f, -offsetZ), threshold, ref sampledCount, ref shadowSampleCount, ref luminanceSum, out _);

		if (sampledCount == 0)
			return false;

		luminance = luminanceSum / sampledCount;
		int requiredShadowSamples = Mathf.Max(1, Mathf.CeilToInt(sampledCount * RegionalShadowSampleRatio));
		isInShadow = centerInShadow || shadowSampleCount >= requiredShadowSamples || luminance <= threshold;
		return true;
	}

	private static Bounds GetPlantSamplingBounds(
		Transform plantRoot,
		MeshRenderer fallbackRenderer,
		Dictionary<Material, PlantTypeEntry> materialLookup)
	{
		Bounds bounds = fallbackRenderer.bounds;
		bool foundPlantRenderer = false;
		MeshRenderer[] renderers = plantRoot.GetComponentsInChildren<MeshRenderer>(true);

		for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
		{
			MeshRenderer renderer = renderers[rendererIndex];
			if (renderer == null || !RendererUsesDiscoveredMaterial(renderer.sharedMaterials, materialLookup))
				continue;

			if (!foundPlantRenderer)
			{
				bounds = renderer.bounds;
				foundPlantRenderer = true;
			}
			else
			{
				bounds.Encapsulate(renderer.bounds);
			}
		}

		return bounds;
	}

	private static bool TryAccumulateShadowSample(
		TerrainLightmapSamplerSet terrainSampler,
		Vector3 worldPosition,
		float threshold,
		ref int sampledCount,
		ref int shadowSampleCount,
		ref float luminanceSum,
		out float luminance)
	{
		if (!terrainSampler.TrySampleShadow(worldPosition, threshold, out bool sampleInShadow, out luminance))
			return false;

		sampledCount++;
		luminanceSum += luminance;

		if (sampleInShadow)
		{
			shadowSampleCount++;
		}

		return sampleInShadow;
	}

	private static bool IsSameOrChildOf(Transform transform, Transform possibleParent)
	{
		Transform current = transform;
		while (current != null)
		{
			if (current == possibleParent)
				return true;

			current = current.parent;
		}

		return false;
	}

	[Serializable]
	private sealed class PlantTypeEntry
	{
		public GameObject prefabAsset;
		public string prefabName;
		public Material lightMaterial;
		public Material shadowMaterial;

		public PlantTypeEntry(GameObject prefabAsset)
		{
			this.prefabAsset = prefabAsset;
			prefabName = prefabAsset != null ? prefabAsset.name : "<missing prefab>";
		}

		public string PrefabName
		{
			get
			{
				if (!string.IsNullOrEmpty(prefabName))
					return prefabName;

				return prefabAsset != null ? prefabAsset.name : "<missing prefab>";
			}
		}

		public bool HasBothMaterials => lightMaterial != null && shadowMaterial != null;

		public string Status
		{
			get
			{
				if (lightMaterial == null)
					return "❌ Missing Light";

				if (shadowMaterial == null)
					return "❌ Missing Shadow";

				return "✔ OK";
			}
		}
	}

	private readonly struct RendererMaterialChange
	{
		public readonly MeshRenderer renderer;
		public readonly Material[] materials;

		public RendererMaterialChange(MeshRenderer renderer, Material[] materials)
		{
			this.renderer = renderer;
			this.materials = materials;
		}
	}

	private sealed class TerrainLightmapSamplerSet : IDisposable
	{
		private readonly List<TerrainLightmapSampler> samplers = new List<TerrainLightmapSampler>();

		public static bool TryCreate(List<GameObject> terrainMeshObjects, out TerrainLightmapSamplerSet samplerSet, out string error)
		{
			samplerSet = null;
			error = null;

			if (terrainMeshObjects == null || terrainMeshObjects.Count == 0)
			{
				error = "Assign at least one Terrain Mesh GameObject first.";
				return false;
			}

			var result = new TerrainLightmapSamplerSet();
			for (int i = 0; i < terrainMeshObjects.Count; i++)
			{
				GameObject terrainMeshObject = terrainMeshObjects[i];
				if (terrainMeshObject == null)
					continue;

				if (!TerrainLightmapSampler.TryCreate(terrainMeshObject, out TerrainLightmapSampler sampler, out string samplerError))
				{
					result.Dispose();
					error = $"Terrain Mesh '{terrainMeshObject.name}': {samplerError}";
					return false;
				}

				result.samplers.Add(sampler);
			}

			if (result.samplers.Count == 0)
			{
				result.Dispose();
				error = "Assign at least one valid Terrain Mesh GameObject.";
				return false;
			}

			samplerSet = result;
			return true;
		}

		public bool TrySampleShadow(Vector3 worldPosition, float threshold, out bool isInShadow, out float luminance)
		{
			for (int i = 0; i < samplers.Count; i++)
			{
				if (samplers[i].TrySampleShadow(worldPosition, threshold, out isInShadow, out luminance))
					return true;
			}

			isInShadow = false;
			luminance = 1f;
			return false;
		}

		public void Dispose()
		{
			for (int i = 0; i < samplers.Count; i++)
			{
				samplers[i]?.Dispose();
			}

			samplers.Clear();
		}
	}

	private sealed class TerrainLightmapSampler : IDisposable
	{
		private const float BarycentricEpsilon = 0.0005f;
		private const int MinimumGridResolution = 16;
		private const int MaximumGridResolution = 256;

		private readonly MeshRenderer terrainRenderer;
		private readonly Mesh terrainMesh;
		private readonly LightmapTextureReader lightmapReader;
		private readonly Matrix4x4 worldToLocal;
		private readonly Vector4 lightmapScaleOffset;
		private readonly Vector3[] vertices;
		private readonly Vector2[] lightmapUvs;
		private readonly int[] triangles;
		private readonly Bounds localBounds;
		private readonly List<int>[] projectedTriangleGrid;
		private readonly int gridWidth;
		private readonly int gridDepth;
		private readonly Vector2 gridMin;
		private readonly Vector2 gridSize;
		private readonly Vector2 cellSize;

		private TerrainLightmapSampler(MeshRenderer terrainRenderer, Mesh terrainMesh, Texture2D lightmapTexture)
		{
			this.terrainRenderer = terrainRenderer;
			this.terrainMesh = terrainMesh;
			lightmapReader = new LightmapTextureReader(lightmapTexture);
			worldToLocal = terrainRenderer.transform.worldToLocalMatrix;
			lightmapScaleOffset = terrainRenderer.lightmapScaleOffset;
			vertices = terrainMesh.vertices;
			triangles = terrainMesh.triangles;
			localBounds = terrainMesh.bounds;

			Vector2[] uv2 = terrainMesh.uv2;
			if (uv2 != null && uv2.Length == vertices.Length)
			{
				lightmapUvs = uv2;
			}
			else
			{
				Vector2[] uv = terrainMesh.uv;
				if (uv != null && uv.Length == vertices.Length)
				{
					lightmapUvs = uv;
					Debug.LogWarning(
						"PlantMaterialShadeTool: terrain mesh has no valid UV2 lightmap channel. " +
						"Falling back to UV0, which is only an approximation for baked lightmap sampling.");
				}
				else
				{
					throw new InvalidOperationException("The terrain mesh has no valid UV2 or UV0 coordinates for lightmap sampling.");
				}
			}

			if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length < 3)
				throw new InvalidOperationException("The terrain mesh has no triangles to sample.");

			gridMin = new Vector2(localBounds.min.x, localBounds.min.z);
			gridSize = new Vector2(localBounds.size.x, localBounds.size.z);
			if (gridSize.x <= Mathf.Epsilon || gridSize.y <= Mathf.Epsilon)
				throw new InvalidOperationException("The terrain mesh bounds are too small in X/Z for projected sampling.");

			int triangleCount = triangles.Length / 3;
			int gridResolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(triangleCount)), MinimumGridResolution, MaximumGridResolution);
			gridWidth = gridResolution;
			gridDepth = gridResolution;
			cellSize = new Vector2(gridSize.x / gridWidth, gridSize.y / gridDepth);
			projectedTriangleGrid = BuildProjectedTriangleGrid();
		}

		public static bool TryCreate(GameObject terrainMeshObject, out TerrainLightmapSampler sampler, out string error)
		{
			sampler = null;
			error = null;

			if (terrainMeshObject == null)
			{
				error = "Assign a Terrain Mesh GameObject first.";
				return false;
			}

			MeshRenderer meshRenderer = terrainMeshObject.GetComponent<MeshRenderer>();
			MeshFilter meshFilter = terrainMeshObject.GetComponent<MeshFilter>();
			if (meshRenderer == null || meshFilter == null)
			{
				error = "The Terrain Mesh object must have both a MeshRenderer and a MeshFilter.";
				return false;
			}

			Mesh mesh = meshFilter.sharedMesh;
			if (mesh == null)
			{
				error = "The Terrain Mesh object has no shared mesh.";
				return false;
			}

			int lightmapIndex = meshRenderer.lightmapIndex;
			LightmapData[] lightmaps = LightmapSettings.lightmaps;
			if (lightmapIndex < 0 || lightmaps == null || lightmapIndex >= lightmaps.Length)
			{
				error = "The Terrain Mesh renderer is not assigned to a baked lightmap. Bake lighting before running this tool.";
				return false;
			}

			Texture2D lightmapTexture = lightmaps[lightmapIndex].lightmapColor;
			if (lightmapTexture == null)
			{
				error = "The Terrain Mesh lightmap has no color texture to sample.";
				return false;
			}

			try
			{
				sampler = new TerrainLightmapSampler(meshRenderer, mesh, lightmapTexture);
				return true;
			}
			catch (Exception exception)
			{
				sampler?.Dispose();
				sampler = null;
				error = exception.Message;
				return false;
			}
		}

		public bool TrySampleShadow(Vector3 worldPosition, float threshold, out bool isInShadow, out float luminance)
		{
			isInShadow = false;
			luminance = 1f;

			if (!TrySampleLightmap(worldPosition, out Color lightmapColor))
				return false;

			luminance = Mathf.Clamp01(CalculateLuminance(lightmapColor));
			isInShadow = luminance <= threshold;
			return true;
		}

		public void Dispose()
		{
			lightmapReader.Dispose();
		}

		private bool TrySampleLightmap(Vector3 worldPosition, out Color color)
		{
			color = Color.white;
			Vector3 localPosition = worldToLocal.MultiplyPoint3x4(worldPosition);

			if (localPosition.x < localBounds.min.x || localPosition.x > localBounds.max.x ||
				localPosition.z < localBounds.min.z || localPosition.z > localBounds.max.z)
			{
				return false;
			}

			if (!TryFindLightmapUv(localPosition, out Vector2 lightmapUv))
				return false;

			Vector2 atlasUv = new Vector2(
				lightmapUv.x * lightmapScaleOffset.x + lightmapScaleOffset.z,
				lightmapUv.y * lightmapScaleOffset.y + lightmapScaleOffset.w);

			atlasUv.x = Mathf.Clamp01(atlasUv.x);
			atlasUv.y = Mathf.Clamp01(atlasUv.y);
			color = lightmapReader.GetPixelBilinear(atlasUv.x, atlasUv.y);
			return true;
		}

		private List<int>[] BuildProjectedTriangleGrid()
		{
			var grid = new List<int>[gridWidth * gridDepth];
			int triangleCount = triangles.Length / 3;

			for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
			{
				int baseIndex = triangleIndex * 3;
				Vector3 a = vertices[triangles[baseIndex]];
				Vector3 b = vertices[triangles[baseIndex + 1]];
				Vector3 c = vertices[triangles[baseIndex + 2]];

				float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
				float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
				float minZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
				float maxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));

				int minCellX = GetClampedCellX(minX);
				int maxCellX = GetClampedCellX(maxX);
				int minCellZ = GetClampedCellZ(minZ);
				int maxCellZ = GetClampedCellZ(maxZ);

				for (int z = minCellZ; z <= maxCellZ; z++)
				{
					for (int x = minCellX; x <= maxCellX; x++)
					{
						int cellIndex = GetCellIndex(x, z);
						if (grid[cellIndex] == null)
						{
							grid[cellIndex] = new List<int>(4);
						}

						grid[cellIndex].Add(triangleIndex);
					}
				}
			}

			return grid;
		}

		private bool TryFindLightmapUv(Vector3 localPosition, out Vector2 lightmapUv)
		{
			lightmapUv = Vector2.zero;

			int cellX = GetClampedCellX(localPosition.x);
			int cellZ = GetClampedCellZ(localPosition.z);
			if (TryFindLightmapUvInCell(cellX, cellZ, localPosition, out lightmapUv))
				return true;

			for (int radius = 1; radius <= 2; radius++)
			{
				int minX = Mathf.Max(0, cellX - radius);
				int maxX = Mathf.Min(gridWidth - 1, cellX + radius);
				int minZ = Mathf.Max(0, cellZ - radius);
				int maxZ = Mathf.Min(gridDepth - 1, cellZ + radius);

				for (int z = minZ; z <= maxZ; z++)
				{
					for (int x = minX; x <= maxX; x++)
					{
						if (x == cellX && z == cellZ)
							continue;

						if (TryFindLightmapUvInCell(x, z, localPosition, out lightmapUv))
							return true;
					}
				}
			}

			// Rare fallback for meshes with unusual projected triangle bounds. This is only used when the grid misses.
			return TryFindBestTriangle(triangles.Length / 3, null, localPosition, out lightmapUv);
		}

		private bool TryFindLightmapUvInCell(int cellX, int cellZ, Vector3 localPosition, out Vector2 lightmapUv)
		{
			lightmapUv = Vector2.zero;
			List<int> cellTriangles = projectedTriangleGrid[GetCellIndex(cellX, cellZ)];
			if (cellTriangles == null || cellTriangles.Count == 0)
				return false;

			return TryFindBestTriangle(cellTriangles.Count, cellTriangles, localPosition, out lightmapUv);
		}

		private bool TryFindBestTriangle(int count, List<int> candidateTriangles, Vector3 localPosition, out Vector2 lightmapUv)
		{
			lightmapUv = Vector2.zero;
			bool found = false;
			float bestHeightDistance = float.PositiveInfinity;
			Vector3 bestBarycentric = Vector3.zero;
			int bestBaseIndex = 0;

			for (int i = 0; i < count; i++)
			{
				int triangleIndex = candidateTriangles == null ? i : candidateTriangles[i];
				int baseIndex = triangleIndex * 3;
				Vector3 a = vertices[triangles[baseIndex]];
				Vector3 b = vertices[triangles[baseIndex + 1]];
				Vector3 c = vertices[triangles[baseIndex + 2]];

				if (!TryGetBarycentricXZ(localPosition, a, b, c, out Vector3 barycentric))
					continue;

				float surfaceY = a.y * barycentric.x + b.y * barycentric.y + c.y * barycentric.z;
				float heightDistance = Mathf.Abs(localPosition.y - surfaceY);
				if (heightDistance < bestHeightDistance)
				{
					found = true;
					bestHeightDistance = heightDistance;
					bestBarycentric = barycentric;
					bestBaseIndex = baseIndex;
				}
			}

			if (!found)
				return false;

			Vector2 uvA = lightmapUvs[triangles[bestBaseIndex]];
			Vector2 uvB = lightmapUvs[triangles[bestBaseIndex + 1]];
			Vector2 uvC = lightmapUvs[triangles[bestBaseIndex + 2]];
			lightmapUv = uvA * bestBarycentric.x + uvB * bestBarycentric.y + uvC * bestBarycentric.z;
			return true;
		}

		private static bool TryGetBarycentricXZ(Vector3 point, Vector3 a, Vector3 b, Vector3 c, out Vector3 barycentric)
		{
			barycentric = Vector3.zero;

			float v0x = b.x - a.x;
			float v0z = b.z - a.z;
			float v1x = c.x - a.x;
			float v1z = c.z - a.z;
			float v2x = point.x - a.x;
			float v2z = point.z - a.z;
			float denominator = v0x * v1z - v1x * v0z;

			if (Mathf.Abs(denominator) <= Mathf.Epsilon)
				return false;

			float beta = (v2x * v1z - v1x * v2z) / denominator;
			float gamma = (v0x * v2z - v2x * v0z) / denominator;
			float alpha = 1f - beta - gamma;

			if (alpha < -BarycentricEpsilon || beta < -BarycentricEpsilon || gamma < -BarycentricEpsilon)
				return false;

			barycentric = new Vector3(alpha, beta, gamma);
			return true;
		}

		private int GetClampedCellX(float localX)
		{
			float normalized = (localX - gridMin.x) / gridSize.x;
			return Mathf.Clamp(Mathf.FloorToInt(normalized * gridWidth), 0, gridWidth - 1);
		}

		private int GetClampedCellZ(float localZ)
		{
			float normalized = (localZ - gridMin.y) / gridSize.y;
			return Mathf.Clamp(Mathf.FloorToInt(normalized * gridDepth), 0, gridDepth - 1);
		}

		private int GetCellIndex(int x, int z)
		{
			return z * gridWidth + x;
		}

		private static float CalculateLuminance(Color color)
		{
			return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
		}
	}

	private sealed class LightmapTextureReader : IDisposable
	{
		private readonly Texture2D texture;
		private readonly bool ownsTexture;

		public LightmapTextureReader(Texture2D sourceTexture)
		{
			if (sourceTexture == null)
				throw new ArgumentNullException(nameof(sourceTexture));

			if (sourceTexture.isReadable)
			{
				texture = sourceTexture;
				ownsTexture = false;
			}
			else
			{
				// Unity does not expose baked shadow maps directly through public editor APIs.
				// The closest robust editor-time signal is the final baked lightmap color on the terrain renderer.
				texture = CreateReadableCopy(sourceTexture);
				ownsTexture = true;
			}
		}

		public Color GetPixelBilinear(float u, float v)
		{
			return texture.GetPixelBilinear(u, v);
		}

		public void Dispose()
		{
			if (ownsTexture && texture != null)
			{
				DestroyImmediate(texture);
			}
		}

		private static Texture2D CreateReadableCopy(Texture2D sourceTexture)
		{
			RenderTexture previousActive = RenderTexture.active;
			RenderTexture temporary = RenderTexture.GetTemporary(
				sourceTexture.width,
				sourceTexture.height,
				0,
				RenderTextureFormat.ARGBHalf,
				RenderTextureReadWrite.Linear);

			try
			{
				Graphics.Blit(sourceTexture, temporary);
				RenderTexture.active = temporary;

				var readableCopy = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBAHalf, false, true);
				readableCopy.name = sourceTexture.name + " Readable Copy";
				readableCopy.ReadPixels(new Rect(0, 0, sourceTexture.width, sourceTexture.height), 0, 0, false);
				readableCopy.Apply(false, false);
				return readableCopy;
			}
			finally
			{
				RenderTexture.active = previousActive;
				RenderTexture.ReleaseTemporary(temporary);
			}
		}
	}
}
