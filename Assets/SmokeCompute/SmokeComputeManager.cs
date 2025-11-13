using System;
using Seb.Helpers;
using Seb.Vis;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace FluidSim
{
	public class SmokeComputeManager : MonoBehaviour
	{
		enum Kernel
		{
			Init,
			UpdateObstacleMap,
			UpdateObstacleEdges,
			Interaction,
			ExternalForces,
			Buoyancy,
			PressureSolvePrepare,
			PressureSolve_RedBlack,
			VelocityPressureUpdate,
			VelocitySelfAdvection,
			AdvectedVelocityReadback,
			SmokeAdvection,
			SmokeDiffusionAndReadback,
			Debug_UpdateDivergenceDisplay,
		}

		public enum DisplayMode
		{
			Smoke,
			Temperature,
			Velocity,
			Divergence,
			Pressure,
			Debug,
		}


		[Header("Domain")]
		public Vector2Int resolution = new(640, 360);

		public float worldWidth = 35;
		public bool openRightEdge;
		public bool openTopEdge;

		[Header("Time")]
		public bool paused;

		public bool useFixedTimeStep;
		public int fixedFrameRate = 60;
		public float timeScale = 1;
		public float timeScaleSlow = 0.25f;
		public int numStepsPerFrame = 1;

		[Header("Pressure Solver")]
		public int pressureSolveIts = 40;

		[Range(1, 1.9f)] public float weightSOR = 1.7f;
		public bool clearPressure = false;

		[Header("External Forces")]
		[Min(0)] public float windX;

		public float interactionRadius = 1;
		public float velocityMultiplier = 0.4f;

		[Header("Smoke")]
		public float temperatureRate;

		public float temperatureDiffusion;
		public float temperatureDecay;
		public float smokeDiffusion;
		public float smokeDecay;
		public float ambientTemperature;
		public float gravity = -1;
		public float buoyancyFactor_temperature;
		public float buoyancyFactor_smoke;

		[Header("Display")]
		public DisplayMode displayMode;

		public float temperatureDisplayFactor = 1;
		public float divergenceDisplayFactor = 1;
		public float smokeDisplayFactor = 1;
		public float displayPressureFactor = 1;
		public float displayVelocityFactor = 1;
		public Color interactCol;
		public Color interactCol_LeftMouse;

		[Header("Debug Info")]
		public Vector2 info_worldSize;

		public float info_cellSize;
		public float info_timeStep;

		[Header("References")]
		public ComputeShader compute;

		public MeshRenderer display;
		public Shader displayShader;

		// Resources
		RenderTexture velocityMap;
		RenderTexture velocityMapAdvected;
		RenderTexture obstacleMap;
		RenderTexture smokeMap;
		RenderTexture smokeMapAdvected;
		RenderTexture pressureMap;
		RenderTexture debugMap;
		RenderTexture pressureSolveData;
		ComputeBuffer elementsBuffer;
		Material displayMat;

		// State
		bool hasInit;
		Vector2 mousePosOld;
		bool pauseNextFrame;
		float simTime = 0;
		SceneElement[] elementsRaw;
		bool inSlowMode;
		static readonly int[] AllKernels = (int[])Enum.GetValues(typeof(Kernel));

		float CellSize => worldWidth / resolution.x;

		void Start()
		{
			PrintControls();
		}


		void Update()
		{
			HandleInput();
			if (!paused) RunCompute();

			UpdateDisplay();
		}

		void HandleInput()
		{
			// Switch display modes with tab
			if (InputHelper.IsKeyDownThisFrame(KeyCode.Tab))
			{
				displayMode = (DisplayMode)(((int)displayMode + 1) % Enum.GetNames(typeof(DisplayMode)).Length);
			}

			if (InputHelper.IsKeyDownThisFrame(KeyCode.Q)) inSlowMode = !inSlowMode;

			if (InputHelper.IsKeyDownThisFrame(KeyCode.Space) || pauseNextFrame)
			{
				paused = !paused;
				pauseNextFrame = false;
			}

			if (InputHelper.IsKeyDownThisFrame(KeyCode.RightArrow))
			{
				paused = false;
				pauseNextFrame = true;
			}

			if (InputHelper.IsMouseInGameWindow()) interactionRadius += InputHelper.MouseScrollDelta.y * 0.1f;
			interactionRadius = Mathf.Max(interactionRadius, 0.1f);
		}

		static void MatchArraySize<T>(ref T[] array, int length)
		{
			if (array == null || array.Length != length)
			{
				array = new T[length];
			}
		}


		void RunCompute()
		{
			info_worldSize = (Vector2)resolution * CellSize;
			info_cellSize = CellSize;
			Bind();

			// Set initial obstacles
			if (!hasInit || (InputHelper.IsKeyDownThisFrame(KeyCode.C) && InputHelper.CtrlIsHeld))
			{
				hasInit = true;
				simTime = 0;
				ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.Init);
			}

			for (int i = 0; i < numStepsPerFrame; i++)
			{
				RunSim();
			}
		}

		void RunSim()
		{
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.ExternalForces);
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.Buoyancy);

			// Player input
			RunInteractionKernel();

			// Update map of which cell edges border an obstacle cell (needs updating since user input can alter)
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.UpdateObstacleMap);
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.UpdateObstacleEdges);

			RunPressureSolveCompute();

			// Update velocities from pressure
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.VelocityPressureUpdate);
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.Debug_UpdateDivergenceDisplay);

			// Advection
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.SmokeAdvection);
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.SmokeDiffusionAndReadback);
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.VelocitySelfAdvection);
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.AdvectedVelocityReadback);
		}

		void RunPressureSolveCompute()
		{
			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.PressureSolvePrepare);

			int fullPassIterationCount = pressureSolveIts * 2;
			int cellCount = resolution.x * resolution.y;
			Debug.Assert(cellCount % 2 == 0, "Cell count expected to be even");

			int halfCellCount = cellCount / 2;
			compute.SetInt("halfCellCount", halfCellCount);

			for (int i = 0; i < fullPassIterationCount; i++)
			{
				compute.SetInt("passIndex", i);
				ComputeHelper.Dispatch(compute, halfCellCount, Kernel.PressureSolve_RedBlack);
			}
		}

		void Bind()
		{
			// Find and update scene elements
			SceneObject[] elementObjects = FindObjectsByType<SceneObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			MatchArraySize(ref elementsRaw, elementObjects.Length);

			for (int i = 0; i < elementsRaw.Length; i++)
			{
				elementsRaw[i] = elementObjects[i].SceneElementData;
			}

			ComputeHelper.CreateStructuredBuffer_DontShrink(ref elementsBuffer, elementsRaw);
			ComputeHelper.SetBuffer(compute, elementsBuffer, "Elements", AllKernels);
			compute.SetInt("ElementCount", elementsRaw.Length);

			// ---- Create texture maps (if not created already) ----
			ComputeHelper.CreateRenderTexture(ref velocityMap, resolution.x, resolution.y, FilterMode.Bilinear, GraphicsFormat.R32G32B32A32_SFloat);
			ComputeHelper.CreateRenderTexture(ref velocityMapAdvected, resolution.x, resolution.y, FilterMode.Bilinear, GraphicsFormat.R32G32B32A32_SFloat);
			ComputeHelper.CreateRenderTexture(ref obstacleMap, resolution.x, resolution.y, FilterMode.Point, GraphicsFormat.R32G32B32A32_SFloat);
			ComputeHelper.CreateRenderTexture(ref smokeMap, resolution.x, resolution.y, FilterMode.Bilinear, GraphicsFormat.R32G32B32A32_SFloat);
			ComputeHelper.CreateRenderTexture(ref smokeMapAdvected, resolution.x, resolution.y, FilterMode.Bilinear, GraphicsFormat.R32G32B32A32_SFloat);

			ComputeHelper.CreateRenderTexture(ref pressureMap, resolution.x, resolution.y, FilterMode.Bilinear, ComputeHelper.R_SFloat);
			ComputeHelper.CreateRenderTexture(ref pressureSolveData, resolution.x, resolution.y, FilterMode.Bilinear, ComputeHelper.RG_SFloat);
			ComputeHelper.CreateRenderTexture(ref debugMap, resolution.x, resolution.y, FilterMode.Point, ComputeHelper.RGBA_SFloat);

			// ---- Bind maps----
			ComputeHelper.SetTexture(compute, velocityMap, "VelocityMap", AllKernels);
			ComputeHelper.SetTexture(compute, velocityMap, "VelocityMapSample", AllKernels);
			ComputeHelper.SetTexture(compute, velocityMapAdvected, "VelocityMapAdvected", AllKernels);
			ComputeHelper.SetTexture(compute, obstacleMap, "ObstacleMap", AllKernels);
			ComputeHelper.SetTexture(compute, smokeMap, "SmokeMap", AllKernels);
			ComputeHelper.SetTexture(compute, smokeMap, "SmokeMapSample", AllKernels);
			ComputeHelper.SetTexture(compute, smokeMapAdvected, "SmokeMapAdvected", AllKernels);
			ComputeHelper.SetTexture(compute, pressureMap, "PressureMap", AllKernels);
			ComputeHelper.SetTexture(compute, pressureSolveData, "PressureSolveData", AllKernels);
			ComputeHelper.SetTexture(compute, debugMap, "DebugMap", AllKernels);

			// ---- Update time ----
			float currTimeScale = inSlowMode ? timeScaleSlow : timeScale;
			float timeStep = (useFixedTimeStep ? 1f / fixedFrameRate : Time.deltaTime) * currTimeScale;
			float unscaledTimeStep = timeStep * currTimeScale;
			info_timeStep = timeStep;
			simTime += timeStep;

			// ---- Set parameters ----
			const float density = 1;
			float k = timeStep / (density * CellSize);
			compute.SetFloat("K", k);

			compute.SetInts("resolution", resolution.x, resolution.y);
			compute.SetVector("worldSize", info_worldSize);
			compute.SetFloat("time", simTime);
			compute.SetFloat("deltaTime", timeStep);
			compute.SetFloat("unscaledDeltaTime", unscaledTimeStep);
			compute.SetFloat("cellSize", CellSize);
			compute.SetFloat("weightSOR", weightSOR);
			compute.SetBool("clearPressure", clearPressure);

			compute.SetFloat("temperatureRate", temperatureRate);
			compute.SetFloat("temperatureDiffusion", temperatureDiffusion);
			compute.SetFloat("temperatureDecay", temperatureDecay);
			compute.SetFloat("smokeDiffusion", smokeDiffusion);
			compute.SetFloat("smokeDecay", smokeDecay);
			compute.SetFloat("gravity", gravity);
			compute.SetFloat("ambientTemperature", ambientTemperature);
			compute.SetFloat("buoyancyFactor_temperature", buoyancyFactor_temperature);
			compute.SetFloat("buoyancyFactor_smoke", buoyancyFactor_smoke);
			compute.SetBool("openRightEdge", openRightEdge);
			compute.SetBool("openTopEdge", openTopEdge);
			compute.SetFloat("windX", windX);
		}

		void RunInteractionKernel()
		{
			compute.SetBool("interaction_SetVelocities", InputHelper.IsMouseHeld(MouseButton.Left));
			compute.SetVector("interaction_Delta", InputHelper.MousePosWorld - mousePosOld);
			compute.SetBool("brushDownThisFrame", InputHelper.IsMouseDownThisFrame(MouseButton.Left));
			compute.SetBool("interaction_AddSmoke", InputHelper.IsMouseHeld(MouseButton.Right));
			compute.SetFloat("interaction_Radius", interactionRadius);
			compute.SetVector("interaction_Centre", InputHelper.MousePosWorld);
			compute.SetFloat("velocityMultiplier", velocityMultiplier);

			ComputeHelper.Dispatch(compute, resolution.x, resolution.y, Kernel.Interaction);
			mousePosOld = InputHelper.MousePosWorld;
		}


		void UpdateDisplay()
		{
			if ((displayMat == null || displayMat.shader != displayShader) && displayShader != null)
			{
				displayMat = new Material(displayShader);
				display.sharedMaterial = displayMat;
			}

			if (displayMat)
			{
				displayMat.SetTexture("ObstacleMap", obstacleMap);
				displayMat.SetTexture("DebugMap", debugMap);
				displayMat.SetTexture("SmokeMap", smokeMap);
				displayMat.SetTexture("VelocityMapA", velocityMap);
				displayMat.SetTexture("VelocityMapB", velocityMapAdvected);
				displayMat.SetTexture("PressureMap", pressureMap);
				displayMat.SetVector("resolution", (Vector2)resolution);
				displayMat.SetInt("displayMode", (int)displayMode);
				displayMat.SetFloat("ambientTemperature", ambientTemperature);
				displayMat.SetFloat("temperatureDisplayFactor", temperatureDisplayFactor);
				displayMat.SetFloat("smokeDisplayFactor", smokeDisplayFactor);
				displayMat.SetFloat("divergenceDisplayFactor", divergenceDisplayFactor);
				displayMat.SetFloat("displayPressureFactor", displayPressureFactor);
				displayMat.SetFloat("displayVelocityFactor", displayVelocityFactor);
				display.enabled = true;
			}
			else
			{
				Debug.Log("Display shader not assigned");
			}

			display.transform.localScale = new Vector3(info_worldSize.x, info_worldSize.y, 1);

			// Draw interaction radius
			Draw.StartLayer(Vector2.zero, 1, false);
			Color col = interactCol;
			if (InputHelper.IsMouseHeld(MouseButton.Left)) col = interactCol_LeftMouse;
			Draw.Point(InputHelper.MousePosWorld, interactionRadius, col);
		}


		public enum ShapeType
		{
			Circle,
			Quad,
			Line
		}

		public struct SceneElement
		{
			public int shapeType;
			public int isObstacle;
			public int isInstantSource; // smoke is set directly to the smoke value, rather added over time
			public Vector2 pos;
			public Vector2 posLineEnd;
			public Vector2 size;
			public Vector2 velocitySource;
			public Vector3 smokeRate;
			public float targetTemperature;
		}

		void OnDestroy()
		{
			ComputeHelper.Release(elementsBuffer);
			ComputeHelper.Release(smokeMapAdvected, smokeMap, velocityMap, velocityMapAdvected, obstacleMap, debugMap, pressureSolveData, pressureMap);
		}


		void PrintControls()
		{
			Debug.Log("Left Mouse -- Add Velocity");
			Debug.Log("Right Mouse -- Add Smoke");
			Debug.Log("Scroll Wheel -- Change Radius");
			Debug.Log("Space -- Pause/Play");
			Debug.Log("Tab -- Change Display Mode");
		}
	}
}