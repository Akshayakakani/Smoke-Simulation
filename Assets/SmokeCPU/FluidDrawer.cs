using System.Collections.Generic;
using Seb.Helpers;
using Seb.Vis;
using UnityEngine;
using static UnityEngine.Mathf;

namespace FluidSimCPU
{

	public class FluidDrawer : MonoBehaviour
	{
		public enum VisMode
		{
			None,
			Divergence,
			Pressure,
			Dye,
			Speed,
		}


		public VisMode visMode;

		[Header("Interaction")]
		public float interactionRadius;

		public float interactStrength;

		public Color interactionCol;
		public Color interactionColActive;

		[Header("Cell")]
		public float cellBorderThickness;

		public bool drawCellOutlineOnly;
		public Color cellCol;
		public Color cellWallCol;
		public Color dyeCol;

		[Header("Velocity")]
		public bool showVelocityComponents;
		public bool showVelocityAtCentre;
		public float edgePointRadius;
		public float arrowLengthFactor;
		public float arrowThickness;
		public Color velXCol;
		public Color velYCol;
		public Color centreVelCol;
		public Gradient speedCol;
		public float speedVisMax;

		[Header("Interpolated Velocity")]
		public bool showInterpolatedAtMouse;

		public bool showInterpolatedGrid;
		public int interpolatedGridResolution;
		public float interpolateGridLenMul = 1;
		public float interpolatedVelThickness;
		public float interpolatedVelDotRadius;
		public Color interpolatedVelCol;

		[Header("Pressure")]
		public float pressureDisplayRange;

		public Color positivePresureCol;
		public Color negativePresureCol;

		[Header("Divergence")]
		public float divergenceDisplayRange;

		public Color positiveDivergenceCol;
		public Color negativeDivergenceCol;
		public float fontSize;
		public float errorFontSize;
		public Color errorTextCol;
		public Vector2 errorTextPos;
		Vector2 cellDisplaySize;

		FluidGrid fluidGrid;
		Vector2 boundsSize;
		Vector2 bottomLeft;
		float halfCellSize;
		Vector2 mousePosStart;

		float mouse_velStartVal;
		float velLengthAnimTime;
		Vector2 mousePosOld;
		bool isInteracting;


		public void SetFluidGridToVisualize(FluidGrid grid)
		{
			fluidGrid = grid;
			cellDisplaySize = Vector2.one * grid.CellSize * (1 - cellBorderThickness);
			boundsSize = new Vector2(grid.CellCountX, grid.CellCountY) * grid.CellSize;
			bottomLeft = -boundsSize / 2;
			halfCellSize = grid.CellSize / 2;
		}

		Vector2 CellCentre(int x, int y) => bottomLeft + new Vector2(x + 0.5f, y + 0.5f) * fluidGrid.CellSize;
		Vector2 LeftEdgeCentre(int x, int y) => CellCentre(x, y) - new Vector2(halfCellSize, 0);
		Vector2 BottomEdgeCentre(int x, int y) => CellCentre(x, y) - new Vector2(0, halfCellSize);

		Vector2Int CellCoordFromPos(Vector2 pos)
		{
			float x = (pos.x - bottomLeft.x) / fluidGrid.CellSize - 0.5f;
			float y = (pos.y - bottomLeft.y) / fluidGrid.CellSize - 0.5f;
			return new Vector2Int(RoundToInt(x), RoundToInt(y));
		}


		public void Visualize()
		{
			Draw.StartLayerIfNotInMatching(Vector2.zero, 1, false);

			// Draw cells
			for (int x = 0; x < fluidGrid.CellCountX; x++)
			{
				for (int y = 0; y < fluidGrid.CellCountY; y++)
				{
					DrawCell(x, y);
				}
			}
			
			// Draw interpolated
			if (showInterpolatedAtMouse)
			{
				Vector2 mousePos = InputHelper.MousePosWorld;
				Vector2 vel = fluidGrid.GetVelocityAtWorldPos(mousePos);
				DrawInterpolatedArrow(mousePos, vel, Color.yellow);
			}

			if (showInterpolatedGrid)
			{
				int nx = (fluidGrid.CellCountX) * interpolatedGridResolution + 1;
				int ny = (fluidGrid.CellCountY) * interpolatedGridResolution + 1;

				for (int x = 0; x < nx; x++)
				{
					for (int y = 0; y < ny; y++)
					{
						float tx = x / (nx - 1f);
						float ty = y / (ny - 1f);
						Vector2 pos = bottomLeft + new Vector2(tx * boundsSize.x, ty * boundsSize.y);
						Vector2 vel = fluidGrid.GetVelocityAtWorldPos(pos);
						DrawInterpolatedArrow(pos, vel * interpolateGridLenMul, interpolatedVelCol);
					}
				}
			}

			if (showVelocityComponents)
			{
				// Draw horizontal velocities
				for (int x = 0; x < fluidGrid.VelocitiesX.GetLength(0); x++)
				{
					for (int y = 0; y < fluidGrid.VelocitiesX.GetLength(1); y++)
					{
						DrawArrow(LeftEdgeCentre(x, y), Vector2.right * fluidGrid.VelocitiesX[x, y], velXCol);
					}
				}

				// Draw vertical velocities
				for (int x = 0; x < fluidGrid.VelocitiesY.GetLength(0); x++)
				{
					for (int y = 0; y < fluidGrid.VelocitiesY.GetLength(1); y++)
					{
						DrawArrow(BottomEdgeCentre(x, y), Vector2.up * fluidGrid.VelocitiesY[x, y], velYCol);
					}
				}
			}

			if (showVelocityAtCentre)
			{

				for (int x = 0; x < fluidGrid.CellCountX; x++)
				{
					for (int y = 0; y < fluidGrid.CellCountY; y++)
					{
						Vector2 pos = CellCentre(x, y);
						Vector2 vel = fluidGrid.GetVelocityAtWorldPos(pos);

						Draw.Arrow(pos, pos + vel * arrowLengthFactor, arrowThickness, arrowThickness * 3.5f, 32, centreVelCol);
					}
				}
			}

			// Draw overlay
			Draw.Point(mousePosOld, interactionRadius, isInteracting ? interactionColActive : interactionCol);
		}


		public void HandleInteraction()
		{
			Vector2 mousePos = InputHelper.MousePosWorld;
			isInteracting = InputHelper.IsMouseHeld(MouseButton.Left);

			if (InputHelper.IsKeyDownThisFrame(KeyCode.Tab))
			{
				visMode = visMode == VisMode.Divergence ? VisMode.Pressure : VisMode.Divergence;
			}


			if (isInteracting)
			{
				Vector2Int centreCoord = CellCoordFromPos(mousePos);

				Vector2 mouseDelta = mousePos - mousePosOld;
				int numCellsHalf = CeilToInt(interactionRadius / fluidGrid.CellSize * 0.5f);
				for (int oy = -numCellsHalf; oy <= numCellsHalf; oy++)
				{
					for (int ox = -numCellsHalf; ox <= numCellsHalf; ox++)
					{
						int x = centreCoord.x + ox;
						int y = centreCoord.y + oy;
						if (x < 0 || x >= fluidGrid.CellCountX || y < 0 || y >= fluidGrid.CellCountY) continue;

						Vector2 cellPos = CellCentre(x, y);
						float weight = 1 - Maths.Clamp01((cellPos - mousePos).sqrMagnitude / (interactionRadius * interactionRadius));


						fluidGrid.VelocitiesX[x, y] += mouseDelta.x * weight * interactStrength;
						fluidGrid.VelocitiesY[x, y] += mouseDelta.y * weight * interactStrength;
					}
				}
			}

			interactionRadius = Mathf.Max(0, interactionRadius + InputHelper.MouseScrollDelta.y * 0.1f);

			if (InputHelper.IsMouseHeld(MouseButton.Middle))
			{
				Vector2Int centreCoord = CellCoordFromPos(mousePos);

				int numCellsHalf = CeilToInt(interactionRadius / fluidGrid.CellSize * 0.5f) * 2;
				for (int oy = -numCellsHalf; oy <= numCellsHalf; oy++)
				{
					for (int ox = -numCellsHalf; ox <= numCellsHalf; ox++)
					{
						int x = centreCoord.x + ox;
						int y = centreCoord.y + oy;
						if (x < 0 || x >= fluidGrid.CellCountX || y < 0 || y >= fluidGrid.CellCountY) continue;
						if (fluidGrid.SolidCellMap[x, y]) continue;

						Vector2 cellPos = CellCentre(x, y);
						float weight = 1 - Maths.Clamp01((cellPos - mousePos).sqrMagnitude / (interactionRadius * interactionRadius));

						fluidGrid.SmokeMap[x, y] = Mathf.Max(fluidGrid.SmokeMap[x, y], Mathf.Pow(weight, 0.25f));
					}
				}
			}


			mousePosOld = mousePos;
		}


		void DrawArrow(Vector2 pos, Vector2 velocity, Color col)
		{
			Draw.Point(pos, edgePointRadius, col);
			Draw.Arrow(pos, pos + velocity * arrowLengthFactor, arrowThickness, arrowThickness * 3.5f, 32, col);
		}

		void DrawInterpolatedArrow(Vector2 pos, Vector2 velocity, Color col)
		{
			Draw.Point(pos, interpolatedVelDotRadius, col);
			Draw.Arrow(pos, pos + velocity, interpolatedVelThickness, interpolatedVelThickness * 3.5f, 32, col);
		}

		void DrawCell(int x, int y)
		{
			Color col = fluidGrid.SolidCellMap[x, y] ? cellWallCol : cellCol;

			if (visMode == VisMode.Divergence)
			{
				float divergence = fluidGrid.CalculateVelocityDivergenceAtCell(x, y);
				float divergenceT = Abs(divergence) / divergenceDisplayRange;
				col = Color.Lerp(col, divergence < 0 ? negativeDivergenceCol : positiveDivergenceCol, divergenceT);
			}
			else if (visMode == VisMode.Pressure)
			{
				float pressure = fluidGrid.PressureMap[x, y];

				float pressureT = Abs(pressure) / pressureDisplayRange;
				col = Color.Lerp(col, pressure < 0 ? negativePresureCol : positivePresureCol, pressureT);
			}
			else if (visMode == VisMode.Dye)
			{
				float dye = fluidGrid.SmokeMap[x, y];
				if (dye > 0)
				{
					col = Color.Lerp(cellCol, dyeCol, dye);
				}
			}
			else if (visMode == VisMode.Speed)
			{
				if (!fluidGrid.SolidCellMap[x, y])
				{
					float speed = fluidGrid.GetVelocityAtWorldPos(fluidGrid.CellCentre(x, y)).magnitude;
					float speedT = speed / speedVisMax;
					col = speedCol.Evaluate(speedT);
				}
			}

			Draw.Quad(CellCentre(x, y), cellDisplaySize, col);
		}

	}
}