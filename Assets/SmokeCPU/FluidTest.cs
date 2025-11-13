using UnityEngine;

namespace FluidSimCPU
{
	public class FluidTest : MonoBehaviour
	{
		[Header("Grid Settings")]
		public int CellCountX;

		public int CellCountY;
		public float CellSize = 1;

		[Header("Sim Settings")]
		public int solverIterations = 1;

		public float sor = 1.7f;
		public float timeStepMul = 1;

		FluidGrid fluidGrid;
		FluidDrawer fluidDrawer;


		void Start()
		{
			fluidDrawer = GetComponent<FluidDrawer>();
			fluidGrid = new FluidGrid(CellCountX, CellCountY, CellSize);
			fluidDrawer.SetFluidGridToVisualize(fluidGrid);

			Camera.main.orthographicSize = CellCountY * CellSize / 2;

			PrintControls();
		}

		void Update()
		{
			HandleInput();
			RunSim();
		}

		void RunSim()
		{
			// Settings
			fluidGrid.TimeStepMul = timeStepMul;
			fluidGrid.SOR = sor;

			// Pressure Solve
			fluidGrid.RunPressureSolver(solverIterations);
			fluidGrid.UpdateVelocities();

			// Visualize
			fluidDrawer.Visualize();

			// Advection
			fluidGrid.AdvectDye();
			fluidGrid.AdvectVelocity();
		}

		void HandleInput()
		{
			fluidDrawer.HandleInteraction();

			if (Input.GetKeyDown(KeyCode.C))
			{
				Debug.Log("Clear velocities");
				fluidGrid.ClearVelocities();
				fluidGrid.ClearDye();
			}
		}

		void PrintControls()
		{
			Debug.Log("Left Mouse --- Add Velocity");
			Debug.Log("Middle Mouse --- Add Smoke");
			Debug.Log("Scroll Wheel -- Change Radius");
			Debug.Log("Ctrl + C --- Clear");
		}
	}
}